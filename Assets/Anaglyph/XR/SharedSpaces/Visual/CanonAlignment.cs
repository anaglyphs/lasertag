using System;
using System.Collections.Generic;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// An alignment against the session authority's frame that was measured once, not a
	/// reference the runtime keeps tracking as it does an anchor or a tag: this holds what was
	/// found as the single constraint the <see cref="Colocator"/> aligns the rig with, exactly
	/// as an anchor would be, moved by whatever <see cref="SiteCorrection"/> has been measured
	/// since. Its owner finds the alignments (<see cref="VisualColocationConstraintProvider"/>
	/// from camera landmarks) and hands them to <see cref="Apply"/>.
	///
	/// The authority's frame is canon by definition, so the authority (and anyone offline) is
	/// aligned at once; everyone else searches for it. A recenter, sleep or tracking loss makes
	/// the applied alignment stale, and the headset relocalizes against the map it already holds.
	/// </summary>
	public sealed class CanonAlignment : IDisposable
	{
		public enum Phase { Stopped, Aligned, Searching, Relocalizing }

		private readonly string logTag;
		private readonly Func<bool> isAvailable;
		private readonly Func<bool> hasMap;

		public CanonAlignment(string logTag, Func<bool> isAvailable, Func<bool> hasMap)
		{
			this.logTag = logTag;
			this.isAvailable = isAvailable;
			this.hasMap = hasMap;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
			MainXRRig.Recentered += OnRecentered;
		}

		public void Dispose()
		{
			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			MainXRRig.Recentered -= OnRecentered;
			Stop();
		}

		public Phase CurrentPhase
		{
			get => currentPhase;
			private set
			{
				if (currentPhase == value) return;
				currentPhase = value;
				PhaseChanged.Invoke(value);
			}
		}

		private Phase currentPhase = Phase.Stopped;

		/// <summary>Raised after <see cref="CurrentPhase"/> changes, with the new phase.</summary>
		public event Action<Phase> PhaseChanged = delegate { };

		/// <summary>
		/// Raised when a recenter leaves nothing mapped to relocalize against: whatever was
		/// sensed in the old frame is gone with it, and the role's own alignment is taken again.
		/// </summary>
		public event Action FrameAbandoned = delegate { };

		public bool IsRunning => CurrentPhase != Phase.Stopped;

		private bool alignmentApplied;

		// where the alignment says the tracking space belongs. Identical for the authority
		private Pose canonSpacePose;

		/// <summary>Where the alignment put the tracking space, before any drift correction.</summary>
		public Pose CanonSpacePose => canonSpacePose;

		/// <summary>Bumped whenever a new alignment is found (colocation, relocalization, becoming the authority).</summary>
		public int Generation { get; private set; }

		/// <summary>Bumped by every recenter; an alignment found before one describes a frame that is gone.</summary>
		public int FrameEpoch { get; private set; }

		/// <summary>
		/// The drift correction the rig is being aligned by, on top of the found alignment:
		/// the tracking space goes to SiteCorrection ∘ <see cref="CanonSpacePose"/>.
		/// Reset whenever a new alignment is found, which is found against the map as it is
		/// </summary>
		public Pose4 SiteCorrection { get; set; } = Pose4.Identity;

		/// <summary>Where the tracking space belongs now: the alignment, corrected.</summary>
		public Pose TargetSpacePose => SiteCorrection.Apply(canonSpacePose);

		public void Start()
		{
			if (IsRunning) return;
			EnterRole();
		}

		public void Stop()
		{
			if (CurrentPhase != Phase.Stopped) Debug.Log($"{logTag} stopped (was {CurrentPhase})");
			CurrentPhase = Phase.Stopped;
		}

		/// <summary>The alignment's own constraint, while aligned.</summary>
		public void AddConstraint(List<ColocationConstraint> results)
		{
			if (CurrentPhase != Phase.Aligned) return;
			Transform space = MainXRRig.TrackingSpace;
			if (!space) return;

			// observed drifts toward canon as the colocator moves the rig, then the two
			// coincide and the fit holds still
			results.Add(new ColocationConstraint(new Pose(space.position, space.rotation),
				TargetSpacePose, hasReliableRotation: true));
		}

		/// <summary>
		/// <paramref name="worldToCanon"/> maps the world as it stood when the evidence was
		/// sensed, with the tracking space at <paramref name="spaceAtCapture"/>, onto the
		/// canon frame
		/// </summary>
		public void Apply(Pose4 worldToCanon, Pose spaceAtCapture) =>
			SetAlignment(worldToCanon.Apply(spaceAtCapture));

		private void SetAlignment(Pose canon)
		{
			canonSpacePose = canon;
			Generation++;
			SiteCorrection = Pose4.Identity;
			alignmentApplied = false;
			CurrentPhase = Phase.Aligned;
		}

		// ---- roles -------------------------------------------------------------

		private void OnBusActivated() => OnRoleEvent("session started");
		private void OnBusDeactivated() => OnRoleEvent("session ended");
		private void OnAuthorityChanged(bool _) => OnRoleEvent("authority changed");

		// an authority already aligned keeps its frame: it is canon as it stands, with the map
		// and every site measured in it. Taking the tracking space as canon again would throw
		// the sites away and fix whatever correction the rig was still easing toward
		private void OnRoleEvent(string why)
		{
			if (!IsRunning) return;
			if (SyncBus.IsAuthority && CurrentPhase == Phase.Aligned)
			{
				Debug.Log($"{logTag} {why}: the authority, keeping its alignment");
				return;
			}

			Debug.Log($"{logTag} {why}: {(!SyncBus.IsAuthority ? "not the authority" : CurrentPhase == Phase.Relocalizing ? "the authority, still relocalizing" : "the authority, taking its own frame")} (was {CurrentPhase})");
			EnterRole(true);
		}

		// the authority's frame is canon by definition; everyone else has to find it. An
		// authority still relocalizing (against a map just loaded, say) keeps at it when the
		// session comes up: taking its frame as it stands would fix the alignment it is
		// looking for at whatever it happens to be
		private void EnterRole(bool keepRelocalizing = false)
		{
			if (!SyncBus.IsAuthority) BeginSearch();
			else if (!keepRelocalizing || CurrentPhase != Phase.Relocalizing) AlignIdentity();
		}

		private void AlignIdentity()
		{
			Transform space = MainXRRig.TrackingSpace;
			SetAlignment(space ? new Pose(space.position, space.rotation) : Pose.identity);
		}

		private void BeginSearch()
		{
			if (CurrentPhase == Phase.Searching) return;
			CurrentPhase = Phase.Searching;
			Debug.Log($"{logTag} searching for the authority's frame");
		}

		// ---- relocalization ----------------------------------------------------

		private void OnRecentered()
		{
			FrameEpoch++;
			if (CurrentPhase == Phase.Aligned) BeginRelocalize("recenter");
		}

		// the colocator drops to Lost on tracking loss, sleep and recenter; the alignment
		// it keeps applied is stale, and only a fresh match can say by how much
		/// <returns>Whether relocalizing began.</returns>
		public bool BeginRelocalize(string why)
		{
			if (CurrentPhase != Phase.Aligned || !isAvailable()) return false;

			// nothing mapped yet to match against (XROrigin setting the tracking origin mode
			// at startup raises a recenter): relocalizing would wait forever with the live
			// scan shut. The authority's frame is canon by definition, so it re-anchors on
			// the new one; anyone else looks for the authority again
			if (!hasMap())
			{
				Debug.Log($"{logTag} {why} with no map to relocalize against: {(SyncBus.IsAuthority ? "re-anchoring" : "searching again")}");
				FrameAbandoned.Invoke();
				EnterRole();
				return false;
			}

			CurrentPhase = Phase.Relocalizing;
			Debug.Log($"{logTag} relocalizing ({why})");
			return true;
		}

		/// <summary>Stops relocalizing and takes the role's own alignment again: the authority's frame as it stands, or a search.</summary>
		public void AbandonRelocalization()
		{
			if (CurrentPhase != Phase.Relocalizing) return;
			Debug.Log($"{logTag} relocalization abandoned");
			EnterRole();
		}

		/// <summary>Call every frame: relocalizes once the colocator loses an alignment this produced.</summary>
		public void Update()
		{
			// the colocator reports Lost until its next fit applies a fresh alignment,
			// so a loss only counts once the alignment we last produced was in use
			if (Colocator.Instance == null) return;
			ColocationAlignmentState state = Colocator.Instance.AlignmentState;
			if (state == ColocationAlignmentState.Localized) alignmentApplied = true;
			else if (state == ColocationAlignmentState.Lost && alignmentApplied && CurrentPhase == Phase.Aligned)
			{
				alignmentApplied = false;
				BeginRelocalize("tracking lost");
			}
		}
	}
}
