using Anaglyph.Netcode;
using System;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Multiplayer;
using Unity.Services.Relay;
using UnityEngine;
using Unity.Services.DistributedAuthority;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AutomaticNetworkConnector : MonoBehaviour
	{
		public static AutomaticNetworkConnector Instance { get; private set; }

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport) NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		private static string LogHeader = "[AutoJoiner] ";
		private static void Log(string str) => Debug.Log(LogHeader + str);

		private static string LanPrefix = "IP:";
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

				if (manager.IsListening)
					ClientStarted();
				else
					ClientStopped();
			}
		}

		private async void ClientStarted()
		{
			Log("Stopping discovery");
			await OVRColocationSession.StopDiscoveryAsync();

			string message = "";

			switch (transport.Protocol)
			{
				case UnityTransport.ProtocolType.UnityTransport:

					string address = transport.ConnectionData.Address;
					message = LanPrefix + address;
					break;

				case UnityTransport.ProtocolType.RelayUnityTransport:

					var sessionIds = await MultiplayerService.Instance.GetJoinedSessionIdsAsync();
					message = RelayPrefix + sessionIds[sessionIds.Count - 1];

					break;
			}

			if (manager.IsHost || transport.Protocol == UnityTransport.ProtocolType.RelayUnityTransport)
			{
				Log($"Starting advertisement {message}");
				await OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(message));
			}
		}

		private async void ClientStopped()
		{
			Log("Stopping advertisement");
			await OVRColocationSession.StopAdvertisementAsync();

			await Awaitable.WaitForSecondsAsync(0.5f);

			Log("Starting discovery");
			await OVRColocationSession.StartDiscoveryAsync();
		}

		private void HandleColocationSessionDiscovered(OVRColocationSession.Data data)
		{
			if (NetworkManager.Singleton.IsListening)
				return;

			string message = Encoding.ASCII.GetString(data.Metadata);
			Log($"Discovered {message}");

			if(message.StartsWith(LanPrefix))
			{
				NetworkHelper.ConnectLAN(message.Remove(0, LanPrefix.Length));
			} else if(message.StartsWith(RelayPrefix))
			{
				NetworkHelper.ConnectUnityServices(message.Remove(0, RelayPrefix.Length));
			}
		}
	}
}