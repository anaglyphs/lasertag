using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XR.AprilTags
{
	/// <summary>A passthrough camera image with what's needed to place it in the world.</summary>
	public struct PassthroughFrame
	{
		public XRCpuImage image;
		public XRCameraIntrinsics intrinsics;

		/// <summary>CLOCK_MONOTONIC nanoseconds, the clock <see cref="HeadPoseHistory"/> keys on.</summary>
		public long timestampNs;

		public LensPosition lensPosition;

		/// <summary>The lens relative to the head, when the source knows its camera; otherwise look it up.</summary>
		public Pose? lensInHead;
	}

	/// <summary>
	/// Picks the passthrough camera source: Meta's camera API where its subsystem runs, Android's
	/// Camera2 on other headsets. False from <see cref="TryAcquire"/> leaves the caller to AR
	/// Foundation's generic image, as on phones.
	/// </summary>
	public static class PassthroughFrames
	{
		private static bool forceCamera2;

		/// <summary>Development builds: Camera2 even where Meta's camera API runs, to test that path on Quest.</summary>
		public static bool ForceCamera2
		{
			get => forceCamera2;
			set
			{
				forceCamera2 = value && Debug.isDebugBuild;
				if (!forceCamera2) Camera2PassthroughCamera.Stop();
			}
		}

		public static bool UsesCamera2(ARCameraManager cameraManager) =>
			Camera2PassthroughCamera.CanRun && (ForceCamera2 || !MetaPassthroughCamera.IsActive(cameraManager));

		/// <summary>No generic AR Foundation image either: Camera2 is being tested in its place.</summary>
		public static bool GenericImageAllowed => !ForceCamera2;

		public static bool TryAcquire(ARCameraManager cameraManager, out PassthroughFrame frame)
		{
			if (UsesCamera2(cameraManager)) return Camera2PassthroughCamera.TryAcquireFrame(out frame);
			return MetaPassthroughCamera.TryAcquireFrame(cameraManager, out frame);
		}
	}
}
