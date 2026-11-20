using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#elif UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace Anaglyph.Bluetooth
{
	/// <summary>
	/// A Bluetooth Low Energy advertisement of one 128-bit service UUID, and a scan for nearby
	/// advertisers whose service UUID starts with a prefix. The UUID is the whole payload: it is
	/// all an iPhone can advertise. UUIDs are lowercase text, 8-4-4-4-12 hex digits.
	/// Starting is safe to repeat, and returns false until Bluetooth is on and the user allows it.
	/// </summary>
	public sealed class BluetoothBeacon : IDisposable
	{
		public static bool IsSupported =>
			Application.platform is RuntimePlatform.Android or RuntimePlatform.IPhonePlayer;

		private bool advertisingRequested;
		private bool scanningRequested;

		public bool TryStartAdvertising(string serviceUuid)
		{
			advertisingRequested = true;
			return PlatformStartAdvertising(serviceUuid);
		}

		public void StopAdvertising()
		{
			if (!advertisingRequested) return;
			advertisingRequested = false;
			PlatformStopAdvertising();
		}

		public bool TryStartScanning(string serviceUuidPrefix)
		{
			scanningRequested = true;
			return PlatformStartScanning(serviceUuidPrefix);
		}

		public void StopScanning()
		{
			if (!scanningRequested) return;
			scanningRequested = false;
			PlatformStopScanning();
		}

		/// <summary>One service UUID heard since the last call. Each UUID comes out once however often it was heard.</summary>
		public bool TryTakeSighting(out string serviceUuid)
		{
			serviceUuid = scanningRequested ? PlatformTakeSighting() : null;
			return serviceUuid != null;
		}

		public void Dispose()
		{
			StopAdvertising();
			StopScanning();
			PlatformDispose();
		}

#if UNITY_ANDROID && !UNITY_EDITOR
		private static readonly string[] requiredPermissions =
		{
			"android.permission.BLUETOOTH_ADVERTISE",
			"android.permission.BLUETOOTH_SCAN",
		};

		private AndroidJavaObject javaBeacon;
		private bool permissionsRequested;

		private bool PlatformStartAdvertising(string serviceUuid) =>
			HasPermissions() && JavaBeacon().Call<bool>("startAdvertising", serviceUuid);

		private void PlatformStopAdvertising() => javaBeacon?.Call("stopAdvertising");

		private bool PlatformStartScanning(string serviceUuidPrefix) =>
			HasPermissions() && JavaBeacon().Call<bool>("startScanning", serviceUuidPrefix);

		private void PlatformStopScanning() => javaBeacon?.Call("stopScanning");

		private string PlatformTakeSighting() => javaBeacon?.Call<string>("takeSighting");

		private void PlatformDispose()
		{
			javaBeacon?.Dispose();
			javaBeacon = null;
		}

		private AndroidJavaObject JavaBeacon()
		{
			if (javaBeacon != null) return javaBeacon;

			using AndroidJavaClass player = new("com.unity3d.player.UnityPlayer");
			using AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
			return javaBeacon = new AndroidJavaObject("com.anaglyph.bluetooth.BluetoothBeacon", activity);
		}

		/// <summary>Asks the user once per run; a refusal leaves the beacon silent.</summary>
		private bool HasPermissions()
		{
			foreach (string permission in requiredPermissions)
			{
				if (Permission.HasUserAuthorizedPermission(permission)) continue;

				if (!permissionsRequested) Permission.RequestUserPermissions(requiredPermissions);
				permissionsRequested = true;
				return false;
			}

			return true;
		}
#elif UNITY_IOS && !UNITY_EDITOR
		private const int UuidTextLength = 36;

		[DllImport("__Internal")] private static extern int AnaglyphBluetoothBeaconStartAdvertising(string serviceUuid);
		[DllImport("__Internal")] private static extern void AnaglyphBluetoothBeaconStopAdvertising();
		[DllImport("__Internal")] private static extern int AnaglyphBluetoothBeaconStartScanning(string serviceUuidPrefix);
		[DllImport("__Internal")] private static extern void AnaglyphBluetoothBeaconStopScanning();
		[DllImport("__Internal")] private static extern int AnaglyphBluetoothBeaconTakeSighting(byte[] buffer, int capacity);

		private readonly byte[] sightingBuffer = new byte[UuidTextLength];

		private bool PlatformStartAdvertising(string serviceUuid) =>
			AnaglyphBluetoothBeaconStartAdvertising(serviceUuid) != 0;

		private void PlatformStopAdvertising() => AnaglyphBluetoothBeaconStopAdvertising();

		private bool PlatformStartScanning(string serviceUuidPrefix) =>
			AnaglyphBluetoothBeaconStartScanning(serviceUuidPrefix) != 0;

		private void PlatformStopScanning() => AnaglyphBluetoothBeaconStopScanning();

		private string PlatformTakeSighting()
		{
			int length = AnaglyphBluetoothBeaconTakeSighting(sightingBuffer, sightingBuffer.Length);
			return length > 0 ? Encoding.ASCII.GetString(sightingBuffer, 0, length) : null;
		}

		private void PlatformDispose()
		{
		}
#else
		private bool PlatformStartAdvertising(string serviceUuid) => false;

		private void PlatformStopAdvertising()
		{
		}

		private bool PlatformStartScanning(string serviceUuidPrefix) => false;

		private void PlatformStopScanning()
		{
		}

		private string PlatformTakeSighting() => null;

		private void PlatformDispose()
		{
		}
#endif
	}
}
