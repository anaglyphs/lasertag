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
		public bool Tags;
	}

	/// <summary>Projects a space's saved targets into canonical coordinates and captures private realizations.
	/// The coordinator stages authored references and erases native saves only after their last owner is gone.</summary>
	internal sealed class MapSpaceColocationAdapter
	{
		private readonly ColocationManager colocation;
		private readonly SpatialAnchorColocationConstraintProvider anchorColocationProvider;
		private readonly AprilTagColocationConstraintProvider aprilTagColocationProvider;
		private bool anchorSnapshotPending;
		private string injectedSpaceId;
		private bool tagSnapshotPending;
		private bool taggedAnchorSnapshotPending;
		public event Action Changed = delegate { };
		public bool HasPendingSnapshots => anchorSnapshotPending || tagSnapshotPending || taggedAnchorSnapshotPending;

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
				aprilTagColocationProvider.TagsChanged += OnAprilTagsColocationChanged;
				aprilTagColocationProvider.TagSizeChanged += OnAprilTagsColocationChanged;
				aprilTagColocationProvider.AnchorsChanged += OnTaggedAnchorsChanged;
			}
		}

		public void Unregister()
		{
			if (aprilTagColocationProvider)
			{
				aprilTagColocationProvider.AnchorsChanged -= OnTaggedAnchorsChanged;
				aprilTagColocationProvider.TagSizeChanged -= OnAprilTagsColocationChanged;
				aprilTagColocationProvider.TagsChanged -= OnAprilTagsColocationChanged;
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
		private void OnAprilTagsColocationChanged() => tagSnapshotPending = true;
		private void OnTaggedAnchorsChanged() => taggedAnchorSnapshotPending = true;
		private void OnAnchorColocationPersisted(Guid _) => Changed.Invoke();

		public void ClearPendingSnapshots()
		{
			anchorSnapshotPending = false;
			tagSnapshotPending = false;
			taggedAnchorSnapshotPending = false;
		}

		/// <summary>Captures into a detached document. The coordinator decides who may commit it.</summary>
		public MapSpace TakePendingSnapshot(MapSpace snapshot, SpaceReferenceCapture capture)
		{
			bool anchors = anchorSnapshotPending && capture.Anchors;
			bool tags = tagSnapshotPending && capture.Tags;
			bool tagged = taggedAnchorSnapshotPending && capture.TaggedAnchors;
			ClearPendingSnapshots();
			if (snapshot == null || (!anchors && !tags && !tagged))
				return null;
			if (anchors)
				SnapshotAnchors(snapshot);
			if (tags)
				SnapshotTags(snapshot);
			if (tagged)
			{
				SnapshotTaggedAnchors(snapshot);
			}
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

		private void SnapshotTags(MapSpace map)
		{
			if (!aprilTagColocationProvider)
				return;

			map.tags.Clear();
			foreach ((int tagId, Pose canon) in aprilTagColocationProvider.RegisteredTags)
				map.SetTag(tagId, map.Frame.ToStorage(canon));

			// The map is written from the provider on a joiner, and the size is half of what a
			// registered pose means — a copy that recorded the poses but not the size they were
			// solved at would be unusable on its own later.
			if (aprilTagColocationProvider.TagSizeCm > 0f)
				map.tagSizeCm = aprilTagColocationProvider.TagSizeCm;

			// Private realizations are retained separately when the host changes its tag definitions.
		}

		private void SnapshotTaggedAnchors(MapSpace map)
		{
			if (!aprilTagColocationProvider)
				return;

			List<TaggedAnchorConstraintData> realized = new();
			aprilTagColocationProvider.GetLocalAnchorConstraints(realized);

			// Mirror of SnapshotAnchors: this provider owns only the tag realizations. Roaming
			// anchors (tagId -1) belong to the anchor provider and survive a map being tagged
			// later, so they must not be swept up here.
			HashSet<string> present = new();
			foreach (TaggedAnchorConstraintData entry in realized)
				present.Add(MapGuid.ToString(entry.guid));

			// Missing realizations are retained: provider replacement is not an explicit deletion.

			foreach (TaggedAnchorConstraintData entry in realized)
				MapSpace.SetAnchor(map.localAnchors, new MapAnchorEntry { guid = MapGuid.ToString(entry.guid),
					canonPose = map.Frame.ToStorage(entry.canonPose), tagId = entry.tagId, tagSizeCm = map.tagSizeCm,
					tagCanonPose = map.TryGetTag(entry.tagId, out var tag) ? tag.canonPose : Pose.identity });

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
