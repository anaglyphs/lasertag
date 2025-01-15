using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	public class AutomaticGameJoiner : SingletonBehavior<AutomaticGameJoiner>
	{
		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport) NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		private static string LogHeader = "[AutoJoiner] ";
		private static void Log(string str) => Debug.Log(LogHeader + str);

		public bool autoJoin = true;

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

		protected override void SingletonAwake()
		{

		}

		protected override void OnSingletonDestroy()
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

			if (!enabled)
			{
				ClientStopped();
				HostingStopped();
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
			OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(address));
			Log("Started advertisement " + address);
		}

		private void HostingStopped()
		{
			OVRColocationSession.StopAdvertisementAsync();
			Log("Stopped advertisement");
		}

		private void ClientStarted()
		{
			OVRColocationSession.StopDiscoveryAsync();
			Log("Stopped discovery");
		}

		private void ClientStopped()
		{
			OVRColocationSession.StartDiscoveryAsync();
			Log("Started discovery");
		}

		private void HandleColocationSessionDiscovered(OVRColocationSession.Data data)
		{
			if (!autoJoin)
				return;

			transport.ConnectionData.Address = Encoding.ASCII.GetString(data.Metadata);
			manager.StartClient();
		}
	}
}