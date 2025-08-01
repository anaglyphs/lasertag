using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Netcode
{
	public class ActiveIfConnected : MonoBehaviour
	{
		public bool invert;

		private NetworkManager networkManager => NetworkManager.Singleton;

		private void OnEnable()
		{
			if (didStart)
				HandleChange();
		}

		private async void Start()
		{
			networkManager.OnConnectionEvent += OnConnectionEvent;
			await Awaitable.EndOfFrameAsync();
			HandleChange();
		}

		private void OnDestroy()
		{
			if (networkManager != null)
				networkManager.OnConnectionEvent -= OnConnectionEvent;
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			HandleChange();
		}

		private void HandleChange()
		{
			bool isConnected = networkManager != null && networkManager.IsConnectedClient;
			gameObject.SetActive(isConnected ^ invert);
		}
	}
}
