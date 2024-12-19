using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SharedSpaces
{
	public class ActiveOnlyIfConnected : MonoBehaviour
	{

		private void Start()
		{
			NetworkManager.Singleton.OnConnectionEvent += OnConnectionEvent;
		}

		private void OnDestroy()
		{
			if(NetworkManager.Singleton != null)
				NetworkManager.Singleton.OnConnectionEvent -= OnConnectionEvent;
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData eventData)
		{
			if (eventData.EventType == ConnectionEvent.ClientConnected)
				gameObject.SetActive(true);
			else if(eventData.EventType == ConnectionEvent.ClientDisconnected)
				gameObject.SetActive(false);
		}
	}
}
