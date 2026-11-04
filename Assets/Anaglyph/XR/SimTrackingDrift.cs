using System;
using UnityEngine;

namespace Anaglyph.XR
{
	/// <summary>
	/// Simulates headset tracking drift for the XR Interaction Simulator. The
	/// rig root's children (camera offset, hands, trackables) are moved under a
	/// drift frame, which is nudged about the head by a small error as the
	/// simulated head moves (and slowly over time), like a VIO tracker
	/// accumulating error.
	///
	/// The defaults follow Quest 3 captures (2026-10-28 to 11-03, eight sessions, about 3.5 km).
	/// Floor position and yaw wander without bound, growing with the square root of the walk. Height
	/// climbs steadily at a rate that stays fixed for the session: from about none to 2.8 mm/m.
	///
	/// The drift lives between the tracking space and the tracked poses, as it
	/// does on a headset: the head pose in the tracking space is wrong, while the
	/// rig root stays owned by colocation, which can shift the rig to compensate
	/// but never erases the drift itself. SimEnvironmentRigFollower anchors the
	/// simulated room to <see cref="Frame"/>, so passthrough and depth stay
	/// consistent with the head while the room slides against world content.
	/// </summary>
	[DefaultExecutionOrder(-100)]
	public class SimTrackingDrift : MonoBehaviour
	{
		/// <summary>The drifted tracking frame, or null when no drift is simulated.</summary>
		public static Transform Frame { get; private set; }

		[Header("Position")]
		[Tooltip("How fast position wanders along each floor axis, meters per √meter walked, past the correlation below")]
		[SerializeField] private float translationRate = 0.018f;
		[Tooltip("Likewise for height, on top of its steady climb")]
		[SerializeField] private float verticalRate = 0.006f;
		[Tooltip("Spread across sessions of the steady height climb, meters per meter walked (1σ)")]
		[SerializeField] private float heightClimbSpread = 0.0015f;
		[Tooltip("Meters of walk over which the error's direction changes: shorter stretches drift smoothly")]
		[SerializeField] private float translationCorrelation = 5f;

		[Header("Yaw")]
		[Tooltip("How fast yaw wanders, degrees per √meter walked")]
		[SerializeField] private float yawRate = 0.05f;

		[Header("Character")]
		[Tooltip("Equivalent walk per second, so drift continues slowly while still")]
		[SerializeField] private float stillMetersPerSecond = 0.05f;
		[Tooltip("Per-frame head jumps larger than this (meters) are treated as resets and cause no drift")]
		[SerializeField] private float jumpThreshold = 0.5f;

		[Header("Accumulated (read only)")]
		[SerializeField] private Vector3 driftPosition;
		[SerializeField] private float driftYaw;

		/// <summary>When set before play starts, every drift follows this seed instead of a random one: repeatable tests.</summary>
		public static int? SeedOverride;

		// private generator so Multiplayer Play Mode players drift differently
		private System.Random random = new(Guid.NewGuid().GetHashCode());
		private Vector3 velocity;
		private float heightClimb;
		private bool hasPrev;
		private Vector3 prevPos;
		private Transform frame;

		private void Awake()
		{
			if (SeedOverride.HasValue) random = new System.Random(SeedOverride.Value);
			heightClimb = Gaussian() * heightClimbSpread;
		}

		private void OnDisable() => hasPrev = false;

		private void OnDestroy()
		{
			if (!frame) return;

			// hand the rig back undrifted
			Transform space = frame.parent;
			if (space)
				while (frame.childCount > 0)
					frame.GetChild(0).SetParent(space, false);

			if (Frame == frame) Frame = null;
			Destroy(frame.gameObject);
		}

		/// <summary>Removes all accumulated drift.</summary>
		[ContextMenu("Reset Drift")]
		public void ResetDrift()
		{
			if (frame) frame.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
			driftPosition = Vector3.zero;
			driftYaw = 0f;
			velocity = Vector3.zero;
		}

		private void LateUpdate()
		{
			if (!MainXRRig.Instance) return;

			Transform space = MainXRRig.TrackingSpace;
			Camera camera = MainXRRig.Camera;
			if (!space || !camera) return;

			if (!frame || frame.parent != space)
				CreateFrame(space);

			// the head in the drift frame is the simulator's "true" pose,
			// which drifting the frame doesn't change
			Transform head = camera.transform;
			Vector3 pos = frame.InverseTransformPoint(head.position);

			if (!hasPrev)
			{
				prevPos = pos;
				hasPrev = true;
				return;
			}

			float dist = Vector3.Distance(pos, prevPos);
			prevPos = pos;

			if (dist > jumpThreshold) return;

			float walked = dist + stillMetersPerSecond * Time.deltaTime;
			if (walked <= 0) return;

			// the error's velocity: wandering, and forgetting where it was wandering to
			float correlation = Mathf.Max(translationCorrelation, 0.001f);
			float forgetting = walked / correlation;
			float noise = Mathf.Sqrt(walked) / correlation;
			velocity += -velocity * forgetting + noise * new Vector3(Gaussian() * translationRate, Gaussian() * verticalRate, Gaussian() * translationRate);
			Vector3 translation = (velocity + Vector3.up * heightClimb) * walked;

			float yaw = yawRate * Mathf.Sqrt(walked) * Gaussian();

			// everything below is in the tracking space, where the frame's local pose lives
			Quaternion delta = Quaternion.AngleAxis(yaw, Vector3.up);

			// turn about the head so the error doesn't swing it around the origin
			Vector3 pivot = space.InverseTransformPoint(head.position);
			frame.GetLocalPositionAndRotation(out Vector3 framePos, out Quaternion frameRot);
			framePos = pivot + delta * (framePos - pivot) + translation;
			frame.SetLocalPositionAndRotation(framePos, delta * frameRot);

			driftPosition = framePos;
			driftYaw += yaw;
		}

		private void CreateFrame(Transform space)
		{
			if (!frame)
			{
				frame = new GameObject("Simulated Tracking Drift").transform;
				Frame = frame;
			}

			frame.SetParent(space, false);
			frame.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

			// move the rig's tracked content (everything already under the root) into the drifted frame
			var children = new Transform[space.childCount];
			for (int i = 0; i < children.Length; i++)
				children[i] = space.GetChild(i);
			foreach (Transform child in children)
				if (child != frame) child.SetParent(frame, false);

			hasPrev = false;
		}

		private float Gaussian()
		{
			double u = 1.0 - random.NextDouble(), v = random.NextDouble();
			return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v));
		}
	}
}
