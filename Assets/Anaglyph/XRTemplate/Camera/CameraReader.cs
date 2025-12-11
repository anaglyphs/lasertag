using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Android;

namespace Anaglyph.XRTemplate.DeviceCameras
{
	[DefaultExecutionOrder(-1000)]
	public class CameraReader : MonoBehaviour
	{
		private Texture2D texture;
		public Texture2D Texture => texture;

		private Pose hardwarePose;
		public Pose HardwarePose => hardwarePose;

		private HardwareIntrinsics intrinsics;
		public HardwareIntrinsics Intrinsics => intrinsics;

		[SerializeField] private Vector2Int defaultTextureSize = new(1280, 960);
		[SerializeField] private int camID = 1;
		public int CamID => camID;

		public event Action DeviceOpened = delegate { };
		public event Action DeviceClosed = delegate { };
		public event Action DeviceDisconnected = delegate { };
		public event Action<ErrorCode> DeviceError = delegate { };
		public event Action Configured = delegate { };
		public event Action ConfigureFailed = delegate { };
		public event Action<Texture2D> ImageAvailable = delegate { };

		private unsafe sbyte* buffer;
		private int bufferSize;

		public unsafe sbyte* Buffer => buffer;
		public int BufferSize => bufferSize;

		public bool DeviceShouldBeOpen { get; private set; }
		public bool DeviceIsOpen { get; private set; }
		public bool IsConfigured { get; private set; }

		public enum ErrorCode
		{
			ERROR_UNKNOWN = default,
			ERROR_CAMERA_DEVICE = 0x00000004,
			ERROR_CAMERA_DISABLED = 0x00000003,
			ERROR_CAMERA_IN_USE = 0x00000001,
			ERROR_CAMERA_SERVICE = 0x00000005,
			ERROR_MAX_CAMERAS_IN_USE = 0x00000002
		}

		/// In nanoseconds!
		public long TimestampNs { get; private set; }

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
			private AndroidJavaClass jniClass;
			private AndroidJavaObject jniObj;

			private void Call(string method)
			{
				jniObj.Call(method);
			}

			private T Call<T>(string method)
			{
				return jniObj.Call<T>(method);
			}

			public AndroidInterface(GameObject messageReceiver)
			{
				jniClass = new AndroidJavaClass("com.trev3d.Camera.CameraReader");
				jniObj = jniClass.CallStatic<AndroidJavaObject>("create", messageReceiver.name);
			}

			public void Configure(int index, int width, int height)
			{
				jniObj.Call("configure", index, width, height);
			}

			public void OpenCamera()
			{
				Call("open");
			}

			public void CloseCamera()
			{
				Call("close");
			}

			public unsafe sbyte* GetByteBuffer()
			{
				AndroidJavaObject byteBuffer = Call<AndroidJavaObject>("getByteBuffer");
				return AndroidJNI.GetDirectBufferAddress(byteBuffer.GetRawObject());
			}

			public long GetTimestamp()
			{
				return jniObj.Call<long>("getTimestamp");
			}

			public float[] GetCamPoseOnDevice()
			{
				return jniObj.Call<float[]>("getCamPoseOnDevice");
			}

			public float[] GetCamIntrinsics()
			{
				return jniObj.Call<float[]>("getCamIntrinsics");
			}
		}

		private AndroidInterface androidInterface;

		private void Awake()
		{
#if UNITY_EDITOR
			return;
#endif

			androidInterface = new AndroidInterface(gameObject);
			// await Configure(defaultCameraIndex, defaultTextureSize.x, defaultTextureSize.y);
		}

		private const string MetaCameraPermission = "horizonos.permission.HEADSET_CAMERA";

		private async Task<bool> CheckPermissions()
		{
			if (!await CheckPermission(Permission.Camera))
				return false;

			if (!await CheckPermission(MetaCameraPermission))
				return false;

			return true;
		}

		private async Task<bool> CheckPermission(string permission)
		{
			if (!Permission.HasUserAuthorizedPermission(permission))
			{
				Permission.RequestUserPermission(permission);

				await Awaitable.WaitForSecondsAsync(0.1f);

				while (!Application.isFocused)
					await Awaitable.NextFrameAsync();

				await Awaitable.WaitForSecondsAsync(0.5f);

				if (!Permission.HasUserAuthorizedPermission(permission))
					return false;
			}

			return true;
		}

