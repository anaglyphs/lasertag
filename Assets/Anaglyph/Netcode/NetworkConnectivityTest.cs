using System;
using UnityEngine;

namespace Anaglyph.Netcode
{
	[Flags]
	public enum NetworkState
	{
		NoConnection = 0,
		ConnectionLAN = 1,
		FullInternetFlag = 2,
		ConnectionFullInternet = 3
	}

	public static class NetworkConnectivityTest
	{
#if UNITY_EDITOR

		/// Set from the Lasertag Simulation Settings window.
		public static NetworkState SimulatedNetworkState { get; set; } =
			NetworkState.ConnectionFullInternet;

#endif

		public static NetworkState GetNetworkState()
		{
#if UNITY_EDITOR

			return SimulatedNetworkState;

#endif
			
        const int NET_CAPABILITY_INTERNET = 12;
        const int NET_CAPABILITY_VALIDATED = 16;

        using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
        using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        using AndroidJavaObject connectivityManager = 
	        activity.Call<AndroidJavaObject>("getSystemService", "connectivity");
        using AndroidJavaObject network = connectivityManager.Call<AndroidJavaObject>("getActiveNetwork");

        if (network == null)
            return NetworkState.NoConnection;

        using AndroidJavaObject capabilities =
            connectivityManager.Call<AndroidJavaObject>(
                "getNetworkCapabilities", network);

        if (capabilities == null)
            return NetworkState.NoConnection;

        bool internetConfigured = capabilities.Call<bool>(
            "hasCapability", NET_CAPABILITY_INTERNET);

        bool internetValidated = capabilities.Call<bool>(
            "hasCapability", NET_CAPABILITY_VALIDATED);

        return internetConfigured && internetValidated
            ? NetworkState.ConnectionFullInternet
            : NetworkState.ConnectionLAN;
		}
	}
}