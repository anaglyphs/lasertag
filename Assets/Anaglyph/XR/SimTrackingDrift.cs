using System;
using UnityEngine;

namespace Anaglyph.XR
{
	/// <summary>
	/// Simulates headset tracking drift for the XR Interaction Simulator. The
	/// rig root's children (camera offset, hands, trackables) are moved under a
	/// drift frame, which is nudged about the head by a small error as the
	/// simulated head moves and turns (and slowly over time), like a VIO tracker
	/// accumulating error. The error's direction wanders slowly and randomly, so
	/// drift builds up coherently rather than averaging out.
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

		[Header("Translation (meters)")]
		[SerializeField] private float translationPerMeter = 0.02f;
		[SerializeField] private float translationPerDegree = 0.0002f;
		[SerializeField] private float translationPerSecond = 0.001f;
		[Tooltip("Scales vertical drift relative to horizontal")]
		[SerializeField] private float verticalScale = 0.3f;

		[Header("Rotation (degrees)")]
		[SerializeField] private float yawPerMeter = 0.5f;
		[SerializeField] private float yawPerDegree = 0.005f;
		[SerializeField] private float yawPerSecond = 0.02f;
		[Tooltip("Pitch and roll drift relative to yaw. Real trackers observe gravity, so this is usually 0")]
		[SerializeField] private float tiltScale = 0f;

		[Header("Character")]
		[Tooltip("Head motion over which the drift direction decorrelates, in meters (plus degrees turned / 90)")]
		[SerializeField] private float wander = 3f;
		[Tooltip("Equivalent head motion per second, so the drift direction also wanders while still")]
		[SerializeField] private float wanderPerSecond = 0.05f;
		[Tooltip("Per-frame head jumps larger than this (meters) are treated as resets and cause no drift")]
		[SerializeField] private float jumpThreshold = 0.5f;

		[Header("Accumulated (read only)")]
		[SerializeField] private Vector3 driftPosition;
		[SerializeField] private float driftYaw;

		/// <summary>When set before play starts, every drift follows this seed instead of a random one: repeatable tests.</summary>
		public static int? SeedOverride;

		// private generator so Multiplayer Play Mode players drift differently
		private System.Random random = new(Guid.NewGuid().GetHashCode());
		private readonly float[] seeds = new float[6];
		private float motion;
		private bool hasPrev;
		private Vector3 prevPos;
		private Quaternion prevRot;
		private Transform frame;

		private void Awake()
		{
			if (SeedOverride.HasValue) random = new System.Random(SeedOverride.Value);
			for (int i = 0; i < seeds.Length; i++)
				seeds[i] = (float)random.NextDouble() * 1000f;
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
			Quaternion rot = Quaternion.Inverse(frame.rotation) * head.rotation;

			if (!hasPrev)
			{
				Remember(pos, rot);
				return;
			}

			float dist = Vector3.Distance(pos, prevPos);
			float angle = Quaternion.Angle(rot, prevRot);
			Remember(pos, rot);

			if (dist > jumpThreshold) return;

			float dt = Time.deltaTime;
			motion += dist + angle / 90f + wanderPerSecond * dt;
			float t = motion / Mathf.Max(wander, 0.001f);

			Vector3 translation = new Vector3(Noise(0, t), Noise(1, t) * verticalScale, Noise(2, t))
				* (translationPerMeter * dist + translationPerDegree * angle + translationPerSecond * dt);
			float yaw = Noise(3, t) * (yawPerMeter * dist + yawPerDegree * angle + yawPerSecond * dt);
			float pitch = Noise(4, t) * tiltScale * Mathf.Abs(yaw);
			float roll = Noise(5, t) * tiltScale * Mathf.Abs(yaw);

			// everything below is in the tracking space, where the frame's local pose lives
			Quaternion delta = Quaternion.AngleAxis(yaw, Vector3.up)
				* Quaternion.AngleAxis(pitch, Vector3.right)
				* Quaternion.AngleAxis(roll, Vector3.forward);

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

		private void Remember(Vector3 pos, Quaternion rot)
		{
			prevPos = pos;
			prevRot = rot;
			hasPrev = true;
		}

		// smooth noise in [-1, 1]
		private float Noise(int channel, float t) =>
			Mathf.PerlinNoise(seeds[channel], t) * 2f - 1f;
	}
}
