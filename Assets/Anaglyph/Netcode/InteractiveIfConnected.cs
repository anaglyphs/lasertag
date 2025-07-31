using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Netcode
{
    public class InteractiveIfConnected : MonoBehaviour
    {
		private Selectable selectable;
		public bool invert;

		private NetworkManager networkManager => NetworkManager.Singleton;

		private void Awake()
		{
			selectable = GetComponent<Selectable>();
		}

		private void OnEnable()
		{
			if(didStart)
				HandleChange();
		}

		private void Start()
		{
			networkManager.OnConnectionEvent += OnConnectionEvent;
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
			selectable.interactable = isConnected ^ invert;
		}
	}
}
