using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Anaglyph.WifiDirect
{
	public enum WifiDirectGroupState
	{
		Idle,
		Forming,
		Ready,
		Failed
	}

	/// <summary>
	/// One Wi-Fi Direct group: a Wi-Fi network without a router, where the device that creates it
	/// is the access point and the others join by network name and passphrase. Android only.
	/// The network name must start with "DIRECT-" and two letters or digits; the passphrase is 8 to 63 characters.
	/// Asks the user for the nearby devices permission on first use; a refusal ends as Failed.
	/// </summary>
	public sealed class WifiDirectGroup : IDisposable
	{
#if UNITY_ANDROID && !UNITY_EDITOR
		private const string JavaClassName = "com.anaglyph.wifidirect.WifiDirectGroup";
		private const string NearbyDevicesPermission = "android.permission.NEARBY_WIFI_DEVICES";

		private readonly AndroidJavaObject javaGroup;
		private bool permissionRefused;
		// a create or join asked for while the permission prompt is up
		private Action startWhenPermitted;

		public static bool IsSupported
		{
			get
			{
				using AndroidJavaClass javaClass = new(JavaClassName);
				using AndroidJavaObject activity = CurrentActivity();
				return javaClass.CallStatic<bool>("isSupported", activity);
			}
		}

		/// <summary>Also removes a group this device still owns from an earlier run that was killed.</summary>
		public WifiDirectGroup()
		{
			using AndroidJavaObject activity = CurrentActivity();
			javaGroup = new AndroidJavaObject(JavaClassName, activity);
		}

		public WifiDirectGroupState State =>
			permissionRefused ? WifiDirectGroupState.Failed :
			startWhenPermitted != null ? WifiDirectGroupState.Forming :
			(WifiDirectGroupState)javaGroup.Call<int>("state");

		/// <summary>The owner's address inside the group, known once Ready.</summary>
		public string OwnerAddress => javaGroup.Call<string>("ownerAddress");

		public void Create(string networkName, string passphrase) =>
			StartOncePermitted(() => javaGroup.Call("create", networkName, passphrase));

		public void Join(string networkName, string passphrase) =>
			StartOncePermitted(() => javaGroup.Call("join", networkName, passphrase));

		public void Leave()
		{
			startWhenPermitted = null;
			permissionRefused = false;
			javaGroup.Call("leave");
		}

		public void Dispose()
		{
			Leave();
			javaGroup.Call("dispose");
			javaGroup.Dispose();
		}

		private void StartOncePermitted(Action start)
		{
			permissionRefused = false;
			if (Permission.HasUserAuthorizedPermission(NearbyDevicesPermission))
			{
				start();
				return;
			}

			startWhenPermitted = start;
			PermissionCallbacks callbacks = new();
			callbacks.PermissionGranted += _ => OnPermissionAnswered(start, true);
			callbacks.PermissionDenied += _ => OnPermissionAnswered(start, false);
			Permission.RequestUserPermission(NearbyDevicesPermission, callbacks);
		}

		private void OnPermissionAnswered(Action start, bool granted)
		{
			// left, or asked again, while the prompt was up
			if (startWhenPermitted != start) return;

			startWhenPermitted = null;
			permissionRefused = !granted;
			if (granted) start();
		}

		private static AndroidJavaObject CurrentActivity()
		{
			using AndroidJavaClass player = new("com.unity3d.player.UnityPlayer");
			return player.GetStatic<AndroidJavaObject>("currentActivity");
		}
#else
		public static bool IsSupported => false;

		public WifiDirectGroupState State { get; private set; }

		public string OwnerAddress => null;

		public void Create(string networkName, string passphrase) => State = WifiDirectGroupState.Failed;

		public void Join(string networkName, string passphrase) => State = WifiDirectGroupState.Failed;

		public void Leave() => State = WifiDirectGroupState.Idle;

		public void Dispose()
		{
		}
#endif
	}
}
