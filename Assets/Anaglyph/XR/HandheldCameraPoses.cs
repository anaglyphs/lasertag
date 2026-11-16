using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XR
{
	/// <summary>
	/// On ARKit and ARCore phones, feeds <see cref="HeadPoseHistory"/> each camera frame's pose in the
	/// sensor's own orientation, stamped with the frame's timestamp: the pose that lines up with that
	/// frame's CPU image and intrinsics, whichever way the screen is turned. Disables itself elsewhere.
	/// </summary>
	[DefaultExecutionOrder(-997)] // after HeadPoseHistory (-998)
	public sealed class HandheldCameraPoses : MonoBehaviour
	{
		[SerializeField] private ARCameraManager cameraManager;
		[SerializeField] private ARSession session;

		private HandheldAR.Provider runtime;
		private bool checkedRuntime;
		private bool loggedComparison;

		private void OnEnable()
		{
			if (cameraManager == null) cameraManager = FindAnyObjectByType<ARCameraManager>();
			if (session == null) session = FindAnyObjectByType<ARSession>();
			if (cameraManager != null) cameraManager.frameReceived += OnFrameReceived;
		}

		private void OnDisable()
		{
			if (cameraManager != null) cameraManager.frameReceived -= OnFrameReceived;
		}

		private void OnFrameReceived(ARCameraFrameEventArgs args)
		{
			XRCameraSubsystem camera = cameraManager.subsystem;
			if (camera == null || HeadPoseHistory.Instance == null || MainXRRig.Instance == null) return;

			if (!checkedRuntime)
			{
				checkedRuntime = true;
				runtime = HandheldAR.Runtime;
				if (runtime == HandheldAR.Provider.None)
				{
					enabled = false;
					return;
				}

				Debug.Log($"[{nameof(HandheldCameraPoses)}] camera poses from {runtime}, in sensor orientation");
			}

			Camera xrCamera = MainXRRig.Camera;
			XRCameraParams cameraParams = new()
			{
				zNear = xrCamera.nearClipPlane,
				zFar = xrCamera.farClipPlane,
				screenWidth = Screen.width,
				screenHeight = Screen.height,
				screenOrientation = Screen.orientation,
			};
			if (!camera.TryGetLatestFrame(cameraParams, out XRCameraFrame frame) || !frame.TryGetTimestamp(out long timestampNs)) return;

			IntPtr sessionPtr = session != null && session.subsystem != null ? session.subsystem.nativePtr : IntPtr.Zero;
			if (!HandheldSensorPose.TryGet(runtime, frame.nativePtr, sessionPtr, out Pose sensorPose, out Pose displayPose)) return;

			HeadPoseHistory.Instance.AddCameraFrameSample(timestampNs, sensorPose);
			if (!loggedComparison) LogComparison(sensorPose, displayPose, xrCamera.transform);
		}

		// once, to confirm on device that the sensor pose differs from the screen's only by a roll of
		// a quarter turn about the view axis, and that the conversion matches AR Foundation's camera
		private void LogComparison(Pose sensor, Pose display, Transform xrCamera)
		{
			loggedComparison = true;
			Quaternion roll = Quaternion.Inverse(display.rotation) * sensor.rotation;
			roll.ToAngleAxis(out float rollDegrees, out Vector3 rollAxis);
			Debug.Log($"[{nameof(HandheldCameraPoses)}] sensor vs screen: {rollDegrees:F1}° about {rollAxis:F2}, " +
			          $"{Vector3.Distance(sensor.position, display.position) * 1000:F1} mm apart; " +
			          $"screen pose vs AR Foundation camera: {Vector3.Distance(display.position, xrCamera.localPosition) * 1000:F1} mm, " +
			          $"{Quaternion.Angle(display.rotation, xrCamera.localRotation):F1}° (screen orientation {Screen.orientation})");
		}
	}

	/// <summary>The camera pose of an ARKit or ARCore frame, in the runtime's session space converted to Unity's axes.</summary>
	internal static class HandheldSensorPose
	{
		// AR Foundation's native pointers lead with a version number, then the runtime's own object
		[StructLayout(LayoutKind.Sequential)]
		private struct VersionedNativePointer
		{
			public int version;
			public IntPtr pointer;
		}

		private static IntPtr Unwrap(IntPtr versioned) =>
			versioned == IntPtr.Zero ? IntPtr.Zero : Marshal.PtrToStructure<VersionedNativePointer>(versioned).pointer;

		/// <summary>
		/// <paramref name="sensor"/>: the camera in the sensor's fixed orientation, which its CPU image
		/// and intrinsics share. <paramref name="display"/>: the same camera turned to the screen, as
		/// AR Foundation drives the Unity camera. Both in session space, Unity axes (z forward).
		/// </summary>
		public static bool TryGet(HandheldAR.Provider runtime, IntPtr frameNativePtr, IntPtr sessionNativePtr, out Pose sensor, out Pose display)
		{
			sensor = display = default;
			IntPtr frame = Unwrap(frameNativePtr);
			if (frame == IntPtr.Zero) return false;

			switch (runtime)
			{
#if UNITY_IOS && !UNITY_EDITOR
				case HandheldAR.Provider.ARKit:
				{
					float[] sensorMatrix = new float[16], displayMatrix = new float[16];
					if (!AnaglyphARKitCameraTransforms(frame, (int)Screen.orientation, sensorMatrix, displayMatrix)) return false;
					sensor = FromRightHanded(sensorMatrix);
					display = FromRightHanded(displayMatrix);
					return true;
				}
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
				case HandheldAR.Provider.ARCore:
				{
					IntPtr arSession = Unwrap(sessionNativePtr);
					if (arSession == IntPtr.Zero) return false;
					ArFrame_acquireCamera(arSession, frame, out IntPtr arCamera);
					if (arCamera == IntPtr.Zero) return false;
					try
					{
						ArCamera_getTrackingState(arSession, arCamera, out int trackingState);
						if (trackingState != ArTrackingStateTracking) return false;
						sensor = ArCorePose(arSession, arCamera, false);
						display = ArCorePose(arSession, arCamera, true);
						return true;
					}
					finally
					{
						ArCamera_release(arCamera);
					}
				}
#endif
				default:
					return false;
			}
		}

		// a right-handed, column-major pose matrix (y up, -z forward) in Unity's left-handed axes
		private static Pose FromRightHanded(float[] m)
		{
			Matrix4x4 rightHanded = new(
				new Vector4(m[0], m[1], m[2], m[3]),
				new Vector4(m[4], m[5], m[6], m[7]),
				new Vector4(m[8], m[9], m[10], m[11]),
				new Vector4(m[12], m[13], m[14], m[15]));
			Matrix4x4 flipZ = Matrix4x4.Scale(new Vector3(1, 1, -1));
			Matrix4x4 unity = flipZ * rightHanded * flipZ;
			return new Pose(unity.GetPosition(), unity.rotation);
		}

#if UNITY_IOS && !UNITY_EDITOR
		[DllImport("__Internal")]
		private static extern bool AnaglyphARKitCameraTransforms(IntPtr arFrame, int screenOrientation, float[] sensorColumnMajor, float[] displayColumnMajor);
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
		private const string ArCoreLibrary = "arcore_sdk_c";
		private const int ArTrackingStateTracking = 0;

		// raw ARCore poses are qx qy qz qw tx ty tz, right-handed, y up, -z forward
		private static Pose ArCorePose(IntPtr arSession, IntPtr arCamera, bool displayOriented)
		{
			ArPose_create(arSession, null, out IntPtr arPose);
			try
			{
				if (displayOriented) ArCamera_getDisplayOrientedPose(arSession, arCamera, arPose);
				else ArCamera_getPose(arSession, arCamera, arPose);
				float[] raw = new float[7];
				ArPose_getPoseRaw(arSession, arPose, raw);
				return new Pose(new Vector3(raw[4], raw[5], -raw[6]), new Quaternion(-raw[0], -raw[1], raw[2], raw[3]));
			}
			finally
			{
				ArPose_destroy(arPose);
			}
		}

		[DllImport(ArCoreLibrary)] private static extern void ArFrame_acquireCamera(IntPtr session, IntPtr frame, out IntPtr camera);
		[DllImport(ArCoreLibrary)] private static extern void ArCamera_release(IntPtr camera);
		[DllImport(ArCoreLibrary)] private static extern void ArCamera_getTrackingState(IntPtr session, IntPtr camera, out int trackingState);
		[DllImport(ArCoreLibrary)] private static extern void ArCamera_getPose(IntPtr session, IntPtr camera, IntPtr pose);
		[DllImport(ArCoreLibrary)] private static extern void ArCamera_getDisplayOrientedPose(IntPtr session, IntPtr camera, IntPtr pose);
		[DllImport(ArCoreLibrary)] private static extern void ArPose_create(IntPtr session, float[] poseRaw, out IntPtr pose);
		[DllImport(ArCoreLibrary)] private static extern void ArPose_getPoseRaw(IntPtr session, IntPtr pose, float[] poseRaw);
		[DllImport(ArCoreLibrary)] private static extern void ArPose_destroy(IntPtr pose);
#endif
	}
}
