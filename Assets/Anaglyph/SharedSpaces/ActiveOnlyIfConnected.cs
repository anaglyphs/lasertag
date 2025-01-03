using Anaglyph.SharedSpaces;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.SharedSpaces
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

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (NetcodeHelpers.ThisClientConnected(data))
				gameObject.SetActive(true);
			else if(NetcodeHelpers.ThisClientDisconnected(data))
				gameObject.SetActive(false);
		}
	}
}
