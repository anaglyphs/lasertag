using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Durable redo record for writes spanning a space and separate layout files.</summary>
	public sealed class MapCatalogJournal
	{
		[Serializable] private sealed class Transaction
		{
			public int schemaVersion = 1;
			public string id;
			public MapSpace space;
			public List<GameMap> maps = new();
			public List<string> deleteMaps = new();
			public bool deleteSpace;
		}
		private readonly MapStore maps;
		private readonly MapSpaceStore spaces;
		private readonly string journal;
		private Transaction pending;
		private bool needsJournalWrite;
		public bool IsBlocked { get; private set; }
		public bool HasPending => pending != null || IsBlocked;
		public MapSpace PendingSpace => pending?.space.Clone();
		public MapCatalogJournal(MapStore maps, MapSpaceStore spaces, string directory)
		{
			this.maps = maps; this.spaces = spaces;
			journal = Path.Combine(directory, "pending-catalog-operation.json");
			LoadPending();
		}
		private void LoadPending()
		{
			IsBlocked = false;
			List<string> unreadable = new();
			foreach (string path in new[] { journal, journal + ".bak", journal + ".tmp" })
			{
				try
				{
					string json = File.ReadAllText(path);
					Transaction candidate = JsonUtility.FromJson<Transaction>(json);
					if (candidate == null || candidate.schemaVersion != 1 || !MapSpace.ValidId(candidate.id) ||
						candidate.space == null || !candidate.space.Validate() || candidate.maps == null ||
						!candidate.maps.TrueForAll(m => m != null && m.Validate()) || candidate.deleteMaps == null ||
						!candidate.deleteMaps.TrueForAll(MapSpace.ValidId)) { unreadable.Add(path); continue; }
					pending = candidate; return;
				}
				catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { }
				catch (Exception e) when (e is IOException or UnauthorizedAccessException)
				{
					// Locked or unreadable: a transaction may be pending that cannot be seen yet. Recover() looks again.
					Debug.LogWarning($"[{nameof(MapCatalogJournal)}] cannot read {Path.GetFileName(path)}: {e.Message}. Catalog writes wait until it clears.");
					IsBlocked = true; return;
				}
				catch (ArgumentException) { unreadable.Add(path); }
			}
			// Garbage no retry can replay. Blocking on it would stop every map save until someone
			// deleted the file by hand, so set it aside and carry on.
			foreach (string path in unreadable) Quarantine(path);
		}
		private static void Quarantine(string path)
		{
			string target = path + ".corrupt";
			try
			{
				File.Delete(target);
				File.Move(path, target);
				Debug.LogError($"[{nameof(MapCatalogJournal)}] {Path.GetFileName(path)} is not a valid catalog transaction; moved to {Path.GetFileName(target)}. The operation it recorded is lost.");
			}
			catch (Exception e) { Debug.LogException(e); }
		}
		public bool Recover()
		{
			if (IsBlocked) LoadPending(); // the lock may have cleared since the last look
			if (IsBlocked) return false;
			if (pending == null) return true;
			if (needsJournalWrite)
			{
				if (!AtomicJsonFile.Write(journal, JsonUtility.ToJson(pending, true))) return false;
				needsJournalWrite = false;
			}
			foreach (GameMap map in pending.maps) if (!maps.Save(map)) return false;
			if (!spaces.Save(pending.space)) return false;
			foreach (string id in pending.deleteMaps) if (!maps.Delete(id)) return false;
			if (pending.deleteSpace && !spaces.Delete(pending.space.id)) return false;
			try
			{
				File.Delete(journal + ".tmp"); File.Delete(journal + ".bak"); File.Delete(journal);
				pending = null; return true;
			}
			catch (Exception e) { Debug.LogException(e); return false; }
		}
		public bool Commit(MapSpace space, IReadOnlyList<GameMap> writes = null,
			IReadOnlyList<string> deletions = null, bool deleteSpace = false)
		{
			if (HasPending || space == null || !space.Validate()) return false;
			Transaction transaction = new() { id = Guid.NewGuid().ToString("N"), space = space.Clone(), deleteSpace = deleteSpace };
			if (writes != null) foreach (GameMap map in writes)
			{
				if (map == null || !map.Validate() || map.storageFrameId != space.storageFrameId || !space.mapIds.Contains(map.id)) return false;
				MapSpace owner = spaces.FindOwner(map.id);
				if (owner != null && owner.id != space.id) return false;
				transaction.maps.Add(map.Clone());
			}
			if (deletions != null) foreach (string id in deletions)
			{
				if (!MapSpace.ValidId(id) || space.mapIds.Contains(id)) return false;
				MapSpace owner = spaces.FindOwner(id);
				if (owner != null && owner.id != space.id) return false;
				transaction.deleteMaps.Add(id);
			}
			pending = transaction; needsJournalWrite = true;
			return Recover();
		}
	}
}
