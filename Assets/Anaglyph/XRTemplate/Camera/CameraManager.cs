using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

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

		public static event Action DeviceOpened = delegate { };
		public static event Action DeviceClosed = delegate { };
		public static event Action DeviceDisconnected = delegate { };
		public static event Action DeviceError = delegate { };
		public static event Action Configured = delegate { };
		public static event Action ConfigureFailed = delegate { };
		public static event Action<Texture2D> ImageAvailable = delegate { };

		private unsafe sbyte* buffer;
		private int bufferSize;

		public unsafe sbyte* Buffer => buffer;
		public int BufferSize => bufferSize;

		public bool ShouldDeviceBeOpen { get; private set; }
		public bool IsDeviceOpened { get; private set; }
		public bool IsConfigured { get; private set; }

		public const int ERROR_CAMERA_DEVICE = 0x00000004;
		public const int ERROR_CAMERA_DISABLED = 0x00000003;
		public const int ERROR_CAMERA_IN_USE = 0x00000001;
		public const int ERROR_CAMERA_SERVICE = 0x00000005;
		public const int ERROR_MAX_CAMERAS_IN_USE = 0x00000002;

		/// In nanoseconds!
		public long TimestampNanoseconds { get; private set; }

		
		public class PermissionException : Exception
		{
			public PermissionException(string message) : base(message)
			{
			}
		}
		public class ConfiguredException : Exception
		{
			public ConfiguredException(string message) : base(message)
			{
			}
		}

		private class AndroidInterface
		{
			private AndroidJavaClass androidClass;
			private AndroidJavaObject androidInstance;

			private void Call(string method)
			{
				androidInstance.Call(method);
			}

			private T Call<T>(string method)
			{
				return androidInstance.Call<T>(method);
			}

			public AndroidInterface(GameObject messageReceiver)
			{
				androidClass = new AndroidJavaClass("com.trev3d.Camera.CameraReader");
				androidInstance = androidClass.CallStatic<AndroidJavaObject>("getInstance");
				androidInstance.Call("setup", messageReceiver.name);
			}

			public void Configure(int index, int width, int height)
			{
				androidInstance.Call("configure", index, width, height);
			}

			public void OpenCamera() => Call("open");

			public void CloseCamera() => Call("close");

			public unsafe sbyte* GetByteBuffer()
			{
				AndroidJavaObject byteBuffer = Call<AndroidJavaObject>("getByteBuffer");
				return AndroidJNI.GetDirectBufferAddress(byteBuffer.GetRawObject());
			}

			public long GetTimestamp()
			{
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
#if !UNITY_EDITOR
			androidInterface = new AndroidInterface(gameObject);
#endif
		}

		private const string MetaCameraPermission = "horizonos.permission.HEADSET_CAMERA";

		private async Task<bool> PermissionCheck()
		{
			if (!Permission.HasUserAuthorizedPermission(MetaCameraPermission))
			{
				Permission.RequestUserPermission(MetaCameraPermission);

				await Awaitable.WaitForSecondsAsync(0.2f);

				while (!Application.isFocused)
					await Awaitable.NextFrameAsync();

				if (!Permission.HasUserAuthorizedPermission(MetaCameraPermission))
					return false;
			}

			if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
			{
				Permission.RequestUserPermission(Permission.Camera);

				await Awaitable.WaitForSecondsAsync(0.2f);

				while (!Application.isFocused)
					await Awaitable.NextFrameAsync();

				if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
					return false;
			}

			return true;
		}

		private void OnDestroy()
		{
			ImageAvailable = delegate { };
		}

		public async Task Configure(int index, int width, int height)
		{
#if UNITY_EDITOR
			IsConfigured = true;
			return;
#endif

			if (!await PermissionCheck())
				throw new Exception("Camera does not have permission!");

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

			IsConfigured = true;
		}

		public async Task TryOpenCamera()
		{
			if (!await PermissionCheck())
				throw new PermissionException("Camera does not have permission!");

			if (!IsConfigured)
				throw new ConfiguredException("CameraManager is not yet configured! You must first call " + nameof(Configure));

#if !UNITY_EDITOR
			androidInterface.OpenCamera();
#endif

			ShouldDeviceBeOpen = true;
		}

		public void CloseCamera()
		{
			if (!ShouldDeviceBeOpen)
				return;

			ShouldDeviceBeOpen = false;

#if !UNITY_EDITOR
			androidInterface.CloseCamera();
#endif
		}

		// Messages sent from Android

#pragma warning disable IDE0051 // Remove unused private members

		private void OnDeviceOpened()
		{
			IsDeviceOpened = true;
			DeviceOpened.Invoke();
		}

		private async void OnDeviceClosed()
		{
			IsDeviceOpened = false;
			DeviceClosed.Invoke();

			if (ShouldDeviceBeOpen)
				await TryOpenCamera();
		}

		private void OnDeviceDisconnected()
		{
			IsDeviceOpened = false;
			DeviceDisconnected.Invoke();
		}

		private void OnDeviceError(string errorCodeAsString)
		{
			IsDeviceOpened = false;
			int.TryParse(errorCodeAsString, out int error);
			DeviceError.Invoke();
		}

		private void OnConfigured()
		{
			IsConfigured = true;
			Configured.Invoke();
		}

		private void OnConfigureFailed()
		{
			IsConfigured = false;
			ConfigureFailed.Invoke();
		}

		private unsafe void OnImageAvailable()
		{
			buffer = androidInterface.GetByteBuffer();
			if (buffer == default) return;

			camTex.LoadRawTextureData((IntPtr)buffer, bufferSize);
			camTex.Apply();
			TimestampNanoseconds = androidInterface.GetTimestamp();

			ImageAvailable.Invoke(camTex);
		}

		public struct Intrinsics
		{
			public Vector2 FocalLength;
			public Vector2 PrincipalPoint;
			public Vector2Int Resolution;
			public float Skew;
		}

#pragma warning restore IDE0051 // Remove unused private members
	}
}