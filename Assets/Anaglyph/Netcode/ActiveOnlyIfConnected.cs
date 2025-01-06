using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode
{
	public class ActiveOnlyIfConnected : MonoBehaviour
	{

		private void Start()
		{
			NetworkManager.Singleton.OnConnectionEvent += OnConnectionEvent;

			gameObject.SetActive(NetworkManager.Singleton.IsConnectedClient);
		}

		private void OnDestroy()
		{
			if(NetworkManager.Singleton != null)
				NetworkManager.Singleton.OnConnectionEvent -= OnConnectionEvent;
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (NetcodeHelpers.ThisClientConnected(data))
				gameObject.SetActive(true);
			else if(NetcodeHelpers.ThisClientDisconnected(data))
				gameObject.SetActive(false);
		}
	}
}
