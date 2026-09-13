using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Physical reference definitions and map membership, independent of live XR objects.</summary>
	[Serializable]
	public class MapSpace
	{
		public const int SchemaVersion = 1;
		public const float DefaultTagSizeCm = 10f;
		public int schemaVersion = SchemaVersion;
		public string id;
		public string name;
		public string version;
		public string storageFrameId;
		public string canonicalFrameId;
		public Pose canonicalFromStorage = Pose.identity;
		public long frameRevision;
		public long lastUsed;
		public long lastEdited;
		public bool dirty;
		public bool automaticallyCreated;
		public bool initializationPending;
		public bool hasPendingSetup;
		public ColocationManager.ColocationMethod pendingSetupMethod;
		public ReferenceTransitionIntent pendingSetupIntent;
		public List<string> mapIds = new();
		public List<MapTagEntry> tags = new();
		public List<MapAnchorEntry> anchors = new();
		public List<MapAnchorEntry> localAnchors = new();
		public List<MapSpaceReferenceSet> retainedReferences = new();
		public List<MapSpaceAssociation> associations = new();
		public List<MapSpaceFrameBinding> knownFrames = new();
		public string referenceSourceId;
		public string referenceVersion;
		public bool referenceDirty;
		public float tagSizeCm = DefaultTagSizeCm;
		public ColocationManager.ColocationMethod preferredColocationMethod;
		public int firstTagId = -1;
		public int secondTagId = -1;
		public string previousCanonicalFrameId;
		public Pose previousOffset = Pose.identity;
		public bool HasTags => tags.Count > 0;
		public bool HasReferenceBasedData => tags.Count > 0 || anchors.Count > 0 || localAnchors.Count > 0 ||
			retainedReferences.Exists(r => r.tags.Count > 0 || r.anchors.Count > 0);
		public MapSpaceFrame Frame => new(this);
		public static bool ValidId(string value) => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty;

		public static MapSpace Create(string name)
		{
			string frame = Guid.NewGuid().ToString("N");
			MapSpace space = new()
			{
				id = Guid.NewGuid().ToString("N"), name = name, version = Guid.NewGuid().ToString("N"),
				storageFrameId = frame, canonicalFrameId = frame, lastUsed = DateTime.UtcNow.Ticks,
				lastEdited = DateTime.UtcNow.Ticks, preferredColocationMethod = ColocationManager.ColocationMethod.MetaSharedAnchor
			};
			space.referenceSourceId = space.id; space.referenceVersion = space.version;
			return space;
		}

		public MapSpace Clone()
		{
			MapSpace copy = (MapSpace)MemberwiseClone();
			copy.mapIds = new(mapIds); copy.tags = new(tags); copy.anchors = new(anchors); copy.localAnchors = new(localAnchors);
			copy.associations = new(associations); copy.knownFrames = new(knownFrames);
			copy.retainedReferences = retainedReferences.ConvertAll(r => r.Clone());
			return copy;
		}

		public bool TryGetTag(int tagId, out MapTagEntry entry)
		{
			foreach (var tag in tags) if (tag.id == tagId) { entry = tag; return true; }
			entry = default; return false;
		}
		public bool IsCompatibleLocalAnchor(MapAnchorEntry anchor) => anchor.tagSizeCm > 0 &&
			Mathf.Abs(anchor.tagSizeCm - tagSizeCm) < .001f && TryGetTag(anchor.tagId, out var tag) &&
			MapSpaceFrame.Near(anchor.tagCanonPose, tag.canonPose, .001f, .1f);
		public bool TryGetAnchor(string guid, out MapAnchorEntry entry)
		{
			foreach (var anchor in AllAnchors()) if (anchor.guid == guid) { entry = anchor; return true; }
			entry = default; return false;
		}
		public IEnumerable<MapAnchorEntry> AllAnchors()
		{
			foreach (var anchor in anchors) yield return anchor;
			foreach (var anchor in localAnchors) yield return anchor;
			foreach (var references in retainedReferences)
				foreach (var anchor in references.anchors) yield return anchor;
		}
		public void SetTag(int tagId, Pose storedPose)
		{
			int index = tags.FindIndex(t => t.id == tagId);
			MapTagEntry entry = new() { id = tagId, canonPose = storedPose };
			if (index < 0) tags.Add(entry); else tags[index] = entry;
		}
		public void SetAnchorWithTag(string guid, Pose storedPose, int tagId) => SetAnchor(anchors, new()
			{ guid = guid, canonPose = storedPose, tagId = tagId });
		public static void SetAnchor(List<MapAnchorEntry> list, MapAnchorEntry entry)
		{
			int index = list.FindIndex(a => a.guid == entry.guid);
			if (index < 0) list.Add(entry); else list[index] = entry;
		}
		/// <summary>Commit reference changes without overwriting maps or names edited during observation.</summary>
		public MapSpace ApplyReferenceCandidate(MapSpace candidate)
		{
			if (candidate == null || candidate.id != id || candidate.storageFrameId != storageFrameId ||
				candidate.canonicalFrameId != canonicalFrameId || candidate.frameRevision != frameRevision)
				throw new InvalidOperationException("The candidate belongs to an obsolete space frame.");
			var result = Clone(); result.tags = new(candidate.tags); result.anchors = new(candidate.anchors);
			foreach (var anchor in candidate.localAnchors) SetAnchor(result.localAnchors, anchor);
			result.tagSizeCm = candidate.tagSizeCm; return result;
		}
		public void RetainActiveReferences()
		{
			if (tags.Count == 0 && anchors.Count == 0) return;
			string revision = referenceVersion ?? version;
			if (retainedReferences.Exists(r => r.sourceId == referenceSourceId && r.version == revision)) return;
			retainedReferences.Add(new() { sourceId = referenceSourceId, version = revision, tagSizeCm = tagSizeCm, tags = new(tags), anchors = new(anchors) });
		}
		public void MarkChanged()
		{
			version = Guid.NewGuid().ToString("N"); lastEdited = DateTime.UtcNow.Ticks; dirty = true;
		}
		internal bool Validate() => schemaVersion == SchemaVersion && ValidId(id) && ValidId(version) &&
			ValidId(storageFrameId) && ValidId(canonicalFrameId) && MapSpaceFrame.ValidOffset(canonicalFromStorage) &&
			MapSpaceFrame.Finite(tagSizeCm) && tagSizeCm > 0 && ColocationManager.IsValidMethod(preferredColocationMethod) &&
			mapIds != null && tags != null && anchors != null && localAnchors != null && retainedReferences != null &&
			associations != null && knownFrames != null && frameRevision >= 0 &&
			associations.TrueForAll(a => ValidId(a.remoteSpaceId) && ValidId(a.frameId)) &&
			knownFrames.TrueForAll(f => ValidId(f.frameId) && MapSpaceFrame.ValidOffset(f.canonicalFromStorage)) &&
			((firstTagId == -1 && secondTagId == -1) || (firstTagId >= 0 && secondTagId > firstTagId)) && tags.TrueForAll(t => t.id >= 0 && MapSpaceFrame.ValidPose(t.canonPose)) &&
			anchors.TrueForAll(ValidAnchor) && localAnchors.TrueForAll(ValidAnchor) &&
			retainedReferences.TrueForAll(r => r != null && r.tags != null && r.anchors != null &&
				r.tags.TrueForAll(t => t.id >= 0 && MapSpaceFrame.ValidPose(t.canonPose)) && r.anchors.TrueForAll(ValidAnchor));
		private static bool ValidAnchor(MapAnchorEntry a) => Guid.TryParse(a.guid, out var id) && id != Guid.Empty && a.tagId >= -1 && MapSpaceFrame.ValidPose(a.canonPose);
	}

	[Serializable] public struct MapAnchorEntry
	{
		public string guid;
		/// <summary>Persistent target in the parent space's storage frame.</summary>
		public Pose canonPose;
		public int tagId;
		public float tagSizeCm;
		public Pose tagCanonPose;
	}
	[Serializable] public struct MapTagEntry
	{
		public int id;
		public Pose canonPose;
	}
	[Serializable] public struct MapSpaceAssociation
	{
		public string remoteSpaceId;
		public string frameId;
	}
	[Serializable] public struct MapSpaceFrameBinding
	{
		public string frameId;
		public Pose canonicalFromStorage;
	}
	[Serializable] public class MapSpaceReferenceSet
	{
		public string sourceId;
		public string version;
		public float tagSizeCm;
		public List<MapTagEntry> tags = new();
		public List<MapAnchorEntry> anchors = new();
		public MapSpaceReferenceSet Clone() => new() { sourceId = sourceId, version = version, tagSizeCm = tagSizeCm,
			tags = new(tags), anchors = new(anchors) };
	}
}
