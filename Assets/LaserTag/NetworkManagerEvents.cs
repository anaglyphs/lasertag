using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace SharedSpacesXR
{
	public class NetworkManagerEvents : SuperAwakeBehavior
	{
		public UnityEvent OnClientStarted = new();
		public UnityEvent OnServerStarted = new();
		public UnityEvent OnHostStarted = new();


		public UnityEvent OnConnect = new();

		public UnityEvent<bool> OnClientStopped = new();
		public UnityEvent<bool> OnServerStopped = new();

		private NetworkManager networkManager;

		private void CheckForHost()
		{
			if (NetworkManager.Singleton.IsHost)
			{
				OnHostStarted.Invoke();
			}
		}

		protected override void SuperAwake()
		{
			networkManager = NetworkManager.Singleton;

			if(networkManager == null)
				networkManager = FindObjectOfType<NetworkManager>(true);

			networkManager.OnClientStarted += OnClientStarted.Invoke;
			networkManager.OnServerStarted += OnServerStarted.Invoke;
			networkManager.OnClientStarted += CheckForHost;
			networkManager.OnConnectionEvent += HandleConnectionEvent;

			networkManager.OnClientStopped += OnClientStopped.Invoke;
			networkManager.OnServerStopped += OnServerStopped.Invoke;
		}

		private void HandleConnectionEvent(NetworkManager manager, ConnectionEventData eventData)
		{
			if(eventData.EventType == ConnectionEvent.ClientConnected)
			{
				OnConnect.Invoke();
			}
		}

		private void OnDestroy()
		{
			if (networkManager == null)
				return;

			networkManager.OnClientStarted -= OnClientStarted.Invoke;
			networkManager.OnServerStarted -= OnServerStarted.Invoke;
			networkManager.OnClientStarted -= CheckForHost;
			networkManager.OnConnectionEvent -= HandleConnectionEvent;

			networkManager.OnClientStopped -= OnClientStopped.Invoke;
			networkManager.OnServerStopped -= OnServerStopped.Invoke;
		}
	}
}