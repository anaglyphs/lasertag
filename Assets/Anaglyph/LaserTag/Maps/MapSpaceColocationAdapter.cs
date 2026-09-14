using System;
using System.Collections.Generic;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	internal struct SpaceReferenceCapture
	{
		public bool Anchors;
		public bool TaggedAnchors;
	}

	/// <summary>Projects saved targets into canonical coordinates and captures compatible private anchors.</summary>
	internal sealed class MapSpaceColocationAdapter
	{
		private readonly ColocationManager colocation;
		private readonly SpatialAnchorColocationConstraintProvider anchorColocationProvider;
		private readonly AprilTagColocationConstraintProvider aprilTagColocationProvider;
		private bool anchorSnapshotPending;
		private string injectedSpaceId;
		private bool taggedAnchorSnapshotPending;
		public event Action Changed = delegate { };
		public bool HasPendingSnapshots => anchorSnapshotPending || taggedAnchorSnapshotPending;

		public MapSpaceColocationAdapter(ColocationManager colocation)
		{
			this.colocation = colocation;
			anchorColocationProvider = colocation != null ? colocation.AnchorProvider : null;
			aprilTagColocationProvider = colocation != null ? colocation.TagProvider : null;
		}

		public void Register()
		{
			if (anchorColocationProvider)
			{
				anchorColocationProvider.ConstraintsChanged += OnAnchorColocationConstraintsChanged;
				anchorColocationProvider.AnchorPersisted += OnAnchorColocationPersisted;
			}

			if (aprilTagColocationProvider)
			{
				aprilTagColocationProvider.AnchorsChanged += OnTaggedAnchorsChanged;
			}
		}

		public void Unregister()
		{
			if (aprilTagColocationProvider)
			{
				aprilTagColocationProvider.AnchorsChanged -= OnTaggedAnchorsChanged;
			}

			if (anchorColocationProvider)
			{
				anchorColocationProvider.AnchorPersisted -= OnAnchorColocationPersisted;
				anchorColocationProvider.ConstraintsChanged -= OnAnchorColocationConstraintsChanged;
			}
		}

		// ------- import ------------------------------------------

		public void Inject(MapSpace map)
		{
			if (injectedSpaceId != map.id) colocation?.TwoTagProvider?.ResetReferences();
			injectedSpaceId = map.id;
			colocation?.TwoTagProvider?.ConfigurePair(map.firstTagId, map.secondTagId);
			List<AnchorConstraintData> anchors = new(map.anchors.Count);
			List<TaggedAnchorConstraintData> taggedAnchors = new();
			List<TagConstraintData> tags = new(map.tags.Count);
			HashSet<int> restoredTags = new();

			foreach (MapAnchorEntry entry in map.localAnchors)
				if (map.IsCompatibleLocalAnchor(entry) && MapGuid.TryParse(entry.guid, out Guid localGuid) && restoredTags.Add(entry.tagId))
					taggedAnchors.Add(new TaggedAnchorConstraintData(localGuid, entry.tagId, map.Frame.ToCanonical(entry.canonPose)));
			foreach (MapAnchorEntry entry in map.anchors)
			{
				if (!MapGuid.TryParse(entry.guid, out Guid guid)) continue;
				anchors.Add(new AnchorConstraintData(guid, map.Frame.ToCanonical(entry.canonPose), entry.tagId));
				if (entry.tagId >= 0 && map.TryGetTag(entry.tagId, out _) && restoredTags.Add(entry.tagId))
					taggedAnchors.Add(new TaggedAnchorConstraintData(guid, entry.tagId, map.Frame.ToCanonical(entry.canonPose)));
			}

			foreach (MapTagEntry entry in map.tags)
				tags.Add(new TagConstraintData(entry.id, map.Frame.ToCanonical(entry.canonPose)));

			if (anchorColocationProvider && Anaglyph.Netcode.SyncVariables.SyncBus.IsAuthority)
				anchorColocationProvider.SetConstraints(anchors);

			// Before the tags themselves: the size is what their poses were solved at, so the
			// detector must already be running at it when they land.
			InjectTagSize(map);

			if (aprilTagColocationProvider)
			{
				if (Anaglyph.Netcode.SyncVariables.SyncBus.IsAuthority) aprilTagColocationProvider.SetRegisteredTags(tags);
				aprilTagColocationProvider.SetLocalAnchors(taggedAnchors);
			}
		}

		/// <summary>
		/// Makes the space's tag size the one this device solves at. A map that never recorded one
		/// leaves the last adopted size in place rather than overwriting it with nothing.
		/// </summary>
		private void InjectTagSize(MapSpace map)
		{
			if (map == null || !aprilTagColocationProvider || map.tagSizeCm <= 0f)
				return;

			aprilTagColocationProvider.AdoptTagSize(map.tagSizeCm);
		}

		public void ClearForNoMap()
		{
			if (anchorColocationProvider)
				anchorColocationProvider.SetConstraints(Array.Empty<AnchorConstraintData>());
			if (aprilTagColocationProvider)
				aprilTagColocationProvider.SetConstraints(Array.Empty<TagConstraintData>(),
					Array.Empty<TaggedAnchorConstraintData>());
		}

		// ------- export ------------------------------------------

		private void OnAnchorColocationConstraintsChanged() => anchorSnapshotPending = true;
		private void OnTaggedAnchorsChanged() => taggedAnchorSnapshotPending = true;
		private void OnAnchorColocationPersisted(Guid _) => Changed.Invoke();

		public void ClearPendingSnapshots()
		{
			anchorSnapshotPending = false;
			taggedAnchorSnapshotPending = false;
		}

		/// <summary>Captures into a detached document. The coordinator decides who may commit it.</summary>
		public MapSpace TakePendingSnapshot(MapSpace snapshot, SpaceReferenceCapture capture)
		{
			bool anchors = anchorSnapshotPending && capture.Anchors;
			bool tagged = taggedAnchorSnapshotPending && capture.TaggedAnchors;
			ClearPendingSnapshots();
			if (snapshot == null || (!anchors && !tagged))
				return null;
			if (anchors)
				SnapshotAnchors(snapshot);
			if (tagged)
				SnapshotTaggedAnchors(snapshot);
			return snapshot;
		}

		private void SnapshotAnchors(MapSpace map)
		{
			if (!anchorColocationProvider)
				return;

			// May only prune what this provider itself dropped. Tag anchors are private per
			// device: on a joiner the authority's constraints never describe this headset's own,
			// and clearing here would erase them from its copy.
			HashSet<string> present = new();
			foreach (Guid guid in anchorColocationProvider.Constraints.Keys)
				present.Add(MapGuid.ToString(guid));

			map.anchors.RemoveAll(entry => entry.tagId < 0 && !present.Contains(entry.guid));

			foreach ((Guid guid, AnchorConstraintState state) in anchorColocationProvider.Constraints)
				map.SetAnchorWithTag(MapGuid.ToString(guid), map.Frame.ToStorage(state.canonPose), state.bindingId);
		}

		public MapSpace CapturePrivateReferences(MapSpace source)
		{
			if (source == null) return null;
			var copy = source.Clone();
			SnapshotTaggedAnchors(copy);
			return copy;
		}

		private void SnapshotTaggedAnchors(MapSpace map)
		{
			if (!aprilTagColocationProvider || Mathf.Abs(aprilTagColocationProvider.TagSizeCm - map.tagSizeCm) > .001f)
				return;

			List<TaggedAnchorConstraintData> realized = new();
			aprilTagColocationProvider.GetLocalAnchorConstraints(realized);
			foreach (TaggedAnchorConstraintData entry in realized)
			{
				if (!map.TryGetTag(entry.tagId, out var tag) ||
					!aprilTagColocationProvider.RegisteredTags.TryGetValue(entry.tagId, out var liveTag) ||
					!MapSpaceFrame.Near(map.Frame.ToCanonical(tag.canonPose), liveTag, .001f, .1f)) continue;
				PrioritizePrivateAnchor(map.localAnchors, new MapAnchorEntry
				{
					guid = MapGuid.ToString(entry.guid), tagId = entry.tagId,
					canonPose = map.Frame.ToStorage(entry.canonPose), tagCanonPose = tag.canonPose, tagSizeCm = map.tagSizeCm
				});
			}
		}

		public static void PrioritizePrivateAnchor(List<MapAnchorEntry> anchors, MapAnchorEntry anchor)
		{
			// Restore the live UUID first; retain older saves for ownership and cleanup.
			anchors.RemoveAll(saved => saved.guid == anchor.guid);
			anchors.Insert(0, anchor);
		}

		public static void CopyCompatiblePrivateAnchors(MapSpace target, MapSpace source)
		{
			if (source == null || source.canonicalFrameId != target.canonicalFrameId) return;
			for (int i = source.localAnchors.Count - 1; i >= 0; i--)
			{
				var anchor = source.localAnchors[i];
				if (Mathf.Abs(anchor.tagSizeCm - target.tagSizeCm) >= .001f ||
					!target.TryGetTag(anchor.tagId, out var tag) ||
					!MapSpaceFrame.Near(source.Frame.ToCanonical(anchor.tagCanonPose), target.Frame.ToCanonical(tag.canonPose), .001f, .1f)) continue;
				anchor.canonPose = target.Frame.ToStorage(source.Frame.ToCanonical(anchor.canonPose));
				anchor.tagCanonPose = tag.canonPose;
				PrioritizePrivateAnchor(target.localAnchors, anchor);
			}
		}

		public void EraseAnchorSave(string guid)
		{
			if (!MapGuid.TryParse(guid, out Guid parsed))
				return;
			if (anchorColocationProvider)
				_ = anchorColocationProvider.EraseAsync(parsed);
			else if (aprilTagColocationProvider) _ = aprilTagColocationProvider.EraseAsync(parsed);
		}

	}
}
