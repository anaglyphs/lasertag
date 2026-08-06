using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Adapts a map's saved reference records to the reusable XRTemplate colocation providers, in
	/// both directions: importing a loaded map into them, and snapshotting what they realize back
	/// into that map.
	///
	/// The providers own anchor operations, synchronization and colocation behavior. Nothing here
	/// touches the anchor runtime except to erase a save no map references anymore, which is a
	/// question only the map layer can answer.
	///
	/// The map's anchor list is the union of both providers' realizations, so each snapshot may
	/// only prune what its own provider dropped.
	/// </summary>
	internal sealed class MapColocationAdapter
	{
		private readonly ColocationManager colocation;
		private readonly SpatialAnchorColocationConstraintProvider anchorColocationProvider;
		private readonly AprilTagColocationConstraintProvider aprilTagColocationProvider;
		private readonly Func<GameMap> currentMap;
		private readonly Func<GameMap, bool> mapIsSessionMap;
		private readonly Action changed;

		// Providers raise a change per entry, so injecting a map raises one per anchor. Record
		// which snapshots are stale and take them once at the end of the frame: reacting per
		// entry would both re-serialize the map to disk once per anchor and let a snapshot
		// observe a provider halfway through an import.
		private bool anchorSnapshotPending;
		private bool tagSnapshotPending;
		private bool taggedAnchorSnapshotPending;

		/// <param name="mapIsSessionMap">Whether the providers hold the session's map rather than
		/// one this joiner had loaded before it arrived.</param>
		/// <param name="changed">Raised once per frame in which a snapshot altered the map.</param>
		public MapColocationAdapter(ColocationManager colocation, Func<GameMap> currentMap,
			Func<GameMap, bool> mapIsSessionMap, Action changed)
		{
			this.colocation = colocation;
			anchorColocationProvider = colocation != null ? colocation.AnchorProvider : null;
			aprilTagColocationProvider = colocation != null ? colocation.TagProvider : null;

			this.currentMap = currentMap ?? throw new ArgumentNullException(nameof(currentMap));
			this.mapIsSessionMap = mapIsSessionMap ?? throw new ArgumentNullException(nameof(mapIsSessionMap));
			this.changed = changed ?? throw new ArgumentNullException(nameof(changed));
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
				aprilTagColocationProvider.AnchorsChanged += OnTaggedAnchorsChanged;
			}
		}

		public void Unregister()
		{
			if (aprilTagColocationProvider)
			{
				aprilTagColocationProvider.AnchorsChanged -= OnTaggedAnchorsChanged;
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

			foreach (MapAnchorEntry entry in map.anchors)
			{
				if (!MapGuid.TryParse(entry.guid, out Guid guid))
					continue;

				anchors.Add(new AnchorConstraintData(guid, entry.canonPose, entry.tagId));
				if (entry.tagId >= 0)
					taggedAnchors.Add(new TaggedAnchorConstraintData(
						guid, entry.tagId, entry.canonPose));
			}

			foreach (MapTagEntry entry in map.tags)
				tags.Add(new TagConstraintData(entry.id, entry.canonPose));

			if (anchorColocationProvider)
				anchorColocationProvider.SetConstraints(anchors);
			if (aprilTagColocationProvider)
				aprilTagColocationProvider.SetConstraints(tags, taggedAnchors);
		}

		public void InjectTags(GameMap map)
		{
			if (map == null || !aprilTagColocationProvider)
				return;

			List<TagConstraintData> tags = new(map.tags.Count);
			foreach (MapTagEntry entry in map.tags)
				tags.Add(new TagConstraintData(entry.id, entry.canonPose));

			aprilTagColocationProvider.SetRegisteredTags(tags);
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
			if (SyncBus.IsAuthority)
			{
				if (anchorColocationProvider)
					anchorColocationProvider.SetConstraints(Array.Empty<AnchorConstraintData>());
				if (aprilTagColocationProvider)
					aprilTagColocationProvider.SetConstraints(Array.Empty<TagConstraintData>(),
						Array.Empty<TaggedAnchorConstraintData>());
			}
			else if (aprilTagColocationProvider)
			{
				aprilTagColocationProvider.SetLocalAnchors(Array.Empty<TaggedAnchorConstraintData>());
			}
		}

		// ------- export ------------------------------------------

		private void OnAnchorColocationConstraintsChanged() => anchorSnapshotPending = true;
		private void OnAprilTagsColocationChanged() => tagSnapshotPending = true;
		private void OnTaggedAnchorsChanged() => taggedAnchorSnapshotPending = true;
		private void OnAnchorColocationPersisted(Guid _) => changed();

		public void ClearPendingSnapshots()
		{
			anchorSnapshotPending = false;
			tagSnapshotPending = false;
			taggedAnchorSnapshotPending = false;
		}

		public void ApplyPendingSnapshots()
		{
			GameMap map = currentMap();
			if (map == null)
			{
				ClearPendingSnapshots();
				return;
			}

			bool anchorsOwned = AnchorProviderOwnsState(map);
			bool tagsOwned = TagProviderOwnsState(map);
			bool altered = false;

			if (anchorSnapshotPending)
			{
				anchorSnapshotPending = false;
				if (anchorsOwned)
				{
					SnapshotAnchors(map);
					altered = true;
				}
			}

			if (tagSnapshotPending)
			{
				tagSnapshotPending = false;
				if (tagsOwned)
				{
					SnapshotTags(map);
					altered = true;
				}
			}

			if (taggedAnchorSnapshotPending)
			{
				taggedAnchorSnapshotPending = false;
				if (tagsOwned)
				{
					SnapshotTaggedAnchors(map);

					// Offline the anchor provider is the one that keeps these loaded if the map is
					// later hosted in shared-anchor mode.
					if (!SyncBus.Active)
						InjectAnchors(map);

					altered = true;
				}
			}

			if (altered)
				changed();
		}

		/// <summary>
		/// Whether a provider's state currently describes this map, and so may be written into it.
		/// Keyed off the provider actually selected rather than the session's requested method:
		/// a method whose provider cannot serve the current map selects nothing, and a stopped
		/// provider realizes nothing worth recording.
		/// </summary>
		private bool AnchorProviderOwnsState(GameMap map) =>
			mapIsSessionMap(map) && (SyncBus.Active
				? colocation != null && colocation.UsingAnchorProvider
				: anchorColocationProvider && anchorColocationProvider.IsRunning);

		private bool TagProviderOwnsState(GameMap map) =>
			mapIsSessionMap(map) && (SyncBus.Active
				? colocation != null && colocation.UsingTagProvider
				: aprilTagColocationProvider && aprilTagColocationProvider.IsRunning);

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

			List<string> dropped = new();
			foreach (MapAnchorEntry entry in map.anchors)
				if (entry.tagId >= 0 && !present.Contains(entry.guid))
					dropped.Add(entry.guid);

			map.anchors.RemoveAll(entry => dropped.Contains(entry.guid));

			foreach (TaggedAnchorConstraintData entry in realized)
				map.SetAnchorWithTag(MapGuid.ToString(entry.guid), entry.canonPose, entry.tagId);

			// The provider stopped realizing these — their tag was unregistered, here or by the
			// session authority — so nothing will ask the device for them again.
			foreach (string guid in dropped)
				EraseTagAnchorSaveIfOrphaned(guid);
		}

		/// <summary>
		/// Erases a dropped tag anchor's local save. A guid some other map still references — a
		/// fork keeps its parent's anchors — has to stay on the device. Call this only once the
		/// current map no longer lists the anchor, so it does not veto its own erase.
		/// </summary>
		public void EraseTagAnchorSaveIfOrphaned(string guid)
		{
			if (!aprilTagColocationProvider || MapStore.IsAnchorReferenced(guid))
				return;

			if (MapGuid.TryParse(guid, out Guid parsed))
				_ = aprilTagColocationProvider.EraseAsync(parsed);
		}

		public void EraseAnchorSave(string guid)
		{
			if (!anchorColocationProvider)
				return;

			if (MapGuid.TryParse(guid, out Guid parsed))
				_ = anchorColocationProvider.EraseAsync(parsed);
		}

		// ------- adoption ----------------------------------------

		/// <summary>
		/// Folds the session's provider state into a map this joiner has just adopted.
		///
		/// Branches on the session's colocation method rather than the selected provider: this
		/// runs before <see cref="ColocationManager"/> has reacted to the new map, so the
		/// selection still describes the map being left.
		/// </summary>
		public void AdoptProviderState(GameMap map)
		{
			if (map == null || colocation == null)
				return;

			if (colocation.Method == ColocationManager.ColocationMethod.AprilTag)
			{
				// Tag anchors are private per device. Restore this headset's saved realizations,
				// while registered tag poses come from the authority's provider snapshot.
				List<TaggedAnchorConstraintData> saved = new();
				foreach (MapAnchorEntry entry in map.anchors)
					if (entry.tagId >= 0 && MapGuid.TryParse(entry.guid, out Guid guid))
						saved.Add(new TaggedAnchorConstraintData(guid, entry.tagId, entry.canonPose));

				if (aprilTagColocationProvider)
					aprilTagColocationProvider.SetLocalAnchors(saved);

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
		}
	}
}
