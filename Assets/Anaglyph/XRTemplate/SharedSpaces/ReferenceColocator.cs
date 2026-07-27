using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// Aligns the rig against every reference its sources hand it, whatever they are —
	/// spatial anchors, AprilTags, or anything added later. There is only one fitting
	/// implementation because there is only one thing to do: move tracking space so the
	/// observed references land on their canon poses.
	///
	/// A colocation "method" is therefore a choice of which sources are active, not a choice
	/// of colocator. Sources compose: a tag map hosted with shared anchors runs both, and
	/// every reference feeds the same fit.
	/// </summary>
	[DefaultExecutionOrder(999)]
	public class ReferenceColocator : MonoBehaviour, IColocator
	{
		public ColocationState State { get; private set; } = ColocationState.Stopped;
		public event Action<ColocationState> StateChanged = delegate { };

		[Tooltip("How quickly to ease onto a new fit once already localized. 1 = snap")]
		[SerializeField] private float fitLerp = 0.1f;

		private readonly List<IColocationReferenceSource> sources = new();

		private CancellationTokenSource ctknSrc;

		private readonly List<ColocationReference> references = new();
		private readonly List<(float3 subject, float3 target)> positionPairs = new();

		/// <summary>
		/// Adds a source. Sources may come and go while colocation runs; the fit simply uses
		/// whatever references exist each frame.
		/// </summary>
		public void AddSource(IColocationReferenceSource source)
		{
			if (source != null && !sources.Contains(source))
				sources.Add(source);
		}

		public void RemoveSource(IColocationReferenceSource source)
		{
			sources.Remove(source);
		}

		public void ClearSources()
		{
			sources.Clear();
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
					foreach (IColocationReferenceSource source in sources)
						source.GetColocationReferences(references);

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
			if (references.Count == 0)
				return false;

			// A reference with a trustworthy rotation (an anchor) fully constrains the fit on
			// its own. Position-only references (tags) have to triangulate, which takes three.
			int rotationBearing = 0;
			foreach (ColocationReference reference in references)
				if (reference.hasReliableRotation)
					rotationBearing++;

			if (rotationBearing == 0 && references.Count < 3)
				return false;

			// First fit after Searching or Lost snaps, so the world arrives where it belongs
			// immediately; later ones ease in, so drift corrections don't pop.
			float lerp = State == ColocationState.Localized ? fitLerp : 1f;

			Transform space = MainXRRig.TrackingSpace;
			Matrix4x4 spaceMat = space.localToWorldMatrix;

			if (references.Count == 1)
			{
				// Single pose-to-pose alignment. Only reachable with a rotation-bearing
				// reference, per the check above.
				Pose s = references[0].observed;
				Pose t = references[0].canon;

				MainXRRig.Instance.AlignSpace(
					Matrix4x4.TRS(s.position, s.rotation, Vector3.one),
					Matrix4x4.TRS(t.position, t.rotation, Vector3.one),
					lerp);
			}
			else
			{
				positionPairs.Clear();
				foreach (ColocationReference reference in references)
					positionPairs.Add((reference.observed.position, reference.canon.position));

				Matrix4x4 delta = BestFit.Find4DOF(positionPairs);
				MainXRRig.Instance.AlignSpace(spaceMat, delta * spaceMat, lerp);
			}

			// A degenerate fit (coincident references, NaN input) can throw tracking space
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
