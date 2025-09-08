using Anaglyph.Netcode;
using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaBluetoothNetworkDiscovery : MonoBehaviour
	{
		public static MetaBluetoothNetworkDiscovery Instance { get; private set; }

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport) NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		private static string LogHeader = "[AutoJoiner] ";
		private static void Log(string str) => Debug.Log(LogHeader + str);

		private static string LanPrefix = "IP:";
		private static string RelayPrefix = "Relay:";

		private Task currentTask = Task.CompletedTask;
		private void QueueTask(Func<Task> taskFactory)
		{
			currentTask = currentTask.ContinueWith(_ => taskFactory(), TaskContinuationOptions.ExecuteSynchronously).Unwrap();
		}

		private void Awake()
		{
			Instance = this;
			NetcodeManagement.StateChange += OnNetworkStateChange;
			OVRColocationSession.ColocationSessionDiscovered += HandleColocationSessionDiscovered;
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChange -= OnNetworkStateChange;
			OVRColocationSession.ColocationSessionDiscovered -= HandleColocationSessionDiscovered;
		}

		private void OnNetworkStateChange(NetcodeManagement.NetworkState state) => HandleChange();

		private void OnEnable() => HandleChange();
		private void OnDisable() => HandleChange();

#if !UNITY_EDITOR
		private void OnApplicationFocus(bool focus) => HandleChange();
		private void OnApplicationPause(bool pause) => HandleChange();
#endif

		private void HandleChange()
		{
#if UNITY_EDITOR
			if(enabled)
#else
			if (enabled && Application.isFocused)
#endif
			{
				if (manager == null)
					return;

				switch (NetcodeManagement.State)
				{
					case NetcodeManagement.NetworkState.Disconnected:
						Log("Stopping advertisement, starting discovery");
						QueueTask(StopAdvertisement);
						QueueTask(StartDiscovery);
						break;

					case NetcodeManagement.NetworkState.Connecting:
						Log("Stopping discovery");
						QueueTask(StartDiscovery);
						break;

					case NetcodeManagement.NetworkState.Connected:
						QueueTask(StartAdvertisement);
						break;
				}
			}
			else
			{
				Log("Stopping both discovery and advertisement");
				QueueTask(StopAdvertisement);
				QueueTask(StopDiscovery);
			}
		}

		
		private static async Task StartDiscovery() => await OVRColocationSession.StartDiscoveryAsync();
		private static async Task StopDiscovery() => await OVRColocationSession.StopDiscoveryAsync();

		private static async Task StopAdvertisement() => await OVRColocationSession.StopAdvertisementAsync();
		private async Task StartAdvertisement()
		{
			string message = "";

			switch (transport.Protocol)
			{
				case UnityTransport.ProtocolType.UnityTransport:
					string address = transport.ConnectionData.Address;
					message = LanPrefix + address;
					break;

				case UnityTransport.ProtocolType.RelayUnityTransport:
					message = RelayPrefix + NetcodeManagement.CurrentSessionName;
					break;
			}

			Log($"Starting advertisement {message}");
			await OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(message));
		}

		private void HandleColocationSessionDiscovered(OVRColocationSession.Data data)
		{
			string message = Encoding.ASCII.GetString(data.Metadata);
			Log($"Discovered {message}");

			if (NetworkManager.Singleton.IsListening)
				return;

			if(message.StartsWith(LanPrefix))
			{
				NetcodeManagement.ConnectLAN(message.Remove(0, LanPrefix.Length));
			} else if(message.StartsWith(RelayPrefix))
			{
				NetcodeManagement.ConnectUnityServices(message.Remove(0, RelayPrefix.Length));
			}
		}
	}
}