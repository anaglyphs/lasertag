using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class MainMenu : MonoBehaviour
	{
		[SerializeField] private MenuPositioner menuPositioner;

		[SerializeField] private GameObject[] onlyVisibleIfConnected = null;
		[SerializeField] private GameObject[] menusOnlyVisibleIfConnected = null;
		[SerializeField] private GameObject fallbackMenuOnDisconnect = null;

		private NetworkManager manager;

		private void Start()
		{
			manager = NetworkManager.Singleton;

			if (manager == null)
				return;

			manager.OnConnectionEvent += OnConnectionEvent;

			UpdateVisibilityOfNetworkOnlyObjects(manager.IsConnectedClient || manager.IsHost);

			Colocation.IsColocatedChange += HandleColocation;
		}

		private void OnDestroy()
		{
			Colocation.IsColocatedChange -= HandleColocation;
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (NetcodeHelpers.ThisClientConnected(data))
			{
				UpdateVisibilityOfNetworkOnlyObjects(true);
			}
			else if (NetcodeHelpers.ThisClientDisconnected(data))
			{
				UpdateVisibilityOfNetworkOnlyObjects(false);

				foreach (GameObject menu in menusOnlyVisibleIfConnected)
				{
					if (menu.activeSelf)
					{
						fallbackMenuOnDisconnect.SetActive(true);
						break;
					}
				}

				menuPositioner.SetVisible(true);
			}
		}

		private void HandleColocation(bool b)
		{
			if (Colocation.IsColocated && manager.IsConnectedClient)
				menuPositioner.SetVisible(false);
		}

		private void UpdateVisibilityOfNetworkOnlyObjects(bool visible)
		{
			foreach(var obj in onlyVisibleIfConnected)
				obj.SetActive(visible);
		}
	}
}
