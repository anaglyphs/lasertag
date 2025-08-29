using Anaglyph.Netcode;
using System;
using System.Text;
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

		private void Start()
		{
			manager.OnClientStarted += OnClientStarted;
			OVRColocationSession.ColocationSessionDiscovered += HandleColocationSessionDiscovered;
		}

		private void OnEnable() => HandleChange();
		private void OnDisable() => HandleChange();

		private void OnClientStarted() => HandleChange();
		private void OnClientStopped(bool b) => HandleChange();

#if !UNITY_EDITOR
		private void OnApplicationFocus(bool focus) => HandleChange();
		private void OnApplicationPause(bool pause) => HandleChange();
#endif

		private void Awake()
		{
			Instance = this;
			NetcodeHelper.IsRunningChange += IsNetworkRunningChanged;
		}

		private void OnDestroy()
		{
			NetcodeHelper.IsRunningChange -= IsNetworkRunningChanged;
			OVRColocationSession.ColocationSessionDiscovered -= HandleColocationSessionDiscovered;
		}

		private void HandleChange()
		{

			if (!enabled 
#if !UNITY_EDITOR
				|| !Application.isFocused
#endif
				)
			{
				Log("Stopping both discovery and advertisement");
				OVRColocationSession.StopDiscoveryAsync();
				OVRColocationSession.StopAdvertisementAsync();
			}
			else
			{
				if (manager == null)
					return;

				if (manager.IsListening)
					NetworkStarted();
				else
					NetworkStopped();
			}
		}

		private void IsNetworkRunningChanged(bool isRunning)
		{
			if (isRunning)
				NetworkStarted();
			else
				NetworkStopped();
		}

		private async void NetworkStarted()
		{
			Log("Stopping discovery");
			await OVRColocationSession.StopDiscoveryAsync();
		}

		private async void OnClientStarted()
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

		private async void NetworkStopped()
		{
			Log("Stopping advertisement");
			await OVRColocationSession.StopAdvertisementAsync();
			Log("Starting discovery");
			await OVRColocationSession.StartDiscoveryAsync();
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