using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// Applies the shown space's alignment to the colocation providers and authors its
	/// references: the method, registered tags, shared anchors, tag size and two-tag pair. Every
	/// device writes the tag anchors it realizes to the space's private file; the device that
	/// owns the space (offline, or the host) writes minted anchors to the space itself. Changing
	/// the method bumps the frame when the frame can move: into or out of two-tag mode, or when
	/// the pair changes. It is also the anchor provider's session policy.
	/// </summary>
	public sealed class SpaceAlignment : IAnchorSessionPolicy
	{
		private const int PrivateAnchorsPerTag = 4;

		private readonly SpaceManager manager;
		private readonly ColocationManager colocation;
		private readonly SpaceStore store;
		private Space space;
		private PrivateReferences privateReferences;
		private bool applying;

		public SpaceAlignment(SpaceManager manager, ColocationManager colocation, SpaceStore store)
		{
			this.manager = manager;
			this.colocation = colocation;
			this.store = store;
		}

		private SpatialAnchorColocationConstraintProvider Anchors => colocation.AnchorProvider;
		private AprilTagColocationConstraintProvider Tags => colocation.TagProvider;

		public void Register()
		{
			// this class drives activation; the manager's own selection stays off
			colocation.ManagedSelection = true;
			Anchors.Policy = this;
			Anchors.ConstraintsChanged += OnAnchorsChanged;
			Anchors.AnchorPersisted += OnAnchorPersisted;
			Tags.AnchorsChanged += OnTagAnchorsChanged;
			Tags.PairSelected += OnPairSelected;
		}

		public void Unregister()
		{
			Tags.PairSelected -= OnPairSelected;
			Tags.AnchorsChanged -= OnTagAnchorsChanged;
			Anchors.AnchorPersisted -= OnAnchorPersisted;
			Anchors.ConstraintsChanged -= OnAnchorsChanged;
			if (ReferenceEquals(Anchors.Policy, this)) Anchors.Policy = null;
		}

		// ------- state -----------------------------------------------

		/// <summary>The headset is focused and head-tracked: its frame means something.</summary>
		public static bool TrackingReady => AppFocus.IsFocused && PlayerHeadsetStatus.HeadTrackingReady;

		public Method ActiveMethod => colocation.ActiveMethod;
		/// <summary>The method the shown space is set to; visual without a space.</summary>
		public Method SelectedMethod => space?.alignment.method ?? Method.Visual;
		public bool UsingTwoTagMode => colocation.UsingTwoTagMode;
		public float TagSizeCm => space?.alignment.tagSizeCm ?? Space.DefaultTagSizeCm;

		/// <summary>The rig is in the shown space's frame: colocated, tracked, and on the space's references where the method has any.</summary>
		public bool IsAligned => space != null && ColocationManager.IsColocated && TrackingReady &&
			(!ColocationManager.UsesSavedReferences(colocation.ActiveMethod) || colocation.UsingTwoTagMode || colocation.ReferenceAligned);

		/// <summary>Scans may be integrated and shared: aligned, and in two-tag mode the pair is chosen.</summary>
		public bool CanIntegrateEnvironment => IsAligned && (!colocation.UsingTwoTagMode || space.alignment.HasPair);

		private bool RoundInProgress => MatchReferee.State is MatchState.Playing or MatchState.Countdown;
		private static string L(string key) => MenuCopy.Get("Map", key);

		/// <summary>The space has nothing that fixes its frame yet, so the first reference may define it.</summary>
		private bool FrameIsFree => space != null && !space.HasReferences && !store.HasVisualMap(space.id);

		// ------- applying a space ------------------------------------

		/// <summary>
		/// Pushes the space's settings and references to the providers and activates its method.
		/// With <paramref name="preserve"/> the rig keeps its alignment through the change (a
		/// reference edit); without it, colocation starts over (another space or frame).
		/// </summary>
		public void Apply(Space next, bool preserve)
		{
			space = next;
			applying = true;
			try
			{
				if (space == null)
				{
					privateReferences = null;
					ClearProviders();
					colocation.ClearSpace();
					return;
				}
				PrivateReferences loaded = store.LoadPrivate(space.id);
				privateReferences = loaded != null && loaded.Matches(space) ? loaded : PrivateReferences.Create(space);
				Tags.ConfigurePair(space.alignment.firstTagId, space.alignment.secondTagId);
				// before the tags themselves: the size is what their poses were solved at
				Tags.AdoptTagSize(space.alignment.tagSizeCm);
				if (SyncBus.IsAuthority)
				{
					Anchors.SetConstraints(AnchorConstraints());
					Tags.SetRegisteredTags(TagConstraints());
				}
				Tags.SetLocalAnchors(CompatibleTagAnchors());
				colocation.ConfigureSpace(space);
				Method method = colocation.PreferredAvailableMethod;
				colocation.ActivateMethod(method, preserve);
				if (SyncBus.Active && SyncBus.IsAuthority) colocation.CommitMethod(method);
			}
			finally { applying = false; }
		}

		private void ClearProviders()
		{
			if (SyncBus.IsAuthority)
			{
				Anchors.SetConstraints(Array.Empty<AnchorConstraintData>());
				Tags.SetRegisteredTags(Array.Empty<TagConstraintData>());
			}
			Tags.SetLocalAnchors(Array.Empty<TaggedAnchorConstraintData>());
		}

		private List<AnchorConstraintData> AnchorConstraints()
		{
			List<AnchorConstraintData> result = new(space.anchors.Count);
			foreach (AnchorRef anchor in space.anchors)
				if (Guid.TryParseExact(anchor.guid, "N", out Guid guid)) result.Add(new AnchorConstraintData(guid, anchor.pose, anchor.tagId));
			return result;
		}

		private List<TagConstraintData> TagConstraints()
		{
			List<TagConstraintData> result = new(space.tags.Count);
			foreach (TagRef tag in space.tags) result.Add(new TagConstraintData(tag.id, tag.pose));
			return result;
		}

		/// <summary>The newest private anchor per registered tag, realized at the tag's current pose and size.</summary>
		private List<TaggedAnchorConstraintData> CompatibleTagAnchors()
		{
			List<TaggedAnchorConstraintData> result = new();
			HashSet<int> restored = new();
			foreach (TagAnchorRef anchor in privateReferences.tagAnchors)
				if (IsCompatible(anchor) && restored.Add(anchor.tagId) && Guid.TryParseExact(anchor.guid, "N", out Guid guid))
					result.Add(new TaggedAnchorConstraintData(guid, anchor.tagId, anchor.pose));
			return result;
		}

		private bool IsCompatible(TagAnchorRef anchor) => Mathf.Abs(anchor.tagSizeCm - space.alignment.tagSizeCm) < .001f &&
			space.TryGetTag(anchor.tagId, out TagRef tag) && Space.Near(anchor.tagPose, tag.pose, .001f, .1f);

		// ------- what the providers realize --------------------------

		// the authority's roaming anchors are the space's; a client's provider holds the host's
		private void OnAnchorsChanged()
		{
			if (applying || space == null || manager.Role == SpaceRole.Client) return;
			HashSet<string> present = new();
			foreach ((Guid guid, AnchorConstraintState state) in Anchors.Constraints)
			{
				string id = guid.ToString("N");
				present.Add(id);
				space.SetAnchor(id, state.canonPose, state.bindingId);
			}
			space.anchors.RemoveAll(anchor => anchor.tagId < 0 && !present.Contains(anchor.guid));
			manager.SpaceReferencesChanged();
		}

		private void OnAnchorPersisted(Guid _) => manager.MarkDirty();

		// every device keeps the anchors it realized at the space's tags, in the space's frame
		private void OnTagAnchorsChanged()
		{
			if (applying || space == null || privateReferences == null) return;
			List<TaggedAnchorConstraintData> realized = new();
			Tags.GetLocalAnchorConstraints(realized);
			bool changed = false;
			foreach (TaggedAnchorConstraintData entry in realized)
			{
				if (!space.TryGetTag(entry.tagId, out TagRef tag)) continue;
				string guid = entry.guid.ToString("N");
				TagAnchorRef anchor = new()
				{
					guid = guid, tagId = entry.tagId, pose = entry.canonPose, tagSizeCm = space.alignment.tagSizeCm, tagPose = tag.pose
				};
				int index = privateReferences.tagAnchors.FindIndex(a => a.guid == guid);
				if (index == 0 && Space.Near(privateReferences.tagAnchors[0].pose, anchor.pose, .0001f, .01f)) continue;
				// the live anchor first; older saves stay for a while, in case this one fails to restore
				if (index >= 0) privateReferences.tagAnchors.RemoveAt(index);
				privateReferences.tagAnchors.Insert(0, anchor);
				int kept = 0;
				privateReferences.tagAnchors.RemoveAll(a => a.tagId == anchor.tagId && ++kept > PrivateAnchorsPerTag);
				changed = true;
			}
			if (changed) store.SavePrivate(privateReferences);
		}

		private void OnPairSelected(int first, int second)
		{
			if (space == null || manager.Role == SpaceRole.Client || space.alignment.HasPair) return;
			space.alignment.firstTagId = Math.Min(first, second);
			space.alignment.secondTagId = Math.Max(first, second);
			manager.SpaceReferencesChanged();
		}

		// ------- commands --------------------------------------------

		public string MethodBlocker(Method method) =>
			!ColocationManager.IsValidMethod(method) ? L("blocker.unknown-colocation-method") :
			space == null ? L("space.load-first") :
			RoundInProgress ? L("blocker.wait-until-the-round-ends") :
			manager.Role == SpaceRole.Client ? L("blocker.only-the-host-can-change-the-map") :
			HeadsetConfiguration.SessionIsOperatorManaged && !HeadsetConfiguration.IsOperatorDevice ? L("blocker.the-operator-sets-the-alignment-method") : null;

		/// <summary>Saves the method and re-aligns. The frame moves when two-tag mode is entered or left.</summary>
		public bool SetMethod(Method method)
		{
			if (MethodBlocker(method) != null) return false;
			if (space.alignment.method == method) return true;
			bool wasTwoTag = ColocationManager.IsTwoTagMode(space.alignment.method, space);
			space.alignment.method = method;
			bool isTwoTag = ColocationManager.IsTwoTagMode(method, space);
			manager.SpaceAlignmentChanged(rebase: wasTwoTag != isTwoTag);
			return true;
		}

		public string TagRegistrationBlocker() =>
			space == null ? L("space.load-first") :
			manager.Role == SpaceRole.Client ? L("blocker.only-the-host-can-change-the-map") :
			!TrackingReady || !(IsAligned || FrameIsFree) ? L("alignment.align-before-reference-setup") : null;

		/// <summary>Registers a tag where it is seen, in the world frame. The first one of a free frame defines it.</summary>
		public bool RegisterTag(int id, Pose worldPose)
		{
			if (TagRegistrationBlocker() != null || id < 0 || !Space.ValidPose(worldPose)) return false;
			space.SetTag(id, worldPose);
			manager.SpaceAlignmentChanged(rebase: false);
			return true;
		}

		public string TagRemovalBlocker() =>
			space == null ? L("space.load-first") :
			manager.Role == SpaceRole.Client ? L("blocker.only-the-host-can-change-the-map") : null;

		/// <summary>Removes a tag and the anchors realized at it. Removing the last one enters two-tag mode: a new frame.</summary>
		public bool UnregisterTag(int id)
		{
			if (TagRemovalBlocker() != null || !space.TryGetTag(id, out _)) return false;
			space.tags.RemoveAll(tag => tag.id == id);
			List<string> orphaned = new();
			foreach (AnchorRef anchor in space.anchors) if (anchor.tagId == id) orphaned.Add(anchor.guid);
			space.anchors.RemoveAll(anchor => anchor.tagId == id);
			if (privateReferences != null)
			{
				foreach (TagAnchorRef anchor in privateReferences.tagAnchors) if (anchor.tagId == id) orphaned.Add(anchor.guid);
				if (privateReferences.tagAnchors.RemoveAll(anchor => anchor.tagId == id) > 0) store.SavePrivate(privateReferences);
			}
			EraseUnreferenced(orphaned);
			bool entersTwoTag = space.alignment.method == Method.AprilTag && !space.HasTags;
			manager.SpaceAlignmentChanged(rebase: entersTwoTag);
			return true;
		}

		public void UnregisterAllTags()
		{
			if (space == null) return;
			foreach (TagRef tag in new List<TagRef>(space.tags)) UnregisterTag(tag.id);
		}

		public string TagSizeBlocker() =>
			space == null ? L("space.load-first") :
			manager.Role == SpaceRole.Client ? L("blocker.only-the-host-can-change-the-map") :
			space.HasTags ? L("blocker.unregister-this-map-s-tags-to-change-their-size") : null;

		public bool SetTagSize(float centimeters)
		{
			if (TagSizeBlocker() != null || !Space.Finite(centimeters) || centimeters <= 0) return false;
			if (Mathf.Abs(space.alignment.tagSizeCm - centimeters) < .001f) return true;
			space.alignment.tagSizeCm = centimeters;
			manager.SpaceAlignmentChanged(rebase: false);
			return true;
		}

		/// <summary>Forgets the two-tag pair; the next two tags seen define a new frame.</summary>
		public bool ChooseAnotherTagPair()
		{
			if (space == null || manager.Role == SpaceRole.Client || RoundInProgress) return false;
			bool hadPair = space.alignment.HasPair;
			space.alignment.firstTagId = space.alignment.secondTagId = -1;
			manager.SpaceAlignmentChanged(rebase: hadPair && colocation.UsingTwoTagMode);
			return true;
		}

		/// <summary>Forgets every reference of the shown space; the manager gives it a new frame.</summary>
		public void ForgetReferences()
		{
			if (space == null) return;
			List<string> orphaned = new();
			foreach (AnchorRef anchor in space.anchors) orphaned.Add(anchor.guid);
			if (privateReferences != null) foreach (TagAnchorRef anchor in privateReferences.tagAnchors) orphaned.Add(anchor.guid);
			space.tags.Clear();
			space.anchors.Clear();
			space.alignment.firstTagId = space.alignment.secondTagId = -1;
			EraseUnreferenced(orphaned);
		}

		/// <summary>Erases the device saves of anchors no space refers to any more.</summary>
		public void EraseUnreferenced(IEnumerable<string> guids)
		{
			HashSet<string> referenced = new();
			foreach (Space other in manager.Spaces)
			{
				foreach (AnchorRef anchor in other.anchors) referenced.Add(anchor.guid);
				PrivateReferences references = ReferenceEquals(other, space) ? privateReferences : store.LoadPrivate(other.id);
				if (references != null) foreach (TagAnchorRef anchor in references.tagAnchors) referenced.Add(anchor.guid);
			}
			foreach (string guid in guids)
				if (!referenced.Contains(guid) && Guid.TryParseExact(guid, "N", out Guid parsed))
					_ = Anchors.EraseAsync(parsed);
		}

		// ------- the anchor provider's session policy ----------------

		ulong? IAnchorSessionPolicy.SelectMinter()
		{
			if (space == null) return null;
			if (!HeadsetConfiguration.IsOperatorDevice) return TrackingReady ? SyncBus.LocalClientId : null;
			// an operator has no rig: the lowest ready headset in this frame mints for it
			ulong? chosen = null;
			foreach ((ulong client, PlayerAvatar avatar) in PlayerAvatar.All)
				if (ReadyFor(client) && (chosen == null || client < chosen)) chosen = client;
			return chosen;
		}

		bool IAnchorSessionPolicy.LocallyReady => TrackingReady;
		bool IAnchorSessionPolicy.MayMint => space != null && (IsAligned || FrameIsFree);
		bool IAnchorSessionPolicy.MayPrepare => space != null && (IsAligned || FrameIsFree);
		bool IAnchorSessionPolicy.MayMintForPreparation => space != null && (IsAligned || FrameIsFree);
		int IAnchorSessionPolicy.TrackingGeneration => colocation.TrackingGeneration;
		bool IAnchorSessionPolicy.ValidateRemoteMint(ulong sender) => ReadyFor(sender);
		bool IAnchorSessionPolicy.ValidateTrackingGeneration(ulong sender, int generation) => generation == SenderTrackingGeneration(sender);
		bool IAnchorSessionPolicy.ValidatePreparedAnchor(ulong sender, AnchorConstraintData anchor) => ReadyFor(sender);

		void IAnchorSessionPolicy.GetSharingCandidates(List<AnchorConstraintData> results)
		{
			if (colocation.ActiveMethod == Method.AprilTag)
			{
				List<TaggedAnchorConstraintData> local = new();
				Tags.GetLocalAnchorConstraints(local);
				foreach (TaggedAnchorConstraintData anchor in local) results.Add(new(anchor.guid, anchor.canonPose, anchor.tagId));
			}
			else foreach ((Guid guid, AnchorConstraintState state) in Anchors.Constraints) results.Add(new(guid, state.canonPose, state.bindingId));
		}

		/// <summary>A sender may author references: tracked, focused, in this space and frame, and aligned unless the frame is still free.</summary>
		private bool ReadyFor(ulong sender)
		{
			if (space == null) return false;
			if (sender == SyncBus.LocalClientId) return !HeadsetConfiguration.IsOperatorDevice && TrackingReady && (IsAligned || FrameIsFree);
			if (!PlayerAvatar.All.TryGetValue(sender, out PlayerAvatar avatar) || !avatar || !avatar.IsSpawned || !avatar.HeadsetStatus) return false;
			HeadsetReadiness readiness = avatar.HeadsetStatus.Readiness;
			return readiness.isFocused && readiness.isHeadTracked && !readiness.isOperator &&
				readiness.spaceId == space.Id && readiness.frameId == space.FrameId && (readiness.aligned || FrameIsFree);
		}

		private int SenderTrackingGeneration(ulong sender)
		{
			if (sender == SyncBus.LocalClientId) return colocation.TrackingGeneration;
			return PlayerAvatar.All.TryGetValue(sender, out PlayerAvatar avatar) && avatar && avatar.HeadsetStatus
				? avatar.HeadsetStatus.Readiness.trackingGeneration : -1;
		}
	}
}
