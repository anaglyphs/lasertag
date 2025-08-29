using Anaglyph.Netcode;
using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaBluetoothNetworkConnector : MonoBehaviour
	{
		public static MetaBluetoothNetworkConnector Instance { get; private set; }

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport) NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		private static string LogHeader = "[AutoJoiner] ";
		private static void Log(string str) => Debug.Log(LogHeader + str);

		private static string LanPrefix = "IP:";
		private static string RelayPrefix = "Relay:";

		private void Awake()
		{
			Instance = this;
			NetcodeHelper.StateChange += OnNetworkStateChange;
			OVRColocationSession.ColocationSessionDiscovered += HandleColocationSessionDiscovered;
		}

		private void OnDestroy()
		{
			NetcodeHelper.StateChange -= OnNetworkStateChange;
			OVRColocationSession.ColocationSessionDiscovered -= HandleColocationSessionDiscovered;
		}

		private async void OnNetworkStateChange(NetcodeHelper.NetworkState state) => await HandleChange();

		private async void OnEnable() => await HandleChange();
		private async void OnDisable() => await HandleChange();

#if !UNITY_EDITOR
		private void OnApplicationFocus(bool focus) => await HandleChange();
		private void OnApplicationPause(bool pause) => await HandleChange();
#endif

		private async Task HandleChange()
		{
#if UNITY_EDITOR
			if(enabled)
#else
			if (enabled && Application.isFocused)
#endif
			{
				if (manager == null)
					return;

				switch (NetcodeHelper.State)
				{
					case NetcodeHelper.NetworkState.Disconnected:
						Log("Stopping advertisement, starting discovery");
						await OVRColocationSession.StopAdvertisementAsync();
						await OVRColocationSession.StartDiscoveryAsync();
						break;

					case NetcodeHelper.NetworkState.Connecting:
						Log("Stopping discovery");
						await OVRColocationSession.StopDiscoveryAsync();
						break;

					case NetcodeHelper.NetworkState.Connected:
						await Broadcast();
						break;
				}
			}
			else
			{
				Log("Stopping both discovery and advertisement");
				await OVRColocationSession.StopDiscoveryAsync();
				await OVRColocationSession.StopAdvertisementAsync();
			}
		}

		private async Task Broadcast()
		{
			string message = "";

			switch (transport.Protocol)
			{
				case UnityTransport.ProtocolType.UnityTransport:
					string address = transport.ConnectionData.Address;
					message = LanPrefix + address;
					break;

				case UnityTransport.ProtocolType.RelayUnityTransport:
					message = RelayPrefix + NetcodeHelper.CurrentSessionName;
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
				NetcodeHelper.ConnectLAN(message.Remove(0, LanPrefix.Length));
			} else if(message.StartsWith(RelayPrefix))
			{
				NetcodeHelper.ConnectUnityServices(message.Remove(0, RelayPrefix.Length));
			}
		}
	}
}