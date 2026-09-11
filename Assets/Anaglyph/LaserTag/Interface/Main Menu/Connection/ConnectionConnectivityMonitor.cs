using System;
using System.Threading;
using Anaglyph.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Interface
{
	// Owns device connectivity sampling; the menu decides how changes are presented.
	internal sealed class ConnectionConnectivityMonitor : IDisposable
	{
		public event Action<bool> LanConnectionChanged;
		public event Action InternetConnectionChanged;
		public event Action<bool> BluetoothEnabledChanged;

		public bool HasFullInternet { get; private set; }
		public bool HasBluetoothState { get; private set; }
		public bool BluetoothIsEnabled { get; private set; }

		private NetworkState networkState;
		private bool hasNetworkState;
		private bool hasLoggedBluetoothCheckFailure;
		private CancellationTokenSource pollCancellation;

		public void Start()
		{
			Stop();
			// A newly bound view needs an initial notification, even if the device
			// has not changed since the previous view was disabled.
			hasNetworkState = false;
			HasBluetoothState = false;
			pollCancellation = new CancellationTokenSource();
			Poll(pollCancellation.Token);
		}

		public void Stop()
		{
			pollCancellation?.Cancel();
			pollCancellation?.Dispose();
			pollCancellation = null;
		}

		public void Dispose() => Stop();

		private async void Poll(CancellationToken token)
		{
			try
			{
				while (!token.IsCancellationRequested)
				{
					CheckNetworkConnection();
					if (token.IsCancellationRequested) return;
					CheckBluetoothConnection();
					await Awaitable.WaitForSecondsAsync(1f, token);
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void CheckNetworkConnection()
		{
			NetworkState newState = NetworkConnectivityTest.GetNetworkState();
			bool connectionChanged = !hasNetworkState ||
				((newState ^ networkState) & NetworkState.ConnectionLAN) != 0;
			bool internetChanged = !hasNetworkState ||
				((newState ^ networkState) & NetworkState.FullInternetFlag) != 0;

			networkState = newState;
			hasNetworkState = true;
			HasFullInternet = (newState & NetworkState.FullInternetFlag) != 0;

			// Notify only on transitions so dismissing a warning lasts until the
			// corresponding device state changes, rather than the next poll.
			if (connectionChanged)
				LanConnectionChanged?.Invoke((newState & NetworkState.ConnectionLAN) != 0);
			if (internetChanged)
				InternetConnectionChanged?.Invoke();
		}

		private void CheckBluetoothConnection()
		{
			if (!TryGetBluetoothEnabled(out bool isEnabled)) return;

			bool changed = !HasBluetoothState || BluetoothIsEnabled != isEnabled;
			BluetoothIsEnabled = isEnabled;
			HasBluetoothState = true;
			if (changed)
				BluetoothEnabledChanged?.Invoke(isEnabled);
		}

		private bool TryGetBluetoothEnabled(out bool isEnabled)
		{
#if UNITY_ANDROID
			if (Application.isEditor)
			{
				isEnabled = true;
				return true;
			}

			try
			{
				using AndroidJavaClass unityPlayer =
					new("com.unity3d.player.UnityPlayer");
				using AndroidJavaObject activity =
					unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
				using AndroidJavaObject contentResolver =
					activity.Call<AndroidJavaObject>("getContentResolver");
				using AndroidJavaClass globalSettings =
					new("android.provider.Settings$Global");

				int bluetoothState = globalSettings.CallStatic<int>(
					"getInt", contentResolver, "bluetooth_on", -1);
				if (bluetoothState < 0)
					throw new InvalidOperationException(
						"Android did not report a Bluetooth setting.");

				isEnabled = bluetoothState != 0;
				hasLoggedBluetoothCheckFailure = false;
				return true;
			}
			catch (Exception exception)
			{
				isEnabled = false;
				if (!hasLoggedBluetoothCheckFailure)
				{
					hasLoggedBluetoothCheckFailure = true;
					Debug.LogWarning("Could not check whether Bluetooth is enabled.");
					Debug.LogException(exception);
				}

				return false;
			}
#else
			isEnabled = true;
			return true;
#endif
		}

		public static void OpenWifiSettings() =>
			OpenAndroidSettings("android.settings.WIFI_SETTINGS", "Wi-Fi");

		public static void OpenBluetoothSettings() =>
			OpenAndroidSettings("android.settings.BLUETOOTH_SETTINGS", "Bluetooth");

		private static void OpenAndroidSettings(string action, string settingName)
		{
#if UNITY_ANDROID
			if (Application.isEditor)
			{
				Debug.LogWarning($"{settingName} settings can only be opened from an Android player.");
				return;
			}

			try
			{
				using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
				using AndroidJavaObject activity =
					unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
				using AndroidJavaObject packageManager =
					activity.Call<AndroidJavaObject>("getPackageManager");

				if (TryStartAndroidActivity(activity, packageManager, action))
					return;

				if (!TryStartAndroidActivity(activity, packageManager, "android.settings.SETTINGS"))
					Debug.LogError("No Android system settings activity is available.");
			}
			catch (AndroidJavaException exception)
			{
				Debug.LogException(exception);
			}
#else
			Debug.LogWarning($"{settingName} settings can only be opened from an Android player.");
#endif
		}

#if UNITY_ANDROID
		private static bool TryStartAndroidActivity(
			AndroidJavaObject activity,
			AndroidJavaObject packageManager,
			string action)
		{
			using AndroidJavaObject intent = new("android.content.Intent", action);
			using AndroidJavaObject component =
				intent.Call<AndroidJavaObject>("resolveActivity", packageManager);

			if (component == null)
				return false;

			activity.Call("startActivity", intent);
			return true;
		}
#endif
	}
}
