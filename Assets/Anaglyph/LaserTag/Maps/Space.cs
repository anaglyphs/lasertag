using System;
using System.Collections.Generic;
using UnityEngine;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// A physical room as this device knows it: the references that align to it (tags,
	/// shared anchors, and a visual map kept beside the file), the alignment settings, and
	/// the maps arranged in it. Poses are in the space's frame, which is the aligned world
	/// frame. <see cref="frameId"/> changes whenever stored poses stop meaning the same place.
	/// </summary>
	[Serializable]
	public class Space
	{
		public const int SchemaVersion = 3;
		public const float DefaultTagSizeCm = 10f;
		public const int MaxNameLength = 40;

		public int schemaVersion = SchemaVersion;
		public string id;
		public string name;
		public string frameId;
		public long lastUsed;
		public AlignmentSettings alignment = AlignmentSettings.Default;
		public List<TagRef> tags = new();
		public List<AnchorRef> anchors = new();
		public List<Map> maps = new();

		/// <summary>References the headset observes. A saved visual map is a file question: see the store.</summary>
		public bool HasReferences => tags.Count > 0 || anchors.Count > 0;
		public bool HasTags => tags.Count > 0;
		public Guid Id => Guid.TryParseExact(id, "N", out Guid guid) ? guid : Guid.Empty;
		public Guid FrameId => Guid.TryParseExact(frameId, "N", out Guid guid) ? guid : Guid.Empty;
		public SpaceFrame Frame => new(Id, FrameId);

		public static string NewId() => Guid.NewGuid().ToString("N");

		public static Space Create(string name) => new()
		{
			id = NewId(), name = name ?? "", frameId = NewId(), lastUsed = DateTime.UtcNow.Ticks
		};

		public Map FindMap(string mapId)
		{
			foreach (Map map in maps) if (map.id == mapId) return map;
			return null;
		}

		public bool TryGetTag(int tagId, out TagRef tag)
		{
			foreach (TagRef entry in tags) if (entry.id == tagId) { tag = entry; return true; }
			tag = default; return false;
		}

		public void SetTag(int tagId, Pose pose)
		{
			int index = tags.FindIndex(t => t.id == tagId);
			TagRef entry = new() { id = tagId, pose = pose };
			if (index < 0) tags.Add(entry); else tags[index] = entry;
		}

		public void SetAnchor(string guid, Pose pose, int tagId = -1)
		{
			int index = anchors.FindIndex(a => a.guid == guid);
			AnchorRef entry = new() { guid = guid, pose = pose, tagId = tagId };
			if (index < 0) anchors.Add(entry); else anchors[index] = entry;
		}

		/// <summary>
		/// Makes a loaded or received document usable: each bad entry is dropped with a warning
		/// and the rest is kept. Only <see cref="id"/> is beyond repair; the caller checks it.
		/// </summary>
		public void Sanitize()
		{
			string where = $"space {id}";
			schemaVersion = SchemaVersion;
			name ??= "";
			if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);
			if (!ValidId(frameId))
			{
				frameId = NewId();
				Debug.LogWarning($"[{nameof(Space)}] {where} had no valid frame id: assigned a new one");
			}
			if (lastUsed < 0) lastUsed = 0;
			alignment.Sanitize(where);
			tags ??= new();
			anchors ??= new();
			maps ??= new();

			HashSet<int> tagIds = new();
			DropWhere(tags, where, "tag", tag => tag.id < 0 ? "negative id" : !ValidPose(tag.pose) ? "invalid pose"
				: !tagIds.Add(tag.id) ? "duplicate id" : null);
			HashSet<string> anchorIds = new();
			for (int i = 0; i < anchors.Count; i++)
			{
				AnchorRef anchor = anchors[i];
				if (Guid.TryParse(anchor.guid, out Guid guid) && guid != Guid.Empty) { anchor.guid = guid.ToString("N"); anchors[i] = anchor; }
			}
			DropWhere(anchors, where, "anchor", anchor => !ValidId(anchor.guid) ? "invalid guid" : !ValidPose(anchor.pose) ? "invalid pose"
				: anchor.tagId < -1 ? "invalid tag id" : !anchorIds.Add(anchor.guid) ? "duplicate guid" : null);
			HashSet<string> mapIds = new();
			DropWhere(maps, where, "map", map => map == null ? "null" : !ValidId(map.id) ? "invalid id" : !mapIds.Add(map.id) ? "duplicate id" : null);
			foreach (Map map in maps) map.Sanitize($"{where}, map {map.id}");
		}

		internal static void DropWhere<T>(List<T> list, string where, string kind, Func<T, string> problem)
		{
			for (int i = list.Count - 1; i >= 0; i--)
			{
				string why = problem(list[i]);
				if (why == null) continue;
				Debug.LogWarning($"[{nameof(Space)}] {where}: dropped {kind} {i} ({why})");
				list.RemoveAt(i);
			}
		}

		public static bool ValidId(string value) => value != null && Guid.TryParseExact(value, "N", out Guid guid) && guid != Guid.Empty;
		public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
		public static bool ValidPose(Pose pose) => Finite(pose.position.x) && Finite(pose.position.y) && Finite(pose.position.z) &&
			Finite(pose.rotation.x) && Finite(pose.rotation.y) && Finite(pose.rotation.z) && Finite(pose.rotation.w) &&
			Mathf.Abs(Quaternion.Dot(pose.rotation, pose.rotation) - 1f) < .01f;
		public static bool Near(Pose a, Pose b, float meters, float degrees) =>
			Vector3.Distance(a.position, b.position) <= meters && Quaternion.Angle(a.rotation, b.rotation) <= degrees;
	}

	/// <summary>How a space aligns: the method, the size its tags were solved at, and the two-tag pair.</summary>
	[Serializable]
	public struct AlignmentSettings
	{
		public Method method;
		public float tagSizeCm;
		public int firstTagId;
		public int secondTagId;

		public static AlignmentSettings Default => new()
			{ method = Method.Visual, tagSizeCm = Space.DefaultTagSizeCm, firstTagId = -1, secondTagId = -1 };

		public bool HasPair => firstTagId >= 0 && secondTagId > firstTagId;

		public void Sanitize(string where)
		{
			Method migrated = ColocationManager.MigrateLegacyMethod(method);
			if (migrated == Method.ScanRegistration) migrated = Method.Visual;
			if (!ColocationManager.IsValidMethod(migrated))
			{
				Debug.LogWarning($"[{nameof(Space)}] {where}: unknown alignment method {(int)method}: using visual");
				migrated = Method.Visual;
			}
			method = migrated;
			if (!Space.Finite(tagSizeCm) || tagSizeCm <= 0f)
			{
				Debug.LogWarning($"[{nameof(Space)}] {where}: invalid tag size {tagSizeCm}: using {Space.DefaultTagSizeCm} cm");
				tagSizeCm = Space.DefaultTagSizeCm;
			}
			if (!(firstTagId == -1 && secondTagId == -1) && !HasPair)
			{
				Debug.LogWarning($"[{nameof(Space)}] {where}: invalid tag pair {firstTagId}/{secondTagId}: cleared");
				firstTagId = secondTagId = -1;
			}
		}
	}

	[Serializable]
	public struct TagRef
	{
		public int id;
		public Pose pose;
	}

	[Serializable]
	public struct AnchorRef
	{
		public string guid;
		public Pose pose;
		/// <summary>The tag this anchor was realized at, or -1 for a roaming anchor.</summary>
		public int tagId;
	}

	/// <summary>An arrangement of objects in a space, in the space's frame.</summary>
	[Serializable]
	public class Map
	{
		public string id;
		public string name;
		public long lastUsed;
		public List<MapObjectRecord> objects = new();

		public Guid Id => Guid.TryParseExact(id, "N", out Guid guid) ? guid : Guid.Empty;

		public static Map Create(string name) => new() { id = Space.NewId(), name = name ?? "", lastUsed = DateTime.UtcNow.Ticks };

		public void Sanitize(string where)
		{
			name ??= "";
			if (name.Length > Space.MaxNameLength) name = name.Substring(0, Space.MaxNameLength);
			if (lastUsed < 0) lastUsed = 0;
			objects ??= new();
			Space.DropWhere(objects, where, "object", record => string.IsNullOrEmpty(record.prefabId) ? "no prefab id"
				: !Space.ValidPose(record.pose) ? "invalid pose" : null);
		}
	}

	[Serializable]
	public struct MapObjectRecord
	{
		public string prefabId;
		public Pose pose;
	}

	/// <summary>
	/// What only this device knows about a space: the local anchors it realized at the
	/// space's registered tags. Keyed by the space and frame they were realized in, which
	/// is the host's space in a session, so a client's file may describe a space it does
	/// not have.
	/// </summary>
	[Serializable]
	public class PrivateReferences
	{
		public int schemaVersion = Space.SchemaVersion;
		public string spaceId;
		public string frameId;
		public List<TagAnchorRef> tagAnchors = new();

		public static PrivateReferences Create(Space space) => new() { spaceId = space.id, frameId = space.frameId };

		public bool Matches(Space space) => space != null && spaceId == space.id && frameId == space.frameId;

		public void Sanitize()
		{
			schemaVersion = Space.SchemaVersion;
			tagAnchors ??= new();
			HashSet<string> guids = new();
			for (int i = 0; i < tagAnchors.Count; i++)
			{
				TagAnchorRef anchor = tagAnchors[i];
				if (Guid.TryParse(anchor.guid, out Guid guid) && guid != Guid.Empty) { anchor.guid = guid.ToString("N"); tagAnchors[i] = anchor; }
			}
			Space.DropWhere(tagAnchors, $"private references of {spaceId}", "tag anchor", anchor => !Space.ValidId(anchor.guid) ? "invalid guid"
				: anchor.tagId < 0 ? "negative tag id" : !Space.ValidPose(anchor.pose) || !Space.ValidPose(anchor.tagPose) ? "invalid pose"
				: !Space.Finite(anchor.tagSizeCm) || anchor.tagSizeCm <= 0 ? "invalid tag size" : !guids.Add(anchor.guid) ? "duplicate guid" : null);
		}
	}

	/// <summary>A local anchor realized at a registered tag: valid while the tag is where it was, at the size it was.</summary>
	[Serializable]
	public struct TagAnchorRef
	{
		public string guid;
		public int tagId;
		public Pose pose;
		public float tagSizeCm;
		public Pose tagPose;
	}

	/// <summary>The space and frame a device is aligned in. Peers in different frames do not share scans or readiness.</summary>
	public readonly struct SpaceFrame : IEquatable<SpaceFrame>
	{
		public readonly Guid spaceId;
		public readonly Guid frameId;
		public SpaceFrame(Guid spaceId, Guid frameId) { this.spaceId = spaceId; this.frameId = frameId; }
		public bool IsSet => spaceId != Guid.Empty && frameId != Guid.Empty;
		public bool Equals(SpaceFrame other) => spaceId == other.spaceId && frameId == other.frameId;
		public override bool Equals(object obj) => obj is SpaceFrame other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(spaceId, frameId);
		public static bool operator ==(SpaceFrame a, SpaceFrame b) => a.Equals(b);
		public static bool operator !=(SpaceFrame a, SpaceFrame b) => !a.Equals(b);
	}
}
