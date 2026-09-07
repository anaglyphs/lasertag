using System;
using System.Collections.Generic;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	internal struct MapReferenceCapture
	{
		public bool Anchors;
		public bool TaggedAnchors;
		public bool Tags;
		public bool MirrorLocalAnchors;
	}

	/// <summary>
	/// Adapts a map's saved reference records to the reusable XRTemplate colocation providers, in
	/// both directions: importing a loaded map into them, and snapshotting what they realize back
	/// into that map.
	///
	/// The providers own anchor operations, synchronization and colocation behavior. The
	/// coordinator requests erasure only after storage confirms an anchor is orphaned.
	///
	/// The map's anchor list is the union of both providers' realizations, so each snapshot may
	/// only prune what its own provider dropped.
	/// </summary>
	internal sealed class MapColocationAdapter
	{
		private readonly ColocationManager colocation;
		private readonly SpatialAnchorColocationConstraintProvider anchorColocationProvider;
		private readonly AprilTagColocationConstraintProvider aprilTagColocationProvider;
		private bool anchorSnapshotPending;
		private bool tagSnapshotPending;
		private bool taggedAnchorSnapshotPending;
		public event Action Changed = delegate { };
		public bool HasPendingSnapshots => anchorSnapshotPending || tagSnapshotPending || taggedAnchorSnapshotPending;

		public MapColocationAdapter(ColocationManager colocation)
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

		public void Inject(GameMap map)
		{
			List<AnchorConstraintData> anchors = new(map.anchors.Count);
			List<TaggedAnchorConstraintData> taggedAnchors = new();
			List<TagConstraintData> tags = new(map.tags.Count);
			HashSet<int> restoredTags = new();

			foreach (MapAnchorEntry entry in map.anchors)
			{
				if (!MapGuid.TryParse(entry.guid, out Guid guid))
					continue;

				anchors.Add(new AnchorConstraintData(guid, entry.canonPose, entry.tagId));
				if (entry.tagId >= 0 && restoredTags.Add(entry.tagId))
					taggedAnchors.Add(new TaggedAnchorConstraintData(
						guid, entry.tagId, entry.canonPose));
			}

			foreach (MapTagEntry entry in map.tags)
				tags.Add(new TagConstraintData(entry.id, entry.canonPose));

			if (anchorColocationProvider)
				anchorColocationProvider.SetConstraints(anchors);

			// Before the tags themselves: the size is what their poses were solved at, so the
			// detector must already be running at it when they land.
			InjectTagSize(map);

			if (aprilTagColocationProvider)
				aprilTagColocationProvider.SetConstraints(tags, taggedAnchors);
		}

		/// <summary>
		/// Makes the map's tag size the one this device solves at. A map that never recorded one
		/// leaves the last adopted size in place rather than overwriting it with nothing.
		/// </summary>
		private void InjectTagSize(GameMap map)
		{
			if (map == null || !aprilTagColocationProvider || map.tagSizeCm <= 0f)
				return;

			aprilTagColocationProvider.AdoptTagSize(map.tagSizeCm);
		}

		/// <summary>
		/// Proposes one tag registration. On the authority the provider applies it directly; from a
		/// client it travels to the authority, which broadcasts it back — so either way the map is
		/// written by <see cref="SnapshotTags"/> from the provider's own state.
		/// </summary>
		public bool RequestRegisterTag(int tagId, Pose canonPose)
		{
			if (!aprilTagColocationProvider)
				return false;

			aprilTagColocationProvider.RequestRegisterTag(tagId, canonPose);
			return true;
		}

		/// <summary>
		/// Proposes removing one registered tag. The provider drops that tag's realized anchor on
		/// every peer once the removal lands, and <see cref="SnapshotTaggedAnchors"/> takes it out
		/// of each device's own map from there.
		/// </summary>
		public bool RequestUnregisterTag(int tagId)
		{
			if (!aprilTagColocationProvider)
				return false;

			aprilTagColocationProvider.RequestUnregisterTag(tagId);
			return true;
		}

		/// <summary>
		/// Proposes the size tags are solved at. Like a registration it goes through the provider,
		/// so the session agrees on one size and <see cref="SnapshotTags"/> records it into every
		/// peer's map.
		/// </summary>
		public bool RequestTagSize(float centimeters)
		{
			if (!aprilTagColocationProvider)
				return false;

			aprilTagColocationProvider.RequestTagSize(centimeters);
			return true;
		}

		private void InjectAnchors(GameMap map)
		{
			if (map == null || !anchorColocationProvider)
				return;

			List<AnchorConstraintData> anchors = new(map.anchors.Count);
			foreach (MapAnchorEntry entry in map.anchors)
				if (MapGuid.TryParse(entry.guid, out Guid guid))
					anchors.Add(new AnchorConstraintData(guid, entry.canonPose, entry.tagId));

			anchorColocationProvider.SetConstraints(anchors);
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
		public GameMap TakePendingSnapshot(GameMap snapshot, MapReferenceCapture capture)
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
				if (capture.MirrorLocalAnchors)
					InjectAnchors(snapshot);
			}
			return snapshot;
		}

		private void SnapshotAnchors(GameMap map)
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
				map.SetAnchorWithTag(MapGuid.ToString(guid), state.canonPose, state.bindingId);
		}

		private void SnapshotTags(GameMap map)
		{
			if (!aprilTagColocationProvider)
				return;

			map.tags.Clear();
			foreach ((int tagId, Pose canon) in aprilTagColocationProvider.RegisteredTags)
				map.SetTag(tagId, canon);

			// The map is written from the provider on a joiner, and the size is half of what a
			// registered pose means — a copy that recorded the poses but not the size they were
			// solved at would be unusable on its own later.
			if (aprilTagColocationProvider.TagSizeCm > 0f)
				map.tagSizeCm = aprilTagColocationProvider.TagSizeCm;

			map.anchors.RemoveAll(anchor => anchor.tagId >= 0 && !map.TryGetTag(anchor.tagId, out _));
		}

		private void SnapshotTaggedAnchors(GameMap map)
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

			map.anchors.RemoveAll(entry => entry.tagId >= 0 && !present.Contains(entry.guid));

			foreach (TaggedAnchorConstraintData entry in realized)
				map.SetAnchorWithTag(MapGuid.ToString(entry.guid), entry.canonPose, entry.tagId);

		}

		public void EraseAnchorSave(string guid)
		{
			if (!MapGuid.TryParse(guid, out Guid parsed))
				return;
			if (anchorColocationProvider)
				_ = anchorColocationProvider.EraseAsync(parsed);
			else if (aprilTagColocationProvider) _ = aprilTagColocationProvider.EraseAsync(parsed);
		}

		// ------- adoption ----------------------------------------

		/// <summary>
		/// Folds the session's provider state into a map this joiner has just adopted.
		///
		/// Branches on the session's colocation method rather than the selected provider: this
		/// runs before <see cref="ColocationManager"/> has reacted to the new map, so the
		/// selection still describes the map being left.
		/// </summary>
		public GameMap AdoptProviderState(GameMap map, bool restoreLocalAnchors)
		{
			if (map == null || colocation == null)
				return map;

			// Restore private realizations before adding the authority's shared UUIDs to the
			// document, even when tags are inactive. A later method change can then reuse them.
			if (restoreLocalAnchors && aprilTagColocationProvider)
			{
				List<TaggedAnchorConstraintData> saved = new();
				HashSet<int> restored = new();
				foreach (MapAnchorEntry entry in map.anchors)
					if (entry.tagId >= 0 && MapGuid.TryParse(entry.guid, out Guid guid) && restored.Add(entry.tagId))
						saved.Add(new TaggedAnchorConstraintData(guid, entry.tagId, entry.canonPose));
				aprilTagColocationProvider.SetLocalAnchors(saved);
			}

			if (colocation.Method == ColocationManager.ColocationMethod.AprilTag)
			{
				SnapshotTags(map);
				SnapshotTaggedAnchors(map);
			}
			else
			{
				SnapshotAnchors(map);
				// Preserve tag capability and parent metadata even while the anchor strategy is
				// selected; the inactive tag provider still owns and synchronizes its registered data.
				SnapshotTags(map);
			}

			// The snapshots above are this adoption's, taken deliberately; anything the providers
			// queued while they were being replaced describes the map we just left.
			ClearPendingSnapshots();
			return map;
		}
	}
}
