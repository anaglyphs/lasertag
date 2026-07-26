using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// Aligns the rig against anchor references supplied by an
	/// <see cref="IColocationReferenceSource"/> — in practice the map system, which owns
	/// anchor creation, persistence, sharing, and canon poses. This component only fits:
	/// one reference aligns pose-to-pose, several best-fit as a rigid set, none means the
	/// last alignment is stale.
	/// </summary>
	[DefaultExecutionOrder(999)]
	public class MetaAnchorColocator : MonoBehaviour, IColocator
	{
		public ColocationState State { get; private set; } = ColocationState.Stopped;
		public event Action<ColocationState> StateChanged = delegate { };

		/// <summary>Injected by the map system before colocation starts.</summary>
		public IColocationReferenceSource ReferenceSource { get; set; }

		private CancellationTokenSource ctknSrc;

		private readonly List<ColocationReference> references = new();
		private readonly List<(float3 subject, float3 target)> positionPairs = new();

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
			// the references align it again.
			if (!isFocused)
				Delocalize();
		}

		public void StartColocation()
		{
			#if UNITY_EDITOR
			// No anchor runtime in-editor. Report an identity frame as localized so map
			// editing and everything else gated on colocation is testable off-device.
			SetState(ColocationState.Localized);
			return;
			#endif

			if (State != ColocationState.Stopped) return;

			ctknSrc = new CancellationTokenSource();
			SetState(ColocationState.Searching);
			AlignLoop(ctknSrc.Token);
		}

		public void StopColocation()
		{
			if (State == ColocationState.Stopped) return;

			ctknSrc?.Cancel();
			ctknSrc = null;
			SetState(ColocationState.Stopped);
		}

		private async void AlignLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.NextFrameAsync(ctkn);

					references.Clear();
					ReferenceSource?.GetColocationReferences(references);

					switch (references.Count)
					{
						case 1: // single reference: align pose-to-pose

							Pose s = references[0].observed;
							Pose t = references[0].canon;

							Matrix4x4 observedMat = Matrix4x4.TRS(s.position, s.rotation, Vector3.one);
							Matrix4x4 canonMat = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);

							MainXRRig.Instance.AlignSpace(observedMat, canonMat);

							SetState(ColocationState.Localized);

							break;

						case > 1: // several references: rigid best fit over positions

							positionPairs.Clear();
							foreach (ColocationReference reference in references)
								positionPairs.Add(
									(reference.observed.position, reference.canon.position));

							float4x4 fitShift = BestFit.Find4DOF(positionPairs);
							MainXRRig.Instance.ShiftSpace(fitShift);

							SetState(ColocationState.Localized);

							break;

						default:
							// Every reference went untracked. The last fit is still applied, so
							// the world doesn't jump — it's just no longer trustworthy.
							Delocalize();

							break;
					}
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
	}
}
