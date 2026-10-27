using System;
using System.Collections.Generic;
using System.IO;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// One-shot import of the <c>map-catalog-v2</c> catalog into a <see cref="SpaceStore"/>: each
	/// space's layout files are embedded as its maps, every stored pose is brought into the
	/// space's aligned frame through <c>canonicalFromStorage</c>, the device's tag anchors move
	/// to the private file, and the visual maps and vocabulary are copied beside the new files.
	/// Retained reference sets, associations and known frames are not carried over: a space
	/// adopted from a host becomes a plain local space. The old catalog is left in place.
	/// Delete this class one release after it ships.
	/// </summary>
	public static class CatalogV2Migrator
	{
		public const string V2Folder = "map-catalog-v2";
		public const string MarkerFile = "migrated-from-v2";

		public static string DefaultV2Root => Path.Combine(Application.persistentDataPath, V2Folder);

		/// <summary>There is an old catalog, the new one is empty, and no migration has run yet.</summary>
		public static bool NeedsMigration(string v2Root, SpaceStore target) =>
			Directory.Exists(Path.Combine(v2Root, "spaces")) && target.IsEmpty &&
			!File.Exists(Path.Combine(target.Directory, MarkerFile));

		/// <summary>Imports every readable v2 space. Returns how many were written.</summary>
		public static int Migrate(string v2Root, SpaceStore target)
		{
			string spacesDirectory = Path.Combine(v2Root, "spaces");
			string mapsDirectory = Path.Combine(v2Root, "maps");
			Directory.CreateDirectory(target.Directory);
			int migrated = 0;
			foreach (string path in Directory.GetFiles(spacesDirectory, "*.json"))
			{
				string id = Path.GetFileNameWithoutExtension(path);
				V2Space source = ReadV2<V2Space>(path, id);
				if (source == null) continue;
				try
				{
					if (Convert(source, mapsDirectory, target)) migrated++;
				}
				catch (Exception e)
				{
					Debug.LogWarning($"[{nameof(CatalogV2Migrator)}] space {id} was not migrated: {e.GetType().Name}: {e.Message}");
				}
			}
			string vocabulary = Path.Combine(spacesDirectory, SpaceStore.VocabularyFile);
			if (File.Exists(vocabulary) && !File.Exists(target.VocabularyPath)) Copy(vocabulary, target.VocabularyPath);
			File.WriteAllText(Path.Combine(target.Directory, MarkerFile), DateTime.UtcNow.ToString("o"));
			Debug.Log($"[{nameof(CatalogV2Migrator)}] migrated {migrated} space(s) from {v2Root} to {target.Directory}");
			return migrated;
		}

		private static bool Convert(V2Space source, string mapsDirectory, SpaceStore target)
		{
			if (!Space.ValidId(source.canonicalFrameId) || !Space.ValidPose(source.canonicalFromStorage))
			{
				Debug.LogWarning($"[{nameof(CatalogV2Migrator)}] space {source.id} has no usable frame: skipped");
				return false;
			}
			Pose offset = source.canonicalFromStorage;
			Space space = new()
			{
				id = source.id, name = source.name, frameId = source.canonicalFrameId, lastUsed = source.lastUsed,
				alignment = new AlignmentSettings
				{
					method = ColocationManager.MigrateLegacyMethod(source.preferredColocationMethod) is var method && method == Method.ScanRegistration
						? Method.Visual : method,
					tagSizeCm = source.tagSizeCm, firstTagId = source.firstTagId, secondTagId = source.secondTagId
				}
			};
			foreach (V2Tag tag in source.tags ?? new()) space.tags.Add(new TagRef { id = tag.id, pose = Compose(offset, tag.canonPose) });
			foreach (V2Anchor anchor in source.anchors ?? new())
				space.anchors.Add(new AnchorRef { guid = anchor.guid, pose = Compose(offset, anchor.canonPose), tagId = anchor.tagId });
			foreach (string mapId in source.mapIds ?? new())
			{
				V2Map layout = ReadV2<V2Map>(Path.Combine(mapsDirectory, mapId + ".json"), mapId);
				if (layout == null) continue;
				if (layout.storageFrameId != source.storageFrameId)
				{
					Debug.LogWarning($"[{nameof(CatalogV2Migrator)}] map {mapId} is in another frame than space {source.id}: skipped");
					continue;
				}
				Map map = new() { id = layout.id, name = layout.name, lastUsed = layout.lastUsed };
				foreach (V2Object entry in layout.objects ?? new())
					map.objects.Add(new MapObjectRecord { prefabId = entry.prefabId, pose = Compose(offset, entry.pose) });
				space.maps.Add(map);
			}
			space.Sanitize();
			if (!target.Save(space)) return false;

			if (source.localAnchors != null && source.localAnchors.Count > 0)
			{
				PrivateReferences references = PrivateReferences.Create(space);
				foreach (V2Anchor anchor in source.localAnchors)
					references.tagAnchors.Add(new TagAnchorRef
					{
						guid = anchor.guid, tagId = anchor.tagId, pose = Compose(offset, anchor.canonPose),
						tagSizeCm = anchor.tagSizeCm, tagPose = Compose(offset, anchor.tagCanonPose)
					});
				references.Sanitize();
				if (references.tagAnchors.Count > 0) target.SavePrivate(references);
			}

			string visualMap = Path.Combine(Path.GetDirectoryName(mapsDirectory) ?? "", "spaces", source.id + VisualMapFile.Extension);
			if (File.Exists(visualMap) && !File.Exists(target.VisualMapPath(space.id)))
				CopyVisualMap(visualMap, target.VisualMapPath(space.id), source, offset);
			return true;
		}

		/// <summary>The saved map is in the storage frame; the new space's frame is the canonical one.</summary>
		private static void CopyVisualMap(string from, string to, V2Space source, Pose offset)
		{
			bool identity = source.storageFrameId == source.canonicalFrameId &&
				offset.position == Vector3.zero && Quaternion.Angle(offset.rotation, Quaternion.identity) < .001f;
			if (identity)
			{
				Copy(from, to);
				return;
			}
			VisualMapData data;
			using (FileStream f = new(from, FileMode.Open, FileAccess.Read)) data = VisualMapFile.Read(f);
			if (data == null || data.frameId != source.storageFrameId)
			{
				Debug.LogWarning($"[{nameof(CatalogV2Migrator)}] the visual map of space {source.id} is not in its storage frame: not migrated");
				return;
			}
			foreach (VisualMapData.Keyframe keyframe in data.keyframes) keyframe.camera = Compose(offset, keyframe.camera);
			for (int i = 0; i < data.landmarks.Count; i++)
			{
				VisualMapData.Landmark landmark = data.landmarks[i];
				landmark.origin = (float3)(offset.position + offset.rotation * (Vector3)landmark.origin);
				landmark.direction = (float3)(offset.rotation * (Vector3)landmark.direction);
				data.landmarks[i] = landmark;
			}
			data.frameId = source.canonicalFrameId;
			using (FileStream f = new(to + ".tmp", FileMode.Create, FileAccess.Write)) VisualMapFile.Write(data, f);
			SpaceStore.ReplaceFile(to + ".tmp", to);
		}

		private static void Copy(string from, string to)
		{
			File.Copy(from, to + ".tmp", true);
			SpaceStore.ReplaceFile(to + ".tmp", to);
		}

		/// <summary>The primary file, or its backup when the primary is unreadable.</summary>
		private static T ReadV2<T>(string path, string id) where T : V2Document
		{
			foreach (string candidate in new[] { path, path + ".bak" })
			{
				if (!File.Exists(candidate)) continue;
				try
				{
					T document = JsonUtility.FromJson<T>(File.ReadAllText(candidate));
					if (document != null && document.id == id) return document;
					Debug.LogWarning($"[{nameof(CatalogV2Migrator)}] {candidate} does not describe {id}: skipped");
				}
				catch (Exception e)
				{
					Debug.LogWarning($"[{nameof(CatalogV2Migrator)}] {candidate} is unreadable: {e.GetType().Name}: {e.Message}");
				}
			}
			return null;
		}

		private static Pose Compose(Pose a, Pose b) => new(a.position + a.rotation * b.position, a.rotation * b.rotation);

		// the v2 shapes, as far as this reads them; everything else in the files is ignored
		[Serializable] private class V2Document { public string id; }
		[Serializable] private class V2Space : V2Document
		{
			public string name;
			public string storageFrameId;
			public string canonicalFrameId;
			public Pose canonicalFromStorage = Pose.identity;
			public long lastUsed;
			public List<string> mapIds = new();
			public List<V2Tag> tags = new();
			public List<V2Anchor> anchors = new();
			public List<V2Anchor> localAnchors = new();
			public float tagSizeCm = Space.DefaultTagSizeCm;
			public Method preferredColocationMethod;
			public int firstTagId = -1;
			public int secondTagId = -1;
		}
		[Serializable] private class V2Map : V2Document
		{
			public string name;
			public string storageFrameId;
			public long lastUsed;
			public List<V2Object> objects = new();
		}
		[Serializable] private struct V2Tag { public int id; public Pose canonPose; }
		[Serializable] private struct V2Anchor { public string guid; public Pose canonPose; public int tagId; public float tagSizeCm; public Pose tagCanonPose; }
		[Serializable] private struct V2Object { public string prefabId; public Pose pose; }
	}
}
