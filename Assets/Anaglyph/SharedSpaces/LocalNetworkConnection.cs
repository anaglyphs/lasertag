using System;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
    public class LocalNetworkConnection : MonoBehaviour
    {
		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport) NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		private void Start()
		{
			manager.OnClientStarted += OnClientStarted;
			manager.OnClientStopped += OnClientStopped;

			OVRColocationSession.StartDiscoveryAsync().ContinueWith(result =>
			{
				if (!result.Success)
					Debug.LogError("Failed to start colocation session discovery");

				OVRColocationSession.ColocationSessionDiscovered += OnColocationSessionDiscovered;
			});
		}

		private void OnDestroy()
		{
			if (manager != null)
			{
				manager.OnClientStarted -= OnClientStarted;
				manager.OnClientStopped -= OnClientStopped;
			}

			OVRColocationSession.ColocationSessionDiscovered -= OnColocationSessionDiscovered;
		}

		private void OnClientStarted()
		{
			OVRColocationSession.StopDiscoveryAsync();

			if(manager.IsHost)
			{
				var address = transport.ConnectionData.Address;

				OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(address)).ContinueWith(result =>
				{
					if(!result.Success)
						Debug.LogError("Failed to start colocation session advertisement");
				});
			}
		}

		private void OnClientStopped(bool b)
		{
			OVRColocationSession.StopAdvertisementAsync();
			OVRColocationSession.StartDiscoveryAsync();
		}

		private void OnColocationSessionDiscovered(OVRColocationSession.Data data)
		{
			transport.ConnectionData.Address = Encoding.ASCII.GetString(data.Metadata);
			manager.StartClient();
		}
	}
}






//using NetworkDiscoveryUnity;
//using Unity.Netcode;
//using Unity.Netcode.Transports.UTP;
//using UnityEngine;

//namespace SharedSpaces
//{
//	public class LocalNetworkConnection : MonoBehaviour
//	{
//		private static NetworkDiscovery discovery => NetworkDiscovery.Instance;
//		private static NetworkManager manager => NetworkManager.Singleton;
//		private static UnityTransport transport => (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

//		private void Start()
//		{
//			manager.OnServerStarted += OnServerStarted;
//			manager.OnServerStopped += OnServerStopped;

//			discovery.onReceivedServerResponse.AddListener(OnReceivedServerResponse);
//		}

//		private void OnDestroy()
//		{

//			if (manager != null)
//			{
//				manager.OnServerStarted -= OnServerStarted;
//				manager.OnServerStopped -= OnServerStopped;
//			}

//			if (discovery != null)
//			{
//				discovery.onReceivedServerResponse.RemoveListener(OnReceivedServerResponse);
//			}
//		}

//		private void OnServerStarted()
//		{
//			discovery.EnsureServerIsInitialized();
//		}

//		private void OnServerStopped(bool b)
//		{
//			discovery.CloseServerUdpClient();
//		}

//		private void OnReceivedServerResponse(NetworkDiscovery.DiscoveryInfo info)
//		{
//			if (manager.IsConnectedClient) return;

//			transport.SetConnectionData(info.EndPoint.Address.ToString(), 25001);
//			NetworkManager.Singleton.StartClient();
//			manager.StartClient();
//		}
//	}
//}
