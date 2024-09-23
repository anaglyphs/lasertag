using NetworkDiscoveryUnity;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace SharedSpaces
{
    public class LocalNetworkConnection : MonoBehaviour
    {
		private static NetworkDiscovery discovery => NetworkDiscovery.Instance;
		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport) NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		private void Start()
		{
			manager.OnServerStarted += OnServerStarted;
			manager.OnServerStopped += OnServerStopped;

			discovery.onReceivedServerResponse.AddListener(OnReceivedServerResponse);
		}

		private void OnDestroy()
		{

			if (manager != null)
			{
				manager.OnServerStarted -= OnServerStarted;
				manager.OnServerStopped -= OnServerStopped;
			}

			if (discovery != null)
			{
				discovery.onReceivedServerResponse.RemoveListener(OnReceivedServerResponse);
			}
		}

		private void OnServerStarted()
		{
			discovery.EnsureServerIsInitialized();
		}

		private void OnServerStopped(bool b)
		{
			discovery.CloseServerUdpClient();
		}

		private void OnReceivedServerResponse(NetworkDiscovery.DiscoveryInfo info)
		{
			if(manager.IsConnectedClient) return;

			transport.SetConnectionData(info.EndPoint.Address.ToString(), 25001);
			NetworkManager.Singleton.StartClient();
			manager.StartClient();
		}
	}
}
