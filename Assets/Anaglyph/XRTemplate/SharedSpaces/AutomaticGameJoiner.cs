using System.Net;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace Anaglyph.SharedSpaces
{
    public class AutomaticGameJoiner : SingletonBehavior<AutomaticGameJoiner>
    {
		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport) NetworkManager.Singleton.NetworkConfig.NetworkTransport;

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
				OVRColocationSession.StopDiscoveryAsync();
				OVRColocationSession.StopAdvertisementAsync();
			}
			else
			{
				if (manager == null)
					return;

				if (manager.IsHost)
				{
					string address = transport.ConnectionData.Address;
					OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(address));
				}
				else
				{
					OVRColocationSession.StopAdvertisementAsync();
				}

				if (manager.IsClient)
					OVRColocationSession.StopDiscoveryAsync();
				else
					OVRColocationSession.StartDiscoveryAsync();
			}
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