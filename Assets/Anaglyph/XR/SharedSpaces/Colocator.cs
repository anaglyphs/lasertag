using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// How far a colocator currently trusts the alignment it has produced between tracking
	/// space and the canon (world) frame.
	/// </summary>
	public enum ColocationAlignmentState
	{
		/// <summary>Not running.</summary>
		Stopped,

		/// <summary>
		/// Running, but has not yet found enough references to align. There is no meaningful
		/// world frame at all.
		/// </summary>
		Searching,

		/// <summary>Aligned. World space can be trusted.</summary>
		Localized,

		/// <summary>
		/// Was localized and isn't anymore — recenter, sleep/wake, tracking loss, or the
		/// references went out of view. The last alignment is still applied, so the world
		/// doesn't visibly jump, but it is stale: anything that writes durable world-space
		/// data (anchor canon poses, map object poses) must stop until this clears.
		///
		/// Distinct from <see cref="Searching"/> because a stale frame is still worth drawing
		/// and worth keeping a map loaded against; no frame at all is not.
		/// </summary>
		Lost
	}

	/// <summary>
	/// How closely the applied alignment lands each constraint on its own canon pose.
	///
	/// Counted per constraint as well as averaged: a single reference metres out would
	/// otherwise hide inside a mean taken over many good ones.
	/// </summary>
	public readonly struct FitAgreement
	{
		public FitAgreement(int constraintCount, int agreeingCount, float meanError, bool meanAgrees)
		{
			this.constraintCount = constraintCount;
			this.agreeingCount = agreeingCount;
			this.meanError = meanError;
			this.meanAgrees = meanAgrees;
		}

		/// <summary>How many constraints the provider is currently observing.</summary>
		public readonly int constraintCount;

		/// <summary>How many of them land within <see cref="Colocator.AgreementMaxError"/>.</summary>
		public readonly int agreeingCount;

		/// <summary>Mean error beyond the drift each constraint has had time to gather since it was measured; live ones are allowed none.</summary>
		public readonly float meanError;

		/// <summary>
		/// Whether <see cref="meanError"/> is within tolerance. Carried here so a caller can judge
		/// the fit without also having to hold the threshold that produced
		/// <see cref="agreeingCount"/>. False when there is nothing to measure.
		/// </summary>
		public readonly bool meanAgrees;
	}

	/// <summary>
	/// Aligns the rig against the constraints supplied by the selected provider. Providers own
	/// discovery, persistence, and synchronization; this class only fits their observed anchor
	/// poses to their canon poses. Selecting a provider stops the previous one, so two anchor
	/// strategies can never manipulate the same runtime concurrently.
	/// </summary>
	[DefaultExecutionOrder(999)]
	public class Colocator : MonoBehaviour
	{
		public static Colocator Instance;
		
		public const int MinimumPositionOnlyConstraintCount = 2;

		public ColocationAlignmentState AlignmentState { get; private set; } = ColocationAlignmentState.Stopped;
		public event Action<ColocationAlignmentState> StateChanged = delegate { };

		[Tooltip("Reference error above which a constraint does not count as agreeing with the fit")]
		[SerializeField] private float agreementMaxError = 0.3f;

		public float AgreementMaxError => agreementMaxError;

		[Tooltip("Position uncertainty the fit gains as the head walks on, meters per √meter walked: tracking drifts like a random walk, so measurements taken further back count for less. Live references count as measured now")]
		[SerializeField] private float positionDriftRate = 0.014f;

		[Tooltip("Position uncertainty the fit itself gains as the head walks on, meters per √meter walked along each axis: on device long walks gather about 4 cm/√m across the floor, more than the short stretches the drift model above reasons about")]
		[SerializeField] private float fitPositionDriftRate = 0.028f;

		[Tooltip("Yaw uncertainty the fit gains as the head walks on, degrees per √meter walked")]
		[SerializeField] private float yawDriftRateDegrees = 0.05f;

		[Tooltip("Position uncertainty a constraint carries per meter it lies from the head, meters per meter: tracking errs differently in different places, so a correction measured across the room says less about here than one measured here. On device a site 10 m from a table needed 2° and 25 cm that the table itself didn't")]
		[SerializeField] private float spatialDriftRate = 0.03f;

		[Tooltip("Yaw uncertainty a constraint carries per meter it lies from the head, degrees per meter")]
		[SerializeField] private float spatialYawDriftRateDegrees = 0.15f;

		[Tooltip("The rig follows the fit only as fast as the head's own movement hides. Off, the rig stands where the fit says every frame")]
		[SerializeField] private bool hideCorrectionsInHeadMovement;

		[Tooltip("Correction the rig takes per meter the head moves, meters: 0.05 scored the same as unlimited over 14 simulated runs, 0.02 cost 0.4 cm")]
		[SerializeField] private float correctionPerMeterOfHeadMovement = 0.05f;

		[Tooltip("A correction larger than this at the head is applied at once, meters: a new alignment or a recenter, which no movement hides. Below Agreement Max Error, so a correction still on its way never reads as disagreement")]
		[SerializeField] private float largestHiddenCorrection = 0.2f;

		// a yaw correction counts as the distance it moves things this far from the head, meters
		private const float YawCorrectionLever = 1f;

		private Vector3 headInTrackingSpaceLastFit;

		/// <summary>How fast this headset's tracking drifts, for everything that reasons about it.</summary>
		public DriftModel DriftRates => new(positionDriftRate, yawDriftRateDegrees * Mathf.Deg2Rad);

		/// <summary>How constraints' uncertainty stands now, for this headset's tracking: what the fit weighs them by.</summary>
		public ColocationDrift Drift => new(fitPositionDriftRate, yawDriftRateDegrees * Mathf.Deg2Rad,
			HeadPoseHistory.Instance != null ? HeadPoseHistory.Instance.Path : 0f,
			MainXRRig.Instance != null ? MainXRRig.Camera.transform.position : Vector3.zero,
			MainXRRig.Instance != null ? spatialDriftRate : 0f, MainXRRig.Instance != null ? spatialYawDriftRateDegrees * Mathf.Deg2Rad : 0f);

		/// <summary>
		/// How well the alignment currently applied fits the references being observed. Measured
		/// before each new fit, so it describes the alignment the rest of the frame is standing in
		/// rather than the one about to replace it. Zeroed whenever there is nothing to measure.
		/// </summary>
		public FitAgreement Agreement { get; private set; }

		public IColocationConstraintProvider Provider { get; private set; }

		/// <summary>
		/// Raised every frame the fit aligned the rig, right after it moved, with the world pose
		/// the fit gives the tracking space. The rig stands there unless corrections are being
		/// hidden in head movement, when it is still on its way.
		/// </summary>
		public event Action<Pose> Fitted = delegate { };

		private readonly List<ColocationConstraint> constraints = new();
		private readonly ColocationFit fit = new();

		private void Awake()
		{
			Instance = this;
		}

		public void SetProvider(IColocationConstraintProvider next)
		{
			if (ReferenceEquals(Provider, next))
				return;

			if (AlignmentState != ColocationAlignmentState.Stopped)
				StopColocation();

			Provider = next;
			Agreement = default;
			loggedNoReferenceRuntime = false;
		}

		/// <summary>Appends the references currently used by the fit.</summary>
		public void GetCurrentConstraints(List<ColocationConstraint> results)
		{
			Provider?.GetColocationConstraints(results);
		}

		private void SetState(ColocationAlignmentState next)
		{
			if (AlignmentState == next) return;
			AlignmentState = next;
			StateChanged.Invoke(next);
		}

		// Alignment we already had is stale but still applied, so this is Lost rather than
		// Searching. Never demotes a colocator that was never aligned in the first place.
		public void InvalidateTracking() => Delocalize();
		private void Delocalize()
		{
			if (AlignmentState == ColocationAlignmentState.Localized)
				SetState(ColocationAlignmentState.Lost);
		}

		private void OnDestroy()
		{
			StopColocation();
			fit.Dispose();
		}

		private void OnApplicationFocus(bool isFocused)
		{
			// Device sleeping = tracking pauses = tracking lost. Prevents trusting a stale
			// frame (and everything downstream that writes world-space data) on wake, until
			// the constraints align it again.
			if (AppFocus.IsLoss(isFocused))
				Delocalize();
		}

		public void StartColocation()
		{
			if (AlignmentState != ColocationAlignmentState.Stopped || Provider == null)
				return;

			Provider.StartProviding();
			SetState(ColocationAlignmentState.Searching);
		}

		public void StopColocation()
		{
			if (AlignmentState == ColocationAlignmentState.Stopped)
				return;

			Provider?.StopProviding();
			Agreement = default;
			SetState(ColocationAlignmentState.Stopped);
		}

		// late in the frame (DefaultExecutionOrder), so providers have updated their constraints
		// and the fit is the frame's one answer: anything reading the alignment after it,
		// through Fitted, sees where the fit puts the rig
		private void Update()
		{
			if (AlignmentState == ColocationAlignmentState.Stopped) return;

			// An unavailable reference runtime supplies no alignment evidence.
			if (Provider != null && !Provider.IsAvailable)
			{
				WarnNoReferenceRuntimeOnce();
				Agreement = default;
				Delocalize();
				return;
			}

			constraints.Clear();
			Provider?.GetColocationConstraints(constraints);
			MeasureAgreement();

			if (!TryFit(out Pose fittedSpace))
			{
				Delocalize();
				return;
			}

			SetState(ColocationAlignmentState.Localized);
			Fitted.Invoke(fittedSpace);
		}

		private bool loggedNoReferenceRuntime;

		private void WarnNoReferenceRuntimeOnce()
		{
			if (loggedNoReferenceRuntime)
				return;

			loggedNoReferenceRuntime = true;
			Debug.LogWarning($"{Provider.GetType().Name} has no reference runtime available. " +
				"Waiting for an available reference runtime.", this);
		}

		private void MeasureAgreement()
		{
			if (constraints.Count == 0)
			{
				Agreement = default;
				return;
			}

			int agreeing = 0;
			float errorSum = 0f;
			ColocationDrift drift = Drift;

			foreach (ColocationConstraint constraint in constraints)
			{
				// a measurement taken a walk ago, or across the room, is allowed the drift it has had
				// time and distance to gather (2σ)
				float allowance = 2f * Mathf.Sqrt(drift.positionRate * drift.positionRate * drift.Age(constraint) + drift.SpatialVariance(constraint));
				float error = Mathf.Max(0f, Vector3.Distance(
					constraint.observed.position, constraint.canon.position) - allowance);

				errorSum += error;
				if (error <= agreementMaxError)
					agreeing++;
			}

			float meanError = errorSum / constraints.Count;
			Agreement = new FitAgreement(
				constraints.Count, agreeing, meanError, meanError <= agreementMaxError);
		}

		/// <summary>Transfers one rig writer after external validation; it retains the current alignment state.</summary>
		public void Handoff(IColocationConstraintProvider next)
		{
			if (ReferenceEquals(Provider, next)) return;
			if (AlignmentState != ColocationAlignmentState.Localized) { SetProvider(next); StartColocation(); return; }
			next.StartProviding();
			var previous = Provider;
			Provider = next;
			Agreement = default;
			previous?.StopProviding();
		}

		// the fit is always reported whole, so nothing downstream has to tell a goal from where
		// the rig stands; only the rig itself may trail it
		private bool TryFit(out Pose fittedSpace)
		{
			fittedSpace = default;
			if (!MainXRRig.Instance || !fit.TryEvaluate(constraints, Drift, out Pose delta)) return false;
			Matrix4x4 current = MainXRRig.TrackingSpace.localToWorldMatrix;
			Matrix4x4 fitted = Matrix4x4.TRS(delta.position, delta.rotation, Vector3.one) * current;
			float shareOfCorrectionApplied = ShareOfCorrectionHiddenByHeadMovement(delta);
			MainXRRig.Instance.AlignSpace(current, fitted, shareOfCorrectionApplied);
			fittedSpace = new Pose(fitted.GetPosition(), fitted.rotation);
			return true;
		}

		// 1 applies the fit whole: the limit off, the first fit after being stopped or lost, or
		// a correction too large to hide
		private float ShareOfCorrectionHiddenByHeadMovement(Pose delta)
		{
			Vector3 head = MainXRRig.Camera.transform.position;
			Vector3 headInTrackingSpace = MainXRRig.TrackingSpace.InverseTransformPoint(head);
			float headMovement = Vector3.Distance(headInTrackingSpace, headInTrackingSpaceLastFit);
			headInTrackingSpaceLastFit = headInTrackingSpace;
			if (!hideCorrectionsInHeadMovement || AlignmentState != ColocationAlignmentState.Localized) return 1f;

			float correction = Vector3.Distance(delta.rotation * head + delta.position, head) +
				Quaternion.Angle(Quaternion.identity, delta.rotation) * Mathf.Deg2Rad * YawCorrectionLever;
			if (correction > largestHiddenCorrection) return 1f;
			float hidden = correctionPerMeterOfHeadMovement * headMovement;
			return hidden >= correction ? 1f : hidden / correction;
		}
	}
}
