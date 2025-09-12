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

			NetcodeManagement.StateChange += OnNetcodeStateChanged;
			OnNetcodeStateChanged(NetcodeManagement.State);
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChange -= OnNetcodeStateChanged;
		}

		private void OnNetcodeStateChanged(NetcodeManagement.NetworkState state)
		{
			if (state == NetcodeManagement.NetworkState.Connecting)
				menuPositioner.SetVisible(false);
			else if(state == NetcodeManagement.NetworkState.Disconnected)
				menuPositioner.SetVisible(true);

			bool isConnected = state == NetcodeManagement.NetworkState.Connected;

			foreach (var obj in onlyVisibleIfConnected)
				obj.SetActive(isConnected);

			if(!isConnected)
			{
				foreach (GameObject menu in menusOnlyVisibleIfConnected)
				{
					if (menu.activeSelf)
					{
						fallbackMenuOnDisconnect.SetActive(true);
						break;
					}
				}
			}
		}
	}
}