		private void OnDestroy()
		{
			ImageAvailable = delegate { };
		}

		private async Task Configure()
		{
			await Configure(camID, defaultTextureSize.x, defaultTextureSize.y);
		}

		public async Task Configure(int index, int width, int height)
		{
// #if UNITY_EDITOR
// 			IsConfigured = true;
// 			return;
// #endif
			if (DeviceIsOpen)
				throw new ConfiguredException("Cannot configure camera while camera is open!");

			if (!await CheckPermissions())
				throw new Exception("Camera does not have permission!");

			androidInterface.Configure(index, width, height);
			texture = new Texture2D(width, height, TextureFormat.R8, 1, false);
			bufferSize = width * height; // * 4;

			float[] vals;
			vals = androidInterface.GetCamPoseOnDevice();
			Vector3 pos = new(vals[0], vals[1], -vals[2]);
			Quaternion rot = Quaternion.Inverse(new Quaternion(-vals[3], -vals[4], vals[5], vals[6])) *
			                 Quaternion.Euler(180, 0, 0);
			hardwarePose = new Pose(pos, rot);

			vals = androidInterface.GetCamIntrinsics();


			HardwareIntrinsics intrins = new()
			{
				FocalLength = new Vector2(vals[0], vals[1]),
				PrincipalPoint = new Vector2(vals[2], vals[3]),
				Resolution = new Vector2Int((int)vals[5], (int)vals[6]),
				Skew = vals[4]
			};

			Debug.Log($"[Camera Reader] Intrinsics:\n" +
			          $"Focal Length: {intrins.FocalLength.ToString()}\n" +
			          $"Resolution: {intrins.Resolution.ToString()}\n" +
			          $"PrincipalPoint: {intrins.PrincipalPoint.ToString()}\n" +
			          $"Skew: {intrins.Skew}");

			intrinsics = intrins;

			IsConfigured = true;
		}

		public async Task TryOpenCamera()
		{
			if (DeviceShouldBeOpen)
				return;

			DeviceShouldBeOpen = true;

			if (!IsConfigured)
				await Configure();

			if (!await CheckPermissions())
				throw new PermissionException("Camera does not have permission!");

#if UNITY_EDITOR
			return;
#endif

			do
			{
				androidInterface.OpenCamera();
				await Awaitable.WaitForSecondsAsync(1);
			} while (DeviceShouldBeOpen && !DeviceIsOpen);
		}

		public void CloseCamera()
		{
			if (!DeviceShouldBeOpen)
				return;

			DeviceShouldBeOpen = false;

#if UNITY_EDITOR
			return;
#endif

			androidInterface.CloseCamera();
		}

		// Messages sent from Android

#pragma warning disable IDE0051 // Remove unused private members

		private void OnDeviceOpened()
		{
			DeviceIsOpen = true;
			DeviceOpened.Invoke();
		}

		private async void OnDeviceClosed()
		{
			DeviceIsOpen = false;
			DeviceClosed.Invoke();

			await Awaitable.WaitForSecondsAsync(1);

			if (DeviceShouldBeOpen)
				await TryOpenCamera();
		}

		private void OnDeviceDisconnected()
		{
			DeviceIsOpen = false;
			DeviceDisconnected.Invoke();
		}

		private void OnDeviceError(string errorCodeAsString)
		{
			DeviceIsOpen = false;
			if (int.TryParse(errorCodeAsString, out int error))
				DeviceError.Invoke((ErrorCode)error);
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

			texture.LoadRawTextureData((IntPtr)buffer, bufferSize);
			texture.Apply();
			TimestampNs = androidInterface.GetTimestamp();

			ImageAvailable.Invoke(texture);
		}

		public struct HardwareIntrinsics
		{
			public Vector2 FocalLength;
			public Vector2 PrincipalPoint;
			public Vector2Int Resolution;
			public float Skew;
		}

#pragma warning restore IDE0051 // Remove unused private members
	}
}