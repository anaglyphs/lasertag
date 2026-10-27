using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// The space files in one directory: <c>&lt;id&gt;.json</c> for a space, <c>&lt;id&gt;.private.json</c>
	/// for what only this device knows about it, <c>&lt;id&gt;.vmap</c> for its visual map and one
	/// <c>vocabulary.vvoc</c> for the catalog. Every write goes to a <c>.tmp</c> file first and
	/// replaces the old one. One unreadable file is skipped and logged; it never makes the
	/// rest of the catalog unavailable.
	/// </summary>
	public sealed class SpaceStore
	{
		public const string CatalogFolder = "spaces-v3";
		public const string VocabularyFile = "vocabulary.vvoc";
		private const string PrivateSuffix = ".private.json";
		private const string VisualMapExtension = ".vmap";

		public string Directory { get; }

		public SpaceStore(string directory) => Directory = directory ?? throw new ArgumentNullException(nameof(directory));

		public static string DefaultDirectory => Path.Combine(Application.persistentDataPath, CatalogFolder);

		public string SpacePath(string id) => Path.Combine(Directory, id + ".json");
		public string PrivatePath(string id) => Path.Combine(Directory, id + PrivateSuffix);
		public string VisualMapPath(string id) => Path.Combine(Directory, id + VisualMapExtension);
		public string VocabularyPath => Path.Combine(Directory, VocabularyFile);

		public bool HasVisualMap(string id) => Space.ValidId(id) && File.Exists(VisualMapPath(id));

		/// <summary>Whether any space file is in the directory, however readable.</summary>
		public bool IsEmpty
		{
			get
			{
				if (!System.IO.Directory.Exists(Directory)) return true;
				foreach (string path in System.IO.Directory.GetFiles(Directory, "*.json"))
					if (!path.EndsWith(PrivateSuffix, StringComparison.Ordinal)) return false;
				return true;
			}
		}

		/// <summary>Every readable space, most recently used first.</summary>
		public List<Space> LoadAll()
		{
			List<Space> result = new();
			if (!System.IO.Directory.Exists(Directory)) return result;
			string[] paths;
			try { paths = System.IO.Directory.GetFiles(Directory, "*.json"); }
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				Debug.LogException(e);
				return result;
			}
			foreach (string path in paths)
			{
				if (path.EndsWith(PrivateSuffix, StringComparison.Ordinal)) continue;
				Space space = ReadSpace(path);
				if (space != null) result.Add(space);
			}
			result.Sort((a, b) => a.lastUsed == b.lastUsed ? string.CompareOrdinal(a.id, b.id) : b.lastUsed.CompareTo(a.lastUsed));
			return result;
		}

		private static Space ReadSpace(string path)
		{
			string expectedId = Path.GetFileNameWithoutExtension(path);
			Space space;
			try
			{
				space = JsonUtility.FromJson<Space>(File.ReadAllText(path));
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[{nameof(SpaceStore)}] skipped unreadable space file {path}: {e.GetType().Name}: {e.Message}");
				return null;
			}
			if (space == null || space.id != expectedId || !Space.ValidId(space.id))
			{
				Debug.LogWarning($"[{nameof(SpaceStore)}] skipped {path}: it does not describe space {expectedId}");
				return null;
			}
			space.Sanitize();
			return space;
		}

		public bool Save(Space space)
		{
			if (space == null) throw new ArgumentNullException(nameof(space));
			if (!Space.ValidId(space.id)) throw new ArgumentException("A space needs a valid id to be saved.", nameof(space));
			return WriteAtomically(SpacePath(space.id), JsonUtility.ToJson(space, true));
		}

		/// <summary>Removes the space, its visual map and its private file. True when nothing of it is left.</summary>
		public bool Delete(string id)
		{
			if (!Space.ValidId(id)) return false;
			bool success = DeleteVisualMap(id);
			foreach (string path in new[] { SpacePath(id), SpacePath(id) + ".tmp", PrivatePath(id), PrivatePath(id) + ".tmp" })
				success &= TryDelete(path);
			return success;
		}

		private static bool TryDelete(string path)
		{
			try
			{
				File.Delete(path);
				return true;
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				Debug.LogException(e);
				return false;
			}
		}

		/// <summary>Removes a space's visual map and any temp file a write left, leaving the space. True when none is left.</summary>
		public bool DeleteVisualMap(string id)
		{
			if (!Space.ValidId(id)) return false;
			bool success = TryDelete(VisualMapPath(id));
			if (!System.IO.Directory.Exists(Directory)) return success;
			try
			{
				foreach (string temp in System.IO.Directory.GetFiles(Directory, id + VisualMapExtension + ".*tmp")) success &= TryDelete(temp);
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
				Debug.LogException(e);
				success = false;
			}
			return success;
		}

		/// <summary>This device's private references for a space, or null when there are none it can read.</summary>
		public PrivateReferences LoadPrivate(string spaceId)
		{
			if (!Space.ValidId(spaceId)) return null;
			string path = PrivatePath(spaceId);
			if (!File.Exists(path)) return null;
			PrivateReferences references;
			try
			{
				references = JsonUtility.FromJson<PrivateReferences>(File.ReadAllText(path));
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[{nameof(SpaceStore)}] skipped unreadable private file {path}: {e.GetType().Name}: {e.Message}");
				return null;
			}
			if (references == null || references.spaceId != spaceId || !Space.ValidId(references.frameId))
			{
				Debug.LogWarning($"[{nameof(SpaceStore)}] skipped {path}: it does not describe space {spaceId}");
				return null;
			}
			references.Sanitize();
			return references;
		}

		public bool SavePrivate(PrivateReferences references)
		{
			if (references == null) throw new ArgumentNullException(nameof(references));
			if (!Space.ValidId(references.spaceId)) throw new ArgumentException("Private references need a valid space id.", nameof(references));
			return WriteAtomically(PrivatePath(references.spaceId), JsonUtility.ToJson(references, true));
		}

		/// <summary>Writes a <c>.tmp</c> file beside the target and replaces the target with it.</summary>
		public static bool WriteAtomically(string path, string text)
		{
			string temp = path + ".tmp";
			try
			{
				System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
				using (FileStream stream = new(temp, FileMode.Create, FileAccess.Write, FileShare.None))
				using (StreamWriter writer = new(stream, new UTF8Encoding(false)))
				{
					writer.Write(text);
					writer.Flush();
					stream.Flush(true);
				}
				ReplaceFile(temp, path);
				return true;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return false;
			}
		}

		/// <summary>Moves <paramref name="temp"/> over <paramref name="path"/>; the temp file survives a failure.</summary>
		public static void ReplaceFile(string temp, string path)
		{
			if (!File.Exists(path))
			{
				File.Move(temp, path);
				return;
			}
			try
			{
				File.Replace(temp, path, null);
				return;
			}
			catch (Exception e) when (e is PlatformNotSupportedException or NotSupportedException ||
				e is IOException && File.Exists(temp) && File.Exists(path)) { }
			File.Delete(path);
			File.Move(temp, path);
		}
	}
}
