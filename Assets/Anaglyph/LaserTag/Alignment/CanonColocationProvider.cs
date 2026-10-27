using System;
using System.Collections.Generic;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.SharedSpaces;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Colocation against the session authority's frame, found by matching what this
	/// headset senses against a map rather than through anchors, tags or a platform
	/// service. The authority's frame is canon by definition, so the authority (and
	/// anyone offline) is aligned at once; everyone else searches for it. A recenter,
	/// sleep or tracking loss makes the applied alignment stale, and the headset
	/// relocalizes against the map it already holds.
	///
	/// A subclass finds the alignments (<see cref="VisualColocationConstraintProvider"/> from
	/// camera landmarks); this holds the phases and the single constraint the
	/// <see cref="Colocator"/> aligns the rig with, exactly as an anchor would be, moved by
	/// whatever <see cref="SiteCorrection"/> the provider has measured since.
	/// </summary>
	public abstract class CanonColocationProvider : MonoBehaviour, IColocationConstraintProvider
	{
		public enum Phase { Stopped, Aligned, Searching, Registering, Relocalizing }

		/// <summary>The provider the colocation manager has active, if it is one of these.</summary>
		public static CanonColocationProvider Active =>
			ColocationManager.Instance != null ? ColocationManager.Instance.ActiveProvider as CanonColocationProvider : null;

		public Phase CurrentPhase
		{
			get => currentPhase;
			protected set
			{
				if (currentPhase == value) return;
				currentPhase = value;
				PhaseChanged.Invoke(value);
			}
		}

		private Phase currentPhase = Phase.Stopped;

		/// <summary>Raised after <see cref="CurrentPhase"/> changes, with the new phase.</summary>
		public event Action<Phase> PhaseChanged = delegate { };

		private bool alignmentApplied;

		// where the alignment says the tracking space belongs. Identical for the authority
		private Pose canonSpacePose;

		/// <summary>Where the alignment put the tracking space, before any drift correction.</summary>
		public Pose CanonSpacePose => canonSpacePose;

		/// <summary>Bumped whenever a new alignment is found (colocation, relocalization, becoming the authority).</summary>
		public int AlignmentGeneration { get; private set; }

		/// <summary>
		/// Whether the current alignment was found by matching against a map, rather than
		/// taken as identity by the authority
		/// </summary>
		public bool AlignmentIsRegistered { get; private set; }

		// bumped by every recenter: the sensor's world frame moved, so anything sensed
		// before it, and any match still running on it, describes a frame that is gone
		private int frameEpoch;

		/// <summary>Bumped by every recenter; an alignment found before one describes a frame that is gone.</summary>
		public int FrameEpoch => frameEpoch;

		/// <summary>
		/// The drift correction the rig is being aligned by, on top of the found alignment:
		/// the tracking space goes to SiteCorrection ∘ <see cref="CanonSpacePose"/>.
		/// Reset whenever a new alignment is found, which is found against the map as it is
		/// </summary>
		public Pose4 SiteCorrection { get; set; } = Pose4.Identity;

		/// <summary>Where the tracking space belongs now: the alignment, corrected.</summary>
		public Pose TargetSpacePose => SiteCorrection.Apply(canonSpacePose);

		public virtual bool IsAvailable => EnvScanner.Instance != null && MainXRRig.Instance != null;
		public bool IsRunning => CurrentPhase != Phase.Stopped;

		/// <summary>The latest attempt and what came of it, for diagnosis.</summary>
		public abstract string LastAttemptDescription { get; }

		protected string LogTag => $"[{GetType().Name}]";

		protected virtual void Awake()
		{
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
			MainXRRig.Recentered += OnRecentered;
		}

		protected virtual void OnDestroy()
		{
			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			MainXRRig.Recentered -= OnRecentered;
			StopProviding();
		}

		// ---- provider lifecycle ------------------------------------------------

		public void StartProviding()
		{
			if (IsRunning) return;
			EnterRole();
		}

		public void StopProviding()
		{
			if (CurrentPhase != Phase.Stopped) Debug.Log($"{LogTag} stopped (was {CurrentPhase})");
			CurrentPhase = Phase.Stopped;
			ResetWorkingState();
		}

		public virtual void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (CurrentPhase != Phase.Aligned || !IsAvailable) return;
			Transform space = MainXRRig.TrackingSpace;
			if (!space) return;

			// observed drifts toward canon as the colocator moves the rig, then the two
			// coincide and the fit holds still
			results.Add(new ColocationConstraint(new Pose(space.position, space.rotation),
				TargetSpacePose, hasReliableRotation: true));
		}

		private void OnBusActivated() { if (IsRunning) EnterRole(true); }
		private void OnBusDeactivated() { if (IsRunning) EnterRole(true); }
		private void OnAuthorityChanged(bool _) { if (IsRunning) EnterRole(true); }

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
			ResetWorkingState();
			Transform space = MainXRRig.TrackingSpace;
			SetAlignment(space ? new Pose(space.position, space.rotation) : Pose.identity, false);
		}

		private void BeginSearch()
		{
			if (CurrentPhase is Phase.Searching or Phase.Registering) return;
			ResetWorkingState();
			CurrentPhase = Phase.Searching;
			Debug.Log($"{LogTag} searching for the authority's frame");
		}

		/// <summary>Drops whatever the current phase was working with (snapshots, scratch scans, candidates).</summary>
		protected virtual void ResetWorkingState() { }

		/// <summary>
		/// <paramref name="worldToCanon"/> maps the world as it stood when the evidence was
		/// sensed, with the tracking space at <paramref name="spaceAtCapture"/>, onto the
		/// canon frame
		/// </summary>
		protected void ApplyAlignment(Pose4 worldToCanon, Pose spaceAtCapture) =>
			SetAlignment(worldToCanon.Apply(spaceAtCapture), true);

		private void SetAlignment(Pose canon, bool registered)
		{
			canonSpacePose = canon;
			AlignmentGeneration++;
			SiteCorrection = Pose4.Identity;
			AlignmentIsRegistered = registered;
			alignmentApplied = false;
			CurrentPhase = Phase.Aligned;
		}

		// ---- relocalization ----------------------------------------------------

		private void OnRecentered()
		{
			frameEpoch++;
			switch (CurrentPhase)
			{
				case Phase.Aligned:
					BeginRelocalize("recenter");
					break;
				case Phase.Relocalizing:
					OnRecenteredWhileRelocalizing();
					break;
				case Phase.Searching:
				case Phase.Registering:
					OnRecenteredWhileSearching();
					break;
			}
		}

		/// <summary>Whether anything is mapped yet to relocalize against.</summary>
		protected abstract bool HasMapToRelocalizeAgainst { get; }

		protected virtual void OnRelocalizeBegun() { }
		protected virtual void OnRecenteredWhileRelocalizing() { }
		protected virtual void OnRecenteredWhileSearching() { }

		// the colocator drops to Lost on tracking loss, sleep and recenter; the alignment
		// it keeps applied is stale, and only a fresh match can say by how much
		/// <returns>Whether relocalizing began.</returns>
		protected bool BeginRelocalize(string why)
		{
			if (CurrentPhase != Phase.Aligned) return false;
			EnvScanner scanner = EnvScanner.Instance;
			if (scanner == null) return false;

			// nothing mapped yet to match against (XROrigin setting the tracking origin mode
			// at startup raises a recenter): relocalizing would wait forever with the live
			// scan shut. The authority's frame is canon by definition, so it re-anchors on
			// the new one; anyone else looks for the authority again
			if (!HasMapToRelocalizeAgainst)
			{
				Debug.Log($"{LogTag} {why} with no map to relocalize against: {(SyncBus.IsAuthority ? "re-anchoring" : "searching again")}");
				scanner.Clear();
				EnterRole();
				return false;
			}

			CurrentPhase = Phase.Relocalizing;
			OnRelocalizeBegun();
			Debug.Log($"{LogTag} relocalizing ({why})");
			return true;
		}

		/// <summary>Stops relocalizing and takes the role's own alignment again: the authority's frame as it stands, or a search.</summary>
		public void AbandonRelocalization()
		{
			if (CurrentPhase != Phase.Relocalizing) return;
			Debug.Log($"{LogTag} relocalization abandoned");
			EnterRole();
		}

		protected virtual void Update()
		{
			// the colocator reports Lost until its next fit applies a fresh alignment,
			// so a loss only counts once the alignment we last produced was in use
			if (ColocationManager.Instance == null) return;
			ColocationAlignmentState state = ColocationManager.Instance.AlignmentState;
			if (state == ColocationAlignmentState.Localized) alignmentApplied = true;
			else if (state == ColocationAlignmentState.Lost && alignmentApplied && CurrentPhase == Phase.Aligned)
			{
				alignmentApplied = false;
				BeginRelocalize("tracking lost");
			}
		}
	}
}
