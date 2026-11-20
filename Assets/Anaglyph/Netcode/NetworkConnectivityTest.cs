using UnityEngine;

namespace Anaglyph.Netcode
{
	public enum NetworkState
	{
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
            ? NetworkState.FullInternet
            : NetworkState.LocalNetworkOnly;
#else
			// iOS and desktops can't tell a validated internet connection from a local one
			return Application.internetReachability == NetworkReachability.NotReachable
				? NetworkState.NoConnection
				: NetworkState.FullInternet;
#endif
		}
	}
}