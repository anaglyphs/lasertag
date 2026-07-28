using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// Aligns the rig against the constraints supplied by the selected provider. Providers own
	/// discovery, persistence, and synchronization; this class only fits their observed anchor
	/// poses to their canon poses. Selecting a provider stops the previous one, so two anchor
	/// strategies can never manipulate the same runtime concurrently.
	/// </summary>
	[DefaultExecutionOrder(999)]
	public class ConstraintColocator : MonoBehaviour, IColocator
	{
		public const int MinimumPositionOnlyConstraintCount = 2;

		public ColocationState State { get; private set; } = ColocationState.Stopped;
		public event Action<ColocationState> StateChanged = delegate { };

		[Tooltip("How quickly to ease onto a new fit once already localized. 1 = snap")]
		[SerializeField] private float fitLerp = 0.1f;

		private IColocationConstraintProvider provider;
		public IColocationConstraintProvider Provider => provider;

		private CancellationTokenSource ctknSrc;

		private readonly List<ColocationConstraint> constraints = new();
		private readonly List<(float3 subject, float3 target)> positionPairs = new();

		public void SetProvider(IColocationConstraintProvider next)
		{
			if (ReferenceEquals(provider, next))
				return;

			if (State != ColocationState.Stopped)
				StopColocation();

			provider = next;
		}

		/// <summary>Appends the references currently used by the fit.</summary>
		public void GetCurrentConstraints(List<ColocationConstraint> results)
		{
			provider?.GetColocationConstraints(results);
		}

		private void SetState(ColocationState next)
		{
			if (State == next) return;
			State = next;
			StateChanged.Invoke(next);
		}

		// Alignment we already had is stale but still applied, so this is Lost rather than
		// Searching. Never demotes a colocator that was never aligned in the first place.
		private void Delocalize()
		{
			if (State == ColocationState.Localized)
				SetState(ColocationState.Lost);
		}

		private void OnDestroy()
		{
			StopColocation();
		}

		private void OnApplicationFocus(bool isFocused)
		{
			#if UNITY_EDITOR
			return;
			#endif

			// Device sleeping = tracking pauses = tracking lost. Prevents trusting a stale
			// frame (and everything downstream that writes world-space data) on wake, until
			// the constraints align it again.
			if (!isFocused)
				Delocalize();
		}

		public void StartColocation()
		{
			if (State != ColocationState.Stopped || provider == null)
				return;

			provider.StartProviding();

			#if UNITY_EDITOR
			// No anchor runtime in-editor. Report an identity frame as localized so map
			// editing and everything else gated on colocation is testable off-device.
			SetState(ColocationState.Localized);
			return;
			#endif

			ctknSrc = new CancellationTokenSource();
			SetState(ColocationState.Searching);
			AlignLoop(ctknSrc.Token);
		}

		public void StopColocation()
		{
			if (State == ColocationState.Stopped)
				return;

			ctknSrc?.Cancel();
			ctknSrc = null;
			provider?.StopProviding();
			SetState(ColocationState.Stopped);
		}

		private async void AlignLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.NextFrameAsync(ctkn);

					constraints.Clear();
					provider?.GetColocationConstraints(constraints);

					if (!TryFit())
						Delocalize();
					else
						SetState(ColocationState.Localized);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				AlignLoop(ctkn);
			}
		}

		private bool TryFit()
		{
			if (constraints.Count == 0)
				return false;

			// A constraint with a trustworthy rotation (an anchor) fully constrains the fit on
			// its own. Position-only constraints (tags) need two horizontally separated points
			// to constrain yaw as well as translation.
			int rotationBearing = 0;
			foreach (ColocationConstraint constraint in constraints)
				if (constraint.hasReliableRotation)
					rotationBearing++;

			if (rotationBearing == 0 &&
			    constraints.Count < MinimumPositionOnlyConstraintCount)
				return false;

			// First fit after Searching or Lost snaps, so the world arrives where it belongs
			// immediately; later ones ease in, so drift corrections don't pop.
			float lerp = State == ColocationState.Localized ? fitLerp : 1f;

			Transform space = MainXRRig.TrackingSpace;
			Matrix4x4 spaceMat = space.localToWorldMatrix;

			if (constraints.Count == 1)
			{
				// Single pose-to-pose alignment. Only reachable with a rotation-bearing
				// constraint, per the check above.
				Pose s = constraints[0].observed;
				Pose t = constraints[0].canon;

				MainXRRig.Instance.AlignSpace(
					Matrix4x4.TRS(s.position, s.rotation, Vector3.one),
					Matrix4x4.TRS(t.position, t.rotation, Vector3.one),
					lerp);
			}
			else
			{
				positionPairs.Clear();
				foreach (ColocationConstraint constraint in constraints)
					positionPairs.Add((constraint.observed.position, constraint.canon.position));

				Matrix4x4 delta = BestFit.Find4DOF(positionPairs);
				MainXRRig.Instance.AlignSpace(spaceMat, delta * spaceMat, lerp);
			}

			// A degenerate fit (coincident constraints, NaN input) can throw tracking space
			// somewhere unusable; reset rather than leave the player lost in space.
			Vector3 spacePos = space.position;
			if (spacePos.magnitude > 10000f ||
			    float.IsNaN(spacePos.x) || float.IsInfinity(spacePos.x) ||
			    float.IsNaN(spacePos.y) || float.IsInfinity(spacePos.y) ||
			    float.IsNaN(spacePos.z) || float.IsInfinity(spacePos.z))
				space.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

			return true;
		}
	}
}
