using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
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
		public const int MinimumPositionOnlyConstraintCount = 2;

		public ColocationAlignmentState AlignmentState { get; private set; } = ColocationAlignmentState.Stopped;
		public event Action<ColocationAlignmentState> StateChanged = delegate { };

		[Tooltip("How quickly to ease onto a new fit once already localized. 1 = snap")]
		[SerializeField] private float fitLerp = 0.1f;

		[Tooltip("Reference error above which a constraint does not count as agreeing with the fit")]
		[SerializeField] private float agreementMaxError = 0.3f;

		public float AgreementMaxError => agreementMaxError;

		/// <summary>
		/// How well the alignment currently applied fits the references being observed. Measured
		/// before each new fit, so it describes the alignment the rest of the frame is standing in
		/// rather than the one about to replace it. Zeroed whenever there is nothing to measure.
		/// </summary>
		public FitAgreement Agreement { get; private set; }

		public IColocationConstraintProvider Provider { get; private set; }

		private CancellationTokenSource ctknSrc;

		private readonly List<ColocationConstraint> constraints = new();


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
		}

		private void OnApplicationFocus(bool isFocused)
		{
			// Device sleeping = tracking pauses = tracking lost. Prevents trusting a stale
			// frame (and everything downstream that writes world-space data) on wake, until
			// the constraints align it again.
			if (!isFocused)
				Delocalize();
		}

		public void StartColocation()
		{
			if (AlignmentState != ColocationAlignmentState.Stopped || Provider == null)
				return;

			Provider.StartProviding();

			ctknSrc = new CancellationTokenSource();
			SetState(ColocationAlignmentState.Searching);
			AlignLoop(ctknSrc.Token);
		}

		public void StopColocation()
		{
			if (AlignmentState == ColocationAlignmentState.Stopped)
				return;

			ctknSrc?.Cancel();
			ctknSrc = null;
			Provider?.StopProviding();
			Agreement = default;
			SetState(ColocationAlignmentState.Stopped);
		}

		private async void AlignLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.NextFrameAsync(ctkn);

					// An unavailable reference runtime supplies no alignment evidence.
					if (Provider != null && !Provider.IsAvailable)
					{
						WarnNoReferenceRuntimeOnce();
						Agreement = default;
						Delocalize();
						continue;
					}

					constraints.Clear();
					Provider?.GetColocationConstraints(constraints);
					MeasureAgreement();

					if (!TryFit())
						Delocalize();
					else
						SetState(ColocationAlignmentState.Localized);
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

			foreach (ColocationConstraint constraint in constraints)
			{
				float error = Vector3.Distance(
					constraint.observed.position, constraint.canon.position);

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

		private bool TryFit()
		{
			if (!MainXRRig.Instance || !ColocationFit.TryEvaluate(constraints, out Pose delta, out _, out _)) return false;
			float lerp = AlignmentState == ColocationAlignmentState.Localized ? fitLerp : 1f;
			Matrix4x4 current = MainXRRig.TrackingSpace.localToWorldMatrix;
			MainXRRig.Instance.AlignSpace(current, Matrix4x4.TRS(delta.position, delta.rotation, Vector3.one) * current, lerp);
			return true;
		}
	}
}
