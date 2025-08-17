using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode
{
	public class ActiveIfConnected : MonoBehaviour
	{
		public bool invert;

		private async void Start()
		{
			NetworkManager.Singleton.OnConnectionEvent += OnConnectionEvent;
			await Awaitable.EndOfFrameAsync();
			gameObject.SetActive(NetworkManager.Singleton.IsConnectedClient ^ invert);
		}

		private void OnDestroy()
		{
			if (NetworkManager.Singleton != null)
				NetworkManager.Singleton.OnConnectionEvent -= OnConnectionEvent;
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			gameObject.SetActive(NetworkManager.Singleton.IsConnectedClient ^ invert);
		}
	}
}