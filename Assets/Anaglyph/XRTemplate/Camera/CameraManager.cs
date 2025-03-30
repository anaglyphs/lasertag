using System;
using UnityEngine;

namespace Anaglyph.XRTemplate.CameraReader
{
	[DefaultExecutionOrder(-1000)]
	public class CameraManager : MonoBehaviour
	{
		public static CameraManager Instance { get; private set; }

		private Texture2D camTex;
		public Texture2D CamTex => camTex;

		private Pose camPoseOnDevice;
		public Pose CamPoseOnDevice => camPoseOnDevice;

		private Intrinsics camIntrinsics;
		public Intrinsics CamIntrinsics => camIntrinsics;

		public static event Action<Texture2D> OnNewFrame = delegate { };
		public static event Action OnCaptureStart = delegate { };
		public static event Action OnCaptureStop = delegate { };

		private unsafe sbyte* buffer;
		private int bufferSize;

		public unsafe sbyte* Buffer => buffer;
		public int BufferSize => bufferSize;

		public bool IsCapturing { get; private set; }

		/// In nanoseconds!
		public long TimestampNanoseconds { get; private set; }

		private class AndroidInterface
		{
			private AndroidJavaClass androidClass;
			private AndroidJavaObject androidInstance;

			private void Call(string method)
			{
#if !UNITY_EDITOR
				androidInstance.Call(method);
#endif
			}

			private T Call<T>(string method)
			{
#if UNITY_EDITOR
				return default;		
#endif
				return androidInstance.Call<T>(method);
			}

			public AndroidInterface(GameObject messageReceiver)
			{
#if UNITY_EDITOR
				return;
#endif
				androidClass = new AndroidJavaClass("com.trev3d.Camera.CameraReader");
				androidInstance = androidClass.CallStatic<AndroidJavaObject>("getInstance");
				androidInstance.Call("setup", messageReceiver.name);
			}

			public void Configure(int index, int width, int height)
			{
				androidInstance.Call("configure", index, width, height);
			}

			public void StartCapture() => Call("startCapture");

			public void StopCapture() => Call("stopCapture");

			public unsafe sbyte* GetByteBuffer()
			{
#if UNITY_EDITOR
				return null;
#endif
				AndroidJavaObject byteBuffer = Call<AndroidJavaObject>("getByteBuffer");
				return AndroidJNI.GetDirectBufferAddress(byteBuffer.GetRawObject());
			}

			public long GetTimestamp()
			{
#if UNITY_EDITOR
				return 0;
#endif
				return androidInstance.Call<long>("getTimestamp");
			}

			public float[] GetCamPoseOnDevice()
			{
				return androidInstance.Call<float[]>("getCamPoseOnDevice");
			}

			public float[] GetCamIntrinsics()
			{
				return androidInstance.Call<float[]>("getCamIntrinsics");
			}
		}

		private AndroidInterface androidInterface;

		private void Awake()
		{
			Instance = this;
			androidInterface = new AndroidInterface(gameObject);
		}

		private void Start()
		{
			Configure(0, 320, 240);
		}

		private void OnDestroy()
		{
			OnNewFrame = delegate { };
			OnCaptureStart = delegate { };
			OnCaptureStop = delegate { };
		}

		public void Configure(int index, int width, int height)
		{
			androidInterface.Configure(index, width, height);
			camTex = new Texture2D(width, height, TextureFormat.R8, 1, false);
			bufferSize = width * height;// * 4;

			float[] vals;
			vals = androidInterface.GetCamPoseOnDevice();
			Vector3 pos = new(vals[0], vals[1], -vals[2]);
			Quaternion rot = Quaternion.Inverse(new(-vals[3], -vals[4], vals[5], vals[6])) * Quaternion.Euler(180, 0, 0);
			camPoseOnDevice = new Pose(pos, rot);

			vals = androidInterface.GetCamIntrinsics();

			camIntrinsics = new Intrinsics
			{
				FocalLength = new Vector2(vals[0], vals[1]),
				PrincipalPoint = new Vector2(vals[2], vals[3]),
				Resolution = new Vector2Int((int)vals[5], (int)vals[6]),
				Skew = vals[4]
			};
		}

		public void StartCapture()
		{
			if (IsCapturing)
				return;

			IsCapturing = true;

			androidInterface.StartCapture();
		}

		public void StopCapture() => androidInterface.StopCapture();

		// Messages sent from Android

#pragma warning disable IDE0051 // Remove unused private members
		private unsafe void OnCaptureStarted()
		{
			OnCaptureStart.Invoke();
		}

		private void OnPermissionDenied()
		{
			//onPermissionDenied.Invoke();
		}

		private unsafe void OnNewFrameAvailable()
		{
			buffer = androidInterface.GetByteBuffer();
			if (buffer == default) return;

			camTex.LoadRawTextureData((IntPtr)buffer, bufferSize);
			camTex.Apply();
			TimestampNanoseconds = androidInterface.GetTimestamp();

			OnNewFrame.Invoke(camTex);
		}

		public struct Intrinsics
		{
			public Vector2 FocalLength;
			public Vector2 PrincipalPoint;
			public Vector2Int Resolution;
			public float Skew;
		}

		private void OnCaptureStopped()
		{
			OnCaptureStop.Invoke();

			IsCapturing = false;
		}
#pragma warning restore IDE0051 // Remove unused private members
	}
}