using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// Disk store and in-memory catalog of every map this device knows. One JSON file per map
	/// under persistentDataPath/maps/.
	///
	/// Also answers which maps use a given anchor guid, by scanning the loaded catalog rather
	/// than keeping an index — the relation is derivable from the maps' own anchor lists, and a
	/// second copy of it would be a second source of truth to keep correct. It is genuinely
	/// many-to-many: a fork keeps its parent's anchors.
	///
	/// The store never touches the anchor runtime — deleting a map reports which anchor guids
	/// became orphaned so the caller can erase their local saves.
	/// </summary>
	public sealed class MapStore
	{
		private readonly Dictionary<string, GameMap> maps = new();

		private bool loaded;

		public event Action Changed = delegate { };

		public static MapStore Default { get; private set; } =
			new(Path.Combine(Application.persistentDataPath, "maps"));

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init() => Default = new MapStore(Path.Combine(Application.persistentDataPath, "maps"));

		private readonly string directoryPath;
		public MapStore(string directoryPath) => this.directoryPath = directoryPath;
		private string PathFor(string id) => Path.Combine(directoryPath, id + ".json");
		private string TempPathFor(string id) => PathFor(id) + ".tmp";
		private string BackupPathFor(string id) => PathFor(id) + ".bak";

		public IReadOnlyCollection<GameMap> Maps
		{
			get
			{
				EnsureLoaded();
				return GetByLastUsed();
			}
		}

		public bool TryGet(string id, out GameMap map)
		{
			EnsureLoaded();

			if (string.IsNullOrEmpty(id))
			{
				map = null;
				return false;
			}

			if (maps.TryGetValue(id, out GameMap stored))
			{
				map = stored.Clone();
				return true;
			}
			map = null;
			return false;
		}

		/// <summary>Maps ordered most recently used first — the probe and load order.</summary>
		public List<GameMap> GetByLastUsed()
		{
			EnsureLoaded();

			List<GameMap> ordered = new();
			foreach (GameMap map in maps.Values) ordered.Add(map.Clone());
			ordered.Sort((a, b) => b.lastUsed.CompareTo(a.lastUsed));
			return ordered;
		}

		/// <summary>Writes an exact snapshot. A failure leaves the catalog unchanged.</summary>
		public bool Save(GameMap map)
		{
			EnsureLoaded();
			if (map == null || !Guid.TryParseExact(map.id, "N", out _))
				return false;
			GameMap snapshot = map.Clone();
			if (!WriteFile(snapshot)) return false;
			maps[snapshot.id] = snapshot;
			Changed.Invoke();
			return true;
		}

		/// <summary>
		/// Removes the map from disk. Anchors only this map referenced are returned in
		/// <paramref name="orphanedAnchorGuids"/> (when provided) so the caller can erase their
		/// local saves — anchors referenced by any surviving map must stay.
		/// </summary>
		public bool Delete(string id, List<string> orphanedAnchorGuids = null)
		{
			EnsureLoaded();

			if (id == null || !maps.TryGetValue(id, out GameMap removed))
				return false;

			try
			{
				// Recovery artifacts must go first so an interrupted delete never resurrects a
				// map whose primary file was already removed.
				DeleteIfExists(TempPathFor(id));
				DeleteIfExists(BackupPathFor(id));
				DeleteIfExists(PathFor(id));
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return false;
			}

			maps.Remove(id);
			if (orphanedAnchorGuids != null)
				foreach (MapAnchorEntry anchor in removed.anchors)
					if (!IsAnchorReferenced(anchor.guid)) orphanedAnchorGuids.Add(anchor.guid);
			Changed.Invoke();
			return true;
		}

		/// <summary>Whether any known map references this anchor guid.</summary>
		public bool IsAnchorReferenced(string anchorGuid)
		{
			EnsureLoaded();

			foreach (GameMap map in maps.Values)
				if (map.TryGetAnchor(anchorGuid, out _))
					return true;

			return false;
		}

		// ------- disk ----------------------------------------------

		private void EnsureLoaded()
		{
			if (loaded) return;
			loaded = true;

			maps.Clear();

			try
			{
				if (!Directory.Exists(directoryPath))
					return;

				HashSet<string> primaryFiles = new(Directory.GetFiles(directoryPath, "*.json"));

				foreach (string file in Directory.GetFiles(directoryPath, "*.json.bak"))
					primaryFiles.Add(file.Substring(0, file.Length - ".bak".Length));

				foreach (string file in Directory.GetFiles(directoryPath, "*.json.tmp"))
					primaryFiles.Add(file.Substring(0, file.Length - ".tmp".Length));

				foreach (string primaryPath in primaryFiles)
				{
					if (TryReadMap(primaryPath, out GameMap map, out string primaryError))
					{
						maps[map.id] = map;
						continue;
					}

					string backupPath = primaryPath + ".bak";
					if (TryReadMap(backupPath, out map, out string backupError))
					{
						maps[map.id] = map;
						Debug.LogWarning($"Recovered map {map.id} from {backupPath} because " +
							$"the primary file could not be read: {primaryError}");
						continue;
					}

					string tempPath = primaryPath + ".tmp";
					if (TryReadMap(tempPath, out map, out string tempError))
					{
						maps[map.id] = map;
						Debug.LogWarning($"Recovered map {map.id} from {tempPath} because no " +
							"committed copy could be read.");
						continue;
					}

					Debug.LogWarning($"Ignoring unreadable map files for {primaryPath}. " +
						$"Primary: {primaryError}; backup: {backupError}; temporary: {tempError}");
				}
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		private bool WriteFile(GameMap map)
		{
			try
			{
				Directory.CreateDirectory(directoryPath);

				string path = PathFor(map.id);
				string tempPath = TempPathFor(map.id);
				string backupPath = BackupPathFor(map.id);
				string json = JsonUtility.ToJson(map, prettyPrint: true);

				using (FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
				using (StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
				{
					writer.Write(json);
					writer.Flush();
					stream.Flush(flushToDisk: true);
				}

				CommitTempFile(tempPath, path, backupPath);
				return true;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return false;
			}
		}

		private void CommitTempFile(string tempPath, string path, string backupPath)
		{
			if (!File.Exists(path))
			{
				File.Move(tempPath, path);
				return;
			}

			// File.Replace is atomic on filesystems that support it and also preserves the
			// previous committed map as a backup. Some Unity target runtimes do not implement
			// it, so fall back to same-directory renames with equivalent recovery states.
			DeleteIfExists(backupPath);

			try
			{
				File.Replace(tempPath, path, backupPath);
				return;
			}
			catch (PlatformNotSupportedException)
			{
			}
			catch (NotSupportedException)
			{
			}
			catch (IOException) when (File.Exists(tempPath) && File.Exists(path))
			{
				// Some Mono/Android filesystem combinations report an unsupported replacement
				// as IOException rather than PlatformNotSupportedException. Only fall back while
				// both unmodified inputs are still present.
			}

			File.Move(path, backupPath);

			try
			{
				File.Move(tempPath, path);
			}
			catch
			{
				// Restore the last committed version when possible. If restoration itself fails,
				// EnsureLoaded will still find the backup on the next launch.
				if (!File.Exists(path) && File.Exists(backupPath))
					File.Move(backupPath, path);

				throw;
			}
		}

		private bool TryReadMap(string path, out GameMap map, out string error)
		{
			map = null;

			if (!File.Exists(path))
			{
				error = "file does not exist";
				return false;
			}

			try
			{
				string json = File.ReadAllText(path);
				map = JsonUtility.FromJson<GameMap>(json);

				if (map == null || !Guid.TryParseExact(map.id, "N", out _))
				{
					map = null;
					error = "malformed map JSON";
					return false;
				}

				map.objects ??= new();
				map.tags ??= new();
				map.anchors ??= new();
				// Old maps had capability but no preference. Preserve tag maps' authoring flow.
				if (!json.Contains("\"preferredColocationMethod\"") ||
					(map.preferredColocationMethod != ColocationManager.ColocationMethod.MetaSharedAnchor &&
					 map.preferredColocationMethod != ColocationManager.ColocationMethod.AprilTag))
					map.preferredColocationMethod = map.HasTags
						? ColocationManager.ColocationMethod.AprilTag : ColocationManager.ColocationMethod.MetaSharedAnchor;
				if (!Guid.TryParseExact(map.version, "N", out _)) map.version = Guid.NewGuid().ToString("N");
				error = null;
				return true;
			}
			catch (Exception e)
			{
				map = null;
				error = e.Message;
				return false;
			}
		}

		private void DeleteIfExists(string path)
		{
			File.Delete(path);
		}

		public string GenerateName()
		{
			// Choose a name beyond the maps currently in the catalog.
			EnsureLoaded();
			int highest = 0;

			foreach (GameMap map in maps.Values)
			{
				if (map.name == null || !map.name.StartsWith("Map "))
					continue;

				if (int.TryParse(map.name.Substring(4), out int n) && n > highest)
					highest = n;
			}

			return $"Map {highest + 1}";
		}
	}
}
