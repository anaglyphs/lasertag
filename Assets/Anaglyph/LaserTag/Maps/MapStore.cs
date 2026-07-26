using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Disk store and in-memory catalog of every map this device knows. One JSON file per map
	/// under persistentDataPath/maps/.
	///
	/// Also owns the reverse index (anchor guid → maps using it). The index is derivable from
	/// the maps' own anchor lists, so it is rebuilt in memory rather than persisted as a second
	/// source of truth. It is genuinely many-to-many: a fork keeps its parent's anchors.
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
				map.version = Guid.NewGuid().ToString("N");

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
				string path = PathFor(id);
				if (File.Exists(path))
					File.Delete(path);
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

				foreach (string file in Directory.GetFiles(DirectoryPath, "*.json"))
				{
					try
					{
						GameMap map = JsonUtility.FromJson<GameMap>(File.ReadAllText(file));

						if (map == null || string.IsNullOrEmpty(map.id))
						{
							Debug.LogWarning($"Ignoring malformed map file {file}");
							continue;
						}

						maps[map.id] = map;
					}
					catch (Exception e)
					{
						Debug.LogWarning($"Ignoring unreadable map file {file}: {e.Message}");
					}
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
				File.WriteAllText(PathFor(map.id), JsonUtility.ToJson(map, prettyPrint: true));
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
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
