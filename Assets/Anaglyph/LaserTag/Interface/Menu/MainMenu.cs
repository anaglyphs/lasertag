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

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			OnNetcodeStateChanged(NetcodeManagement.State);
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			if (state == NetcodeState.Connecting)
				menuPositioner.SetVisible(false);
			else if(state == NetcodeState.Disconnected)
				menuPositioner.SetVisible(true);
				
			bool isConnected = state == NetcodeState.Connected;

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
