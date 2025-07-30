using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Netcode
{
    public class InteractiveIfConnected : MonoBehaviour
    {
		private Selectable selectable;
		public bool interactableIfDisconnected;

		private async void Awake()
		{
			selectable = GetComponent<Selectable>();

			await Awaitable.EndOfFrameAsync();

			NetworkManager.Singleton.OnConnectionEvent += OnConnectionEvent;
			selectable.interactable = NetworkManager.Singleton.IsConnectedClient || interactableIfDisconnected;
		}

		private void OnDestroy()
		{
			if (NetworkManager.Singleton != null)
				NetworkManager.Singleton.OnConnectionEvent -= OnConnectionEvent;
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (NetcodeHelpers.ThisClientConnected(data))
				selectable.interactable = !interactableIfDisconnected;
			else if (NetcodeHelpers.ThisClientDisconnected(data))
				selectable.interactable = interactableIfDisconnected;
		}
	}
}
