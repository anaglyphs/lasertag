using UnityEditor;
using UnityEngine;

public enum NetworkingState
{
	NoConnection = 0,
	ConnectionLAN = 1,
	ConnectionFullInternet = 2
}

public static class NetworkConnectivityTest
{
#if UNITY_EDITOR

	private static NetworkingState simualtedNetworkingState = NetworkingState.ConnectionFullInternet;

	[MenuItem("Lasertag/Simulated Networking State/No Connection")]
	private static void SimulatedNetworkingStateNoConnection()
	{
		simualtedNetworkingState = NetworkingState.NoConnection;
	}
	
	[MenuItem("Lasertag/Simulated Networking State/Only LAN")]
	private static void SimulatedNetworkingStateLANConnection()
	{
		simualtedNetworkingState = NetworkingState.ConnectionLAN;
	}

	[MenuItem("Lasertag/Simulated Networking State/Full Internet")]
	private static void SimulatedNetworkingStateInternetConnection()
	{
		simualtedNetworkingState = NetworkingState.ConnectionFullInternet;
	}
	
#endif
	
	public static NetworkingState GetNetworkState()
	{
#if UNITY_EDITOR

		return simualtedNetworkingState;

#elif UNITY_ANDROID && !UNITY_EDITOR // Android API 23+
        const int NET_CAPABILITY_INTERNET = 12;
        const int NET_CAPABILITY_VALIDATED = 16;

        using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
        using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        using AndroidJavaObject connectivityManager = 
	        activity.Call<AndroidJavaObject>("getSystemService", "connectivity");
        using AndroidJavaObject network = connectivityManager.Call<AndroidJavaObject>("getActiveNetwork");

        if (network == null)
            return AndroidNetworkState.NoActiveNetwork;

        using AndroidJavaObject capabilities =
            connectivityManager.Call<AndroidJavaObject>(
                "getNetworkCapabilities", network);

        if (capabilities == null)
            return AndroidNetworkState.NoActiveNetwork;

        bool internetConfigured = capabilities.Call<bool>(
            "hasCapability", NET_CAPABILITY_INTERNET);

        bool internetValidated = capabilities.Call<bool>(
            "hasCapability", NET_CAPABILITY_VALIDATED);

        return internetConfigured && internetValidated
            ? AndroidNetworkState.ValidatedInternet
            : AndroidNetworkState.LinkWithoutValidatedInternet;
#else
		return Application.internetReachability ==
		       NetworkReachability.NotReachable
			? AndroidNetworkState.NoActiveNetwork
			: AndroidNetworkState.LinkWithoutValidatedInternet;
#endif


	}
}