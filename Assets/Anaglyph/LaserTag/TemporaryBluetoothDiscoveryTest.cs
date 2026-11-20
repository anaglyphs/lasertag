#if UNITY_ANDROID
using System.Collections;
using Anaglyph.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>Temporary: launch extras that host a LAN session or silence multicast, for testing Bluetooth discovery over adb.</summary>
	public class TemporaryBluetoothDiscoveryTest : MonoBehaviour
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void StartInDevelopmentBuilds()
		{
			if (Application.isEditor || !Debug.isDebugBuild) return;
			DontDestroyOnLoad(new GameObject(nameof(TemporaryBluetoothDiscoveryTest), typeof(TemporaryBluetoothDiscoveryTest)));
		}

		private IEnumerator Start()
		{
			using AndroidJavaClass player = new("com.unity3d.player.UnityPlayer");
			using AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
			using AndroidJavaObject intent = activity.Call<AndroidJavaObject>("getIntent");
			bool host = intent.Call<bool>("getBooleanExtra", "bluetoothTestHost", false);
			bool bluetoothOnly = intent.Call<bool>("getBooleanExtra", "bluetoothTestOnly", false);
			bool hostDirect = intent.Call<bool>("getBooleanExtra", "directTestHost", false);
			Debug.Log($"[BluetoothTest] host {host}, bluetooth only {bluetoothOnly}");

			if (bluetoothOnly)
				foreach (MulticastSessionDiscovery multicast in FindObjectsByType<MulticastSessionDiscovery>(FindObjectsSortMode.None))
					multicast.enabled = false;

			if (!host && !hostDirect) yield break;
			yield return new WaitForSeconds(8);
			Debug.Log($"[BluetoothTest] hosting {(hostDirect ? "direct" : "LAN")}");
			NetcodeManagement.Host(hostDirect ? NetcodeManagement.Protocol.Direct : NetcodeManagement.Protocol.LAN);
		}

		private float nextReport;

		private void Update()
		{
			if (Time.unscaledTime < nextReport) return;
			nextReport = Time.unscaledTime + 5;
			Debug.Log($"[BluetoothTest] activity {SessionDiscovery.Activity}, listening {SessionDiscovery.IsListening}, state {NetcodeManagement.State}, advertisement {SessionDiscovery.CurrentAdvertisement()}");
		}
	}
}
#endif
