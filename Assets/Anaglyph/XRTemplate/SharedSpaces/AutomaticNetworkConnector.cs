using Anaglyph.Netcode;
using System;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	public class AutomaticNetworkConnector : MonoBehaviour
	{
		public static AutomaticNetworkConnector Instance { get; private set; }

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport) NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		private static string LogHeader = "[AutoJoiner] ";
		private static void Log(string str) => Debug.Log(LogHeader + str);

		private static string IPPrefix = "IP:";
		private static string RelayPrefix = "Relay:";

		private void Start()
		{
			manager.OnClientStarted += OnClientStarted;
			manager.OnClientStopped += OnClientStopped;
			OVRColocationSession.ColocationSessionDiscovered += HandleColocationSessionDiscovered;

			HandleChange();
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
		}

		private void OnDestroy()
		{
			if (manager != null)
			{
				manager.OnClientStarted -= OnClientStarted;
				manager.OnClientStopped -= OnClientStopped;
			}

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

				if (manager.IsHost && manager.IsListening)
					HostingStarted();
				else
					HostingStopped();

				if (manager.IsListening)
					ClientStarted();
				else
					ClientStopped();
			}
		}

		private async void HostingStarted()
		{
			string message = "";

			switch(transport.Protocol)
			{
				case UnityTransport.ProtocolType.UnityTransport:
					string address = transport.ConnectionData.Address;
					message = IPPrefix + address;
					break;

				case UnityTransport.ProtocolType.RelayUnityTransport:
					Guid allocationId = NetworkHelper.allocationId;
					var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocationId);
					message = RelayPrefix + joinCode;
					break;
			}

			Log($"Starting advertisement {message}");
			await OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(message));
		}

		private void HostingStopped()
		{
			Log("Stopping advertisement");
			OVRColocationSession.StopAdvertisementAsync();
		}

		private void ClientStarted()
		{
			Log("Stopping discovery");
			OVRColocationSession.StopDiscoveryAsync();
		}

		private void ClientStopped()
		{
			Log("Starting discovery");
			OVRColocationSession.StartDiscoveryAsync();
		}

		private void HandleColocationSessionDiscovered(OVRColocationSession.Data data)
		{
			string message = Encoding.ASCII.GetString(data.Metadata);
			Log($"Discovered {message}");

			if(message.StartsWith(IPPrefix))
			{
				NetworkHelper.StartClientWithIP(message.Remove(0, IPPrefix.Length));
			} else if(message.StartsWith(RelayPrefix))
			{
				NetworkHelper.StartClientWithRelayCode(message.Remove(0, RelayPrefix.Length));
			}
		}
	}
}