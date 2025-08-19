using Anaglyph.Netcode;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AutomaticNetworkConnector : MonoBehaviour
	{
		public static AutomaticNetworkConnector Instance { get; private set; }

		private static NetworkManager manager => NetworkManager.Singleton;
		private static IMultiplayerService service => MultiplayerService.Instance;
		private static UnityTransport transport => (UnityTransport) NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		private static string LogHeader = "[AutoJoiner] ";
		private static void Log(string str) => Debug.Log(LogHeader + str);

		private static string LanPrefix = "IP:";
		private static string RelayPrefix = "Relay:";

		private async void Start()
		{
			manager.OnClientStarted += OnClientStarted;
			manager.OnConnectionEvent += OnConnectionEvent;
			manager.OnClientStopped += OnClientStopped;

			OVRColocationSession.ColocationSessionDiscovered += HandleColocationSessionDiscovered;

			Log("Starting discovery");
			await OVRColocationSession.StartDiscoveryAsync();
		}

		private void OnDestroy()
		{
			if (manager != null)
			{
				manager.OnClientStarted -= OnClientStarted;
				manager.OnConnectionEvent -= OnConnectionEvent;
				manager.OnClientStopped -= OnClientStopped;
			}

			OVRColocationSession.ColocationSessionDiscovered -= HandleColocationSessionDiscovered;
		}

		private async void OnClientStarted()
		{
			Log("Stopping discovery");
			await OVRColocationSession.StopDiscoveryAsync();
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData eventData)
		{
			if (NetworkHelper.ThisClientConnected(eventData))
				OnClientConnected();
		}

		private async void OnClientConnected()
		{
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

			Log($"Starting advertisement {message}");
			await OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(message));
		}

		private async void OnClientStopped(bool isHost)
		{
			Log("Stopping advertisement");
			await OVRColocationSession.StopAdvertisementAsync();

			await Awaitable.WaitForSecondsAsync(5);

			if (!manager.IsListening)
			{
				Log("Starting discovery");
				await OVRColocationSession.StartDiscoveryAsync();
			}
		}

#if !UNITY_EDITOR
		private void OnApplicationFocus(bool focus) => OnApplicationFocusChanged();
		private void OnApplicationPause(bool pause) => OnApplicationFocusChanged();
#endif

		private async void OnApplicationFocusChanged()
		{
			if (Application.isFocused)
			{
				if (manager.IsConnectedClient)
					OnClientConnected();
				else
					await OVRColocationSession.StartDiscoveryAsync();
			} else
			{
				if (manager.IsConnectedClient)
					await OVRColocationSession.StopAdvertisementAsync();
				else
					await OVRColocationSession.StopDiscoveryAsync();
			}
		}

		private async void HandleColocationSessionDiscovered(OVRColocationSession.Data data)
		{
			string message = Encoding.ASCII.GetString(data.Metadata);
			Log($"Discovered {message}");

			if (NetworkManager.Singleton.IsListening)
				return;

			if(message.StartsWith(LanPrefix))
			{
				NetworkHelper.ConnectLAN(message.Remove(0, LanPrefix.Length));
			} else if(message.StartsWith(RelayPrefix))
			{
				await NetworkHelper.ConnectUnityServices(message.Remove(0, RelayPrefix.Length));
			}
		}
	}
}