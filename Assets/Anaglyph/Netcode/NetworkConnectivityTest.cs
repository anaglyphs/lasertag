using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

	private static NetworkState simualtedNetworkState = NetworkState.ConnectionFullInternet;

	[MenuItem("Lasertag/Simulated Networking State/No Connection")]
	private static void SimulatedNetworkingStateNoConnection()
	{
		simualtedNetworkState = NetworkState.NoConnection;
	}
	
	[MenuItem("Lasertag/Simulated Networking State/Only LAN")]
	private static void SimulatedNetworkingStateLANConnection()
	{
		simualtedNetworkState = NetworkState.ConnectionLAN;
	}

	[MenuItem("Lasertag/Simulated Networking State/Full Internet")]
	private static void SimulatedNetworkingStateInternetConnection()
	{
		simualtedNetworkState = NetworkState.ConnectionFullInternet;
	}
	
#endif
	
	public static NetworkState GetNetworkState()
	{
#if UNITY_EDITOR

		return simualtedNetworkState;

#elif UNITY_ANDROID && !UNITY_EDITOR // Android API 23+
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
#else
		return Application.internetReachability ==
		       NetworkReachability.NotReachable
			? NetworkState.NoConnection
			: NetworkState.ConnectionLAN;
#endif


	}
}