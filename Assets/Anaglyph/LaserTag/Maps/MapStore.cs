using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Anaglyph.Lasertag
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
	public static class MapStore
	{
		private static readonly Dictionary<string, GameMap> maps = new();

		private static bool loaded;

		public static event Action Changed = delegate { };

		// Statics persist across play sessions while domain reload is disabled.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init()
		{
			maps.Clear();
			loaded = false;
			Changed = delegate { };
		}

		private static string DirectoryPath => Path.Combine(Application.persistentDataPath, "maps");
		private static string PathFor(string id) => Path.Combine(DirectoryPath, id + ".json");
		private static string TempPathFor(string id) => PathFor(id) + ".tmp";
		private static string BackupPathFor(string id) => PathFor(id) + ".bak";

		public static IReadOnlyCollection<GameMap> Maps
		{
			get
			{
				EnsureLoaded();
				return maps.Values;
			}
		}

		public static bool TryGet(string id, out GameMap map)
		{
			EnsureLoaded();

			if (string.IsNullOrEmpty(id))
			{
				map = null;
				return false;
			}

			return maps.TryGetValue(id, out map);
		}

		/// <summary>Maps ordered most recently used first — the probe and load order.</summary>
		public static List<GameMap> GetByLastUsed()
		{
			EnsureLoaded();

			List<GameMap> ordered = new(maps.Values);
			ordered.Sort((a, b) => b.lastUsed.CompareTo(a.lastUsed));
			return ordered;
		}

		public static GameMap CreateNew()
		{
			EnsureLoaded();

			GameMap map = new()
			{
				id = Guid.NewGuid().ToString("N"),
				name = GenerateName(),
				version = Guid.NewGuid().ToString("N"),
				baseVersion = "",
				dirty = false,
				lastUsed = DateTime.UtcNow.Ticks,
				lastEdited = DateTime.UtcNow.Ticks,
			};

			maps[map.id] = map;
			WriteFile(map);
			Changed.Invoke();
			return map;
		}

		/// <summary>
		/// Clones a locally edited copy under a new id before its original id is replaced by a
		/// received version. The fork keeps its parent's anchors (so both localize in the same
		/// room and most-recently-used picks between them), keeps its lineage's baseVersion, and
		/// stays dirty — it IS the local edits.
		/// </summary>
		public static GameMap Fork(GameMap source)
		{
			EnsureLoaded();

			GameMap fork = new()
			{
				id = Guid.NewGuid().ToString("N"),
				name = source.name + " (fork)",
				version = Guid.NewGuid().ToString("N"),
				baseVersion = source.baseVersion,
				dirty = source.dirty,
				lastUsed = source.lastUsed,
				lastEdited = source.lastEdited,
				objects = new List<MapObjectEntry>(source.objects),
				anchors = new List<MapAnchorEntry>(source.anchors),
				tags = new List<MapTagEntry>(source.tags),
			};

			maps[fork.id] = fork;
			WriteFile(fork);
			Changed.Invoke();
			return fork;
		}

		/// <summary>
		/// Persists the map. A dirty save mints a new content version, so a peer that later
		/// receives this copy can tell it apart from the version it derives from.
		/// </summary>
		public static void Save(GameMap map)
		{
			EnsureLoaded();

			if (map.dirty)
				MintVersion(map);

			maps[map.id] = map;
			WriteFile(map);
			Changed.Invoke();
		}

		/// <summary>
		/// Removes the map from disk. Anchors only this map referenced are returned in
		/// <paramref name="orphanedAnchorGuids"/> (when provided) so the caller can erase their
		/// local saves — anchors referenced by any surviving map must stay.
		/// </summary>
		public static void Delete(string id, List<string> orphanedAnchorGuids = null)
		{
			EnsureLoaded();

			if (!maps.Remove(id, out GameMap removed))
				return;

			if (orphanedAnchorGuids != null)
				foreach (MapAnchorEntry anchor in removed.anchors)
					if (!IsAnchorReferenced(anchor.guid))
						orphanedAnchorGuids.Add(anchor.guid);

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
			}

			Changed.Invoke();
		}

		/// <summary>Whether any known map references this anchor guid.</summary>
		public static bool IsAnchorReferenced(string anchorGuid)
		{
			EnsureLoaded();

			foreach (GameMap map in maps.Values)
				if (map.TryGetAnchor(anchorGuid, out _))
					return true;

			return false;
		}

		/// <summary>All maps that reference this anchor guid — the reverse index lookup.</summary>
		public static List<GameMap> MapsUsingAnchor(string anchorGuid)
		{
			EnsureLoaded();

			List<GameMap> result = new();
			foreach (GameMap map in maps.Values)
				if (map.TryGetAnchor(anchorGuid, out _))
					result.Add(map);

			return result;
		}

		public static void MarkUsed(GameMap map)
		{
			map.lastUsed = DateTime.UtcNow.Ticks;
			Save(map);
		}

		/// <summary>
		/// Mints a new content version, so a peer receiving this copy can tell it apart from the
		/// version it derives from. <see cref="Save"/> does this itself for a dirty map; call it
		/// directly only to settle the version before something else advertises it.
		/// </summary>
		public static void MintVersion(GameMap map)
		{
			map.version = Guid.NewGuid().ToString("N");
		}

		/// <summary>Stamp a local edit: sets dirty, so this copy forks instead of being replaced.</summary>
		public static void MarkEdited(GameMap map)
		{
			map.dirty = true;
			map.lastEdited = DateTime.UtcNow.Ticks;
		}

		// ------- disk ----------------------------------------------

		private static void EnsureLoaded()
		{
			if (loaded) return;
			loaded = true;

			maps.Clear();

			try
			{
				if (!Directory.Exists(DirectoryPath))
					return;

				HashSet<string> primaryFiles = new(Directory.GetFiles(DirectoryPath, "*.json"));

				foreach (string file in Directory.GetFiles(DirectoryPath, "*.json.bak"))
					primaryFiles.Add(file.Substring(0, file.Length - ".bak".Length));

				foreach (string file in Directory.GetFiles(DirectoryPath, "*.json.tmp"))
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

		private static void WriteFile(GameMap map)
		{
			try
			{
				Directory.CreateDirectory(DirectoryPath);

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
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		private static void CommitTempFile(string tempPath, string path, string backupPath)
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

		private static bool TryReadMap(string path, out GameMap map, out string error)
		{
			map = null;

			if (!File.Exists(path))
			{
				error = "file does not exist";
				return false;
			}

			try
			{
				map = JsonUtility.FromJson<GameMap>(File.ReadAllText(path));

				if (map == null || string.IsNullOrEmpty(map.id))
				{
					map = null;
					error = "malformed map JSON";
					return false;
				}

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

		private static void DeleteIfExists(string path)
		{
			if (File.Exists(path))
				File.Delete(path);
		}

		private static string GenerateName()
		{
			// "Map N", where N clears every number any existing map ever used — deletes must
			// not cause reuse.
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
