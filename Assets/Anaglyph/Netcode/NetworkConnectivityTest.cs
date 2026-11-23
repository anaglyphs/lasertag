using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Anaglyph.Netcode
{
	public enum NetworkState
	{
		/// <summary>The Wi-Fi radio is switched off and no other connection exists. Wi-Fi Direct can't work either.</summary>
		WifiOff,
		NoConnection,
		LocalNetworkOnly,
		FullInternet
	}

	public static class NetworkConnectivityTest
	{
#if UNITY_EDITOR

		/// Set from the Lasertag Simulation Settings window.
		public static NetworkState SimulatedNetworkState { get; set; } =
			NetworkState.FullInternet;

#endif

		public static NetworkState GetNetworkState()
		{
#if UNITY_EDITOR

			return SimulatedNetworkState;

#elif UNITY_ANDROID

        const int NET_CAPABILITY_INTERNET = 12;
        const int NET_CAPABILITY_VALIDATED = 16;

        using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
        using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        using AndroidJavaObject connectivityManager =
	        activity.Call<AndroidJavaObject>("getSystemService", "connectivity");
        using AndroidJavaObject network = connectivityManager.Call<AndroidJavaObject>("getActiveNetwork");

        if (network == null)
            return IsWifiEnabled() ? NetworkState.NoConnection : NetworkState.WifiOff;

        using AndroidJavaObject capabilities =
            connectivityManager.Call<AndroidJavaObject>(
                "getNetworkCapabilities", network);

        if (capabilities == null)
            return IsWifiEnabled() ? NetworkState.NoConnection : NetworkState.WifiOff;

        bool internetConfigured = capabilities.Call<bool>(
            "hasCapability", NET_CAPABILITY_INTERNET);

        bool internetValidated = capabilities.Call<bool>(
            "hasCapability", NET_CAPABILITY_VALIDATED);

        return internetConfigured && internetValidated
            ? NetworkState.FullInternet
            : NetworkState.LocalNetworkOnly;
#else
			// iOS and desktops can't tell a validated internet connection from a local one
			if (Application.internetReachability != NetworkReachability.NotReachable)
				return NetworkState.FullInternet;
			return IsWifiEnabled() ? NetworkState.NoConnection : NetworkState.WifiOff;
#endif
		}

		/// <summary>Whether the Wi-Fi radio is switched on, connected to a network or not.</summary>
		public static bool IsWifiEnabled()
		{
#if UNITY_EDITOR
			return SimulatedNetworkState != NetworkState.WifiOff;
#elif UNITY_ANDROID
			using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
			using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
			using AndroidJavaObject wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
			return wifiManager == null || wifiManager.Call<bool>("isWifiEnabled");
#elif UNITY_IOS
			return AnaglyphIsWifiEnabled() != 0;
#else
			return true;
#endif
		}

#if UNITY_IOS && !UNITY_EDITOR
		[DllImport("__Internal")] private static extern int AnaglyphIsWifiEnabled();
#endif
	}
}
