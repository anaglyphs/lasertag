using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XR.AprilTags
{
	/// <summary>
	/// Passthrough frames straight from Android's Camera2 API, for headsets whose AR Foundation
	/// provider hands out no camera image. <see cref="AndroidCamExtrinsicsHelper"/> picks the camera
	/// and places it on the head. Frames are luma only, wrapped as <see cref="XRCpuImage"/>s that
	/// read Java's buffers in place. Main thread only.
	/// </summary>
	public static class Camera2PassthroughCamera
	{
		private const int MaxWidth = 1280;
		private const float RetryIntervalSeconds = 3;
		private const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";
		private const string AndroidCameraPermission = "android.permission.CAMERA";

		private static AndroidJavaObject source;
		private static AndroidJavaObject[] buffers;
		private static IntPtr[] bufferAddresses;
		private static Vector2Int size;
		private static XRCameraIntrinsics intrinsics;
		private static string cameraId;
		private static Pose lensInHead;
		private static bool noUsableCamera;
		private static float nextStartTime;
		private static readonly FrameApi api = new();

		public static bool CanRun =>
			Application.platform == RuntimePlatform.Android && !HandheldAR.IsActive && !noUsableCamera;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			noUsableCamera = false;
			nextStartTime = 0;
			Application.quitting -= Stop;
			Application.quitting += Stop;
		}

		/// <summary>The newest frame, opening the camera first if needed (which takes a few frames).</summary>
		public static bool TryAcquireFrame(out PassthroughFrame frame)
		{
			frame = default;
			if (!CanRun || !EnsureStreaming()) return false;

			int index = source.Call<int>("acquireLatest");
			if (index < 0) return false;

			long timestampNs = source.Call<long>("getTimestampNs", index);
			XRCpuImage.Cinfo cinfo = new(api.Hold(index), size, 1, timestampNs * 1e-9, XRCpuImage.Format.OneComponent8);

			frame.image = new XRCpuImage(api, cinfo);
			frame.intrinsics = intrinsics;
			frame.timestampNs = timestampNs;
			frame.lensPosition = LensPosition.Unknown;
			frame.lensInHead = lensInHead;
			return true;
		}

		/// <summary>Closes the camera. Images already handed out stay valid until disposed.</summary>
		public static void Stop()
		{
			source?.Call("stop");
			nextStartTime = 0;
		}

		private static bool EnsureStreaming()
		{
			if (source != null && source.Call<bool>("isActive"))
				return source.Call<bool>("isRunning");

			if (Time.unscaledTime < nextStartTime || !HasCameraPermission()) return false;
			nextStartTime = Time.unscaledTime + RetryIntervalSeconds;

			if (cameraId == null &&
			    !AndroidCamExtrinsicsHelper.TryFindCameraExtrinsics(CameraFacingDirection.World, Vector2Int.zero,
				    LensPosition.Unknown, out lensInHead, out cameraId))
			{
				noUsableCamera = true;
				Debug.LogError($"[{nameof(Camera2PassthroughCamera)}] no camera publishes a lens pose: no Camera2 frames");
				return false;
			}

			source ??= new AndroidJavaObject("com.anaglyph.camera2.Camera2FrameSource");
			using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
			using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
			if (!source.Call<bool>("start", activity, cameraId, MaxWidth)) return false;

			ReadStream();
			Debug.Log($"[{nameof(Camera2PassthroughCamera)}] opening {source.Call<string>("getDescription")}");
			return false;
		}

		private static void ReadStream()
		{
			size = new Vector2Int(source.Call<int>("getWidth"), source.Call<int>("getHeight"));

			float[] k = source.Call<float[]>("getIntrinsics");
			intrinsics = k == null
				? default
				: new XRCameraIntrinsics(new Vector2(k[0], k[1]), new Vector2(k[2], k[3]), size);
			if (k == null)
				Debug.LogError($"[{nameof(Camera2PassthroughCamera)}] camera {cameraId} publishes no intrinsic calibration");

			if (buffers != null)
				foreach (AndroidJavaObject buffer in buffers)
					buffer.Dispose();

			int count = source.Call<int>("getBufferCount");
			buffers = new AndroidJavaObject[count];
			bufferAddresses = new IntPtr[count];
			for (int i = 0; i < count; i++)
			{
				buffers[i] = source.Call<AndroidJavaObject>("getBuffer", i);
				bufferAddresses[i] = DirectBufferAddress(buffers[i]);
			}
		}

		private static unsafe IntPtr DirectBufferAddress(AndroidJavaObject buffer) =>
			(IntPtr)AndroidJNI.GetDirectBufferAddress(buffer.GetRawObject());

		private static bool HasCameraPermission() =>
			Permission.HasUserAuthorizedPermission(HeadsetCameraPermission) ||
			Permission.HasUserAuthorizedPermission(AndroidCameraPermission);

		private sealed class FrameApi : XRCpuImage.Api
		{
			private readonly Dictionary<int, int> bufferByHandle = new();
			private int nextHandle = 1;

			public int Hold(int bufferIndex)
			{
				int handle = nextHandle++;
				bufferByHandle[handle] = bufferIndex;
				return handle;
			}

			public override bool NativeHandleValid(int nativeHandle) => bufferByHandle.ContainsKey(nativeHandle);

			public override bool TryGetPlane(int nativeHandle, int planeIndex, out XRCpuImage.Plane.Cinfo planeCinfo)
			{
				planeCinfo = default;
				if (planeIndex != 0 || !bufferByHandle.TryGetValue(nativeHandle, out int index)) return false;
				planeCinfo = new XRCpuImage.Plane.Cinfo(bufferAddresses[index], size.x * size.y, size.x, 1);
				return true;
			}

			public override void DisposeImage(int nativeHandle)
			{
				if (bufferByHandle.Remove(nativeHandle, out int index))
					source?.Call("release", index);
			}
		}
	}
}
