using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#elif UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
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
	/// is the access point and the others join by network name and passphrase. Android creates and joins;
	/// an iPhone only joins. Joining is as an ordinary Wi-Fi client, and the system asks the user the first time
	/// it joins a given network. On Android one instance can own a group while another is joined to a different one.
	/// With bindAppToGroup every socket the app opens while joined uses the group; without it only sockets bound to LocalAddress do.
	/// The network name must start with "DIRECT-" and two letters or digits; the passphrase is 8 to 63 characters.
	/// Android also asks for the nearby devices permission on first use. A refusal of either ends as Failed.
	/// </summary>
	public sealed class WifiDirectGroup : IDisposable
	{
		/// <summary>Lets the device pick: 5 GHz where it can host there, 2.4 GHz otherwise. A named frequency is used or the create fails.</summary>
		public const int AnyFrequency = 0;

#if UNITY_ANDROID && !UNITY_EDITOR
		private const string JavaClassName = "com.anaglyph.wifidirect.WifiDirectGroup";
		private const string NearbyDevicesPermission = "android.permission.NEARBY_WIFI_DEVICES";

		private readonly AndroidJavaObject javaGroup;
		private bool permissionRefused;
		// a create or join asked for while the permission prompt is up
		private Action startWhenPermitted;

		public static bool CanCreate
		{
			get
			{
				using AndroidJavaClass javaClass = new(JavaClassName);
				using AndroidJavaObject activity = CurrentActivity();
				return javaClass.CallStatic<bool>("isSupported", activity);
			}
		}

		public static bool CanJoin => CanCreate;

		public static bool CanJoinAsSuggestedNetwork => CanJoin;

		/// <summary>The last create or join ended Failed because the user said no.</summary>
		public bool UserRefused => permissionRefused || javaGroup.Call<bool>("userRefused");

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

		/// <summary>This device's own address inside a joined group, known once Ready.</summary>
		public string LocalAddress => javaGroup.Call<string>("localAddress");

		/// <summary>The frequency of a group this device owns, known once Ready.</summary>
		public int FrequencyMHz => javaGroup.Call<int>("frequencyMHz");

		public void Create(string networkName, string passphrase, int frequencyMHz = AnyFrequency) =>
			StartOncePermitted(() => javaGroup.Call("create", networkName, passphrase, frequencyMHz));

		public void Join(string networkName, string passphrase, bool bindAppToGroup = true) =>
			StartOncePermitted(() => javaGroup.Call("join", networkName, passphrase, bindAppToGroup));

		/// <summary>
		/// Joins without the system asking the user anything. On Android the device then goes on scanning for
		/// networks while joined, and cannot own a group of its own at the same time.
		/// </summary>
		public void JoinWithoutSystemPrompt(string networkName, string passphrase) =>
			StartOncePermitted(() => javaGroup.Call("joinAsWifiDirectClient", networkName, passphrase));

		/// <summary>
		/// Joins by suggesting the group to the system, which then joins it by itself. The system asks the user
		/// once, the first time the app suggests any network, and never per join. Ends Failed when the user refused
		/// that, and when the device is on another Wi-Fi network. Joined this way the owner's network scans
		/// interrupt the group for a few seconds instead of seven.
		/// </summary>
		public void JoinAsSuggestedNetwork(string networkName, string passphrase) =>
			StartOncePermitted(() => javaGroup.Call("joinAsSuggestedNetwork", networkName, passphrase));

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
#elif UNITY_IOS && !UNITY_EDITOR
		// every Android group owner has this address
		private const string AndroidOwnerAddress = "192.168.49.1";

		[DllImport("__Internal")] private static extern void AnaglyphWifiDirectGroupJoin(string networkName, string passphrase);
		[DllImport("__Internal")] private static extern void AnaglyphWifiDirectGroupLeave();
		[DllImport("__Internal")] private static extern int AnaglyphWifiDirectGroupState();
		[DllImport("__Internal")] private static extern int AnaglyphWifiDirectGroupUserRefused();

		private bool createAsked;

		public static bool CanCreate => false;

		public static bool CanJoin => true;

		public static bool CanJoinAsSuggestedNetwork => false;

		public bool UserRefused => AnaglyphWifiDirectGroupUserRefused() != 0;

		public WifiDirectGroupState State =>
			createAsked ? WifiDirectGroupState.Failed : (WifiDirectGroupState)AnaglyphWifiDirectGroupState();

		public string OwnerAddress => AndroidOwnerAddress;

		public string LocalAddress => null;

		public int FrequencyMHz => 0;

		public void Create(string networkName, string passphrase, int frequencyMHz = AnyFrequency) => createAsked = true;

		public void Join(string networkName, string passphrase, bool bindAppToGroup = true)
		{
			createAsked = false;
			AnaglyphWifiDirectGroupJoin(networkName, passphrase);
		}

		/// <summary>An iPhone has one way to join, and it always asks.</summary>
		public void JoinWithoutSystemPrompt(string networkName, string passphrase) => Join(networkName, passphrase);

		public void JoinAsSuggestedNetwork(string networkName, string passphrase) => Join(networkName, passphrase);

		public void Leave()
		{
			createAsked = false;
			AnaglyphWifiDirectGroupLeave();
		}

		public void Dispose() => Leave();
#else
		public static bool CanCreate => false;

		public static bool CanJoin => false;

		public static bool CanJoinAsSuggestedNetwork => false;

		public bool UserRefused => false;

		public WifiDirectGroupState State { get; private set; }

		public string OwnerAddress => null;

		public string LocalAddress => null;

		public int FrequencyMHz => 0;

		public void Create(string networkName, string passphrase, int frequencyMHz = AnyFrequency) => State = WifiDirectGroupState.Failed;

		public void Join(string networkName, string passphrase, bool bindAppToGroup = true) => State = WifiDirectGroupState.Failed;

		public void JoinWithoutSystemPrompt(string networkName, string passphrase) => State = WifiDirectGroupState.Failed;

		public void JoinAsSuggestedNetwork(string networkName, string passphrase) => State = WifiDirectGroupState.Failed;

		public void Leave() => State = WifiDirectGroupState.Idle;

		public void Dispose()
		{
		}
#endif
	}
}
