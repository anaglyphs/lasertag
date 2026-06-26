using System.Runtime.InteropServices;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	/// <summary>
	/// Records the head pose every render frame so it can be looked up by
	/// timestamp afterwards. This is the ARFoundation-friendly replacement for
	/// OVRPlugin.GetNodePoseStateAtTime: sample densely in onBeforeRender, then
	/// interpolate between the two samples bracketing the requested time.
	///
	/// Poses are stored relative to <see cref="MainXRRig.TrackingSpace"/> (the
	/// same space OVR head poses were expressed in), so they stay valid across
	/// rig realignment (e.g. drift correction's AlignSpace).
	///
	/// Timestamps are CLOCK_MONOTONIC nanoseconds, which on the Quest OpenXR
	/// runtime is the same time base as XrTime — i.e. the same domain as
	/// ARCameraFrameEventArgs.timestampNs / AROcclusionFrameEventArgs timestamps.
	/// No clock conversion needed; pass timestampNs straight in.
	/// </summary>
	[DefaultExecutionOrder(-998)] // after MainXRRig (-999) sets its Instance
	public class HeadPoseHistory : MonoBehaviour
	{
		public static HeadPoseHistory Instance { get; private set; }

		private struct Sample
		{
			public long tNs;
			public Vector3 pos;
			public Quaternion rot;
		}

		// 256 samples ≈ 2.8s of history at 90Hz, ~2.1s at 120Hz.
		private readonly Sample[] ring = new Sample[256];
		private int count;

		private void Awake() => Instance = this;
		private void OnEnable() => Application.onBeforeRender += Capture;
		private void OnDisable() => Application.onBeforeRender -= Capture;

		private void Capture()
		{
			if (MainXRRig.Instance == null) return;

			Transform ts = MainXRRig.TrackingSpace;
			Transform cam = MainXRRig.Camera.transform;

			// camera pose expressed in tracking space
			Matrix4x4 local = ts.worldToLocalMatrix * cam.localToWorldMatrix;

			ring[count++ % ring.Length] = new Sample
			{
				tNs = MonotonicNs(),
				pos = local.GetPosition(),
				rot = local.rotation,
			};
		}

		/// <summary>
		/// Head pose in tracking-space-local coordinates at the given OpenXR
		/// timestamp (e.g. ARCameraFrameEventArgs.timestampNs). Returns false if
		/// the timestamp falls outside the buffered window.
		/// </summary>
		public bool TryGetLocalPose(long timestampNs, out Pose pose)
		{
			pose = default;
			if (!TryGetBracket(timestampNs, out Sample a, out Sample b))
				return false;

			float f = (b.tNs == a.tNs) ? 0f
				: (float)((double)(timestampNs - a.tNs) / (b.tNs - a.tNs));
			pose = new Pose(
				Vector3.Lerp(a.pos, b.pos, f),
				Quaternion.Slerp(a.rot, b.rot, f));
			return true;
		}

		/// <summary>
		/// Estimates head velocity around the given timestamp by finite-differencing
		/// the two bracketing samples. <paramref name="linear"/> is in m/s,
		/// <paramref name="angular"/> is in rad/s (axis * angular speed). Both are in
		/// tracking space, matching OVRPlugin's head Velocity / AngularVelocity.
		/// Returns false if the timestamp is outside the buffered window.
		/// </summary>
		public bool TryGetVelocity(long timestampNs, out Vector3 linear, out Vector3 angular)
		{
			linear = default;
			angular = default;
			if (!TryGetBracket(timestampNs, out Sample a, out Sample b))
				return false;

			float dt = (float)((b.tNs - a.tNs) * 1e-9);
			if (dt <= 0f) return false;

			linear = (b.pos - a.pos) / dt;

			// rotation delta a -> b, taken as the shortest arc
			Quaternion dq = b.rot * Quaternion.Inverse(a.rot);
			if (dq.w < 0f) dq = new Quaternion(-dq.x, -dq.y, -dq.z, -dq.w);
			dq.ToAngleAxis(out float angleDeg, out Vector3 axis);
			if (float.IsInfinity(axis.x) || axis.sqrMagnitude < 1e-9f)
				angular = Vector3.zero;
			else
				angular = axis.normalized * (angleDeg * Mathf.Deg2Rad / dt);

			return true;
		}

		// Finds the two samples (older `a`, newer `b`) whose timestamps bracket the
		// requested time. Walks newest -> oldest.
		private bool TryGetBracket(long timestampNs, out Sample a, out Sample b)
		{
			a = default;
			b = default;
			int n = Mathf.Min(count, ring.Length);
			if (n < 2) return false;

			for (int i = 0; i < n - 1; i++)
			{
				Sample newer = ring[(count - 1 - i) % ring.Length];
				Sample older = ring[(count - 2 - i) % ring.Length];

				if (older.tNs <= timestampNs && timestampNs <= newer.tNs)
				{
					a = older;
					b = newer;
					return true;
				}
			}

			return false;
		}

		/// <summary>Same as <see cref="TryGetLocalPose"/> but in world space.</summary>
		public bool TryGetWorldPose(long timestampNs, out Pose worldPose)
		{
			worldPose = default;
			if (!TryGetLocalPose(timestampNs, out Pose local)) return false;

			Transform ts = MainXRRig.TrackingSpace;
			worldPose = new Pose(
				ts.TransformPoint(local.position),
				ts.rotation * local.rotation);
			return true;
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		// On Quest, OpenXR XrTime == CLOCK_MONOTONIC, so reading it directly puts
		// our samples in the exact same domain as AR frame timestamps.
		[DllImport("c", EntryPoint = "clock_gettime")]
		private static extern int clock_gettime(int clkId, out Timespec tp);

		[StructLayout(LayoutKind.Sequential)]
		private struct Timespec
		{
			public long tvSec;  // time_t (8 bytes on arm64)
			public long tvNsec; // long  (8 bytes on arm64)
		}

		private const int CLOCK_MONOTONIC = 1;

		private static long MonotonicNs()
		{
			clock_gettime(CLOCK_MONOTONIC, out Timespec ts);
			return ts.tvSec * 1_000_000_000L + ts.tvNsec;
		}
#else
		// Editor / non-Android fallback. AR frame timestamps in the simulator
		// won't line up with this, but AprilTag detection isn't functional there
		// anyway (BGRA path throws), so it doesn't matter in practice.
		private static long MonotonicNs() =>
			(long)(Time.realtimeSinceStartupAsDouble * 1e9);
#endif
	}
}
