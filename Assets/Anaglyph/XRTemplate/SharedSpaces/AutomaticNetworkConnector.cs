using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
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

		private void HostingStarted()
		{
			string address = transport.ConnectionData.Address;
			Log($"Starting advertisement {address}");
			OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(address));
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
			string address = Encoding.ASCII.GetString(data.Metadata);
			Log($"Discovered {address}");
			transport.ConnectionData.Address = address;
			manager.StartClient();
		}
	}
}