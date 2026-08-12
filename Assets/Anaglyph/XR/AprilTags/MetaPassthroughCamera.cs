using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if META_OPENXR_2_6_OR_NEWER
using UnityEngine.XR.OpenXR.Features.Meta;
#endif

namespace Anaglyph.XR.AprilTags
{
	/// <summary>
	/// Per-eye passthrough camera access, added in com.unity.xr.meta-openxr 2.6.
	/// The generic AR Foundation camera API only hands out one "world facing"
	/// image without saying which lens produced it; this lets us name the lens,
	/// so its extrinsics can be looked up instead of guessed at.
	/// Everything here no-ops when the package is older than 2.6.
	/// </summary>
	public static class MetaPassthroughCamera
	{
		public struct Frame
		{
			public XRCpuImage image;
			public XRCameraIntrinsics intrinsics;
			public long timestampNs;
			public LensPosition lensPosition;
		}

		public static bool IsSupported =>
#if META_OPENXR_2_6_OR_NEWER
			true;
#else
			false;
#endif

		/// <summary>
		/// Acquires a CPU image from a known physical camera, along with that
		/// camera's own intrinsics and frame timestamp.
		/// </summary>
		/// <returns>False if the per-eye API isn't available, in which case the
		/// caller should fall back to <see cref="ARCameraManager"/>.</returns>
		public static bool TryAcquireFrame(ARCameraManager cameraManager, out Frame frame)
		{
			frame = default;

#if META_OPENXR_2_6_OR_NEWER
			if (cameraManager == null || cameraManager.subsystem is not MetaOpenXRCameraSubsystem meta)
				return false;

			MetaOpenXRCameraSubsystem.AvailableCameras available = meta.GetAvailableCameras();

			MetaOpenXRCameraSubsystem.CameraPosition position;

			// The left eye camera is what the mono world-facing image uses, so
			// prefer it to keep behaviour identical across both code paths.
			if ((available & MetaOpenXRCameraSubsystem.AvailableCameras.LeftEye) != 0)
			{
				position = MetaOpenXRCameraSubsystem.CameraPosition.LeftEye;
				frame.lensPosition = LensPosition.Left;
			}
			else if ((available & MetaOpenXRCameraSubsystem.AvailableCameras.RightEye) != 0)
			{
				position = MetaOpenXRCameraSubsystem.CameraPosition.RightEye;
				frame.lensPosition = LensPosition.Right;
			}
			else
			{
				return false;
			}

			if (!meta.TryGetIntrinsicsForPosition(position, out frame.intrinsics))
				return false;

			if (!meta.TryAcquireLatestCpuImageForPosition(position, out XRCpuImage.Cinfo cinfo))
				return false;

			frame.image = new XRCpuImage(meta.cpuImageApi, cinfo);

			if (meta.TryGetFrameForPosition(position, GetCameraParams(), out XRCameraFrame cameraFrame) &&
			    cameraFrame.TryGetTimestamp(out long timestampNs))
			{
				frame.timestampNs = timestampNs;
			}
			else
			{
				// Cinfo carries the same capture time in seconds; double holds
				// nanosecond precision at XrTime's magnitude.
				frame.timestampNs = (long)(cinfo.timestamp * 1_000_000_000d);
			}

			return true;
#else
			return false;
#endif
		}

#if META_OPENXR_2_6_OR_NEWER
		private static XRCameraParams GetCameraParams()
		{
			Camera camera = MainXRRig.Camera;

			return new XRCameraParams
			{
				zNear = camera == null ? 0.1f : camera.nearClipPlane,
				zFar = camera == null ? 1000f : camera.farClipPlane,
				screenWidth = Screen.width,
				screenHeight = Screen.height,
				screenOrientation = Screen.orientation,
			};
		}
#endif
	}
}
