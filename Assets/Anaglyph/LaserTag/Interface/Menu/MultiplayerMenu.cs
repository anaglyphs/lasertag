using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.SharedSpaces;
using Anaglyph.XRTemplate.SharedSpaces;
using System.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class MultiplayerMenu : MonoBehaviour
	{
		private enum SessionState
		{
			Connecting,
			Colocating,
			Connected,
		}

		private NetworkManager manager;
		private UnityTransport transport;

		[SerializeField] private NavPagesParent navView;

		[Header(nameof(homePage))]
		[SerializeField] private NavPage homePage = null;
		[SerializeField] private Button hostButton = null;

		[Header(nameof(manuallyConnectPage))]
		[SerializeField] private NavPage manuallyConnectPage = null;
		[SerializeField] private InputField ipField = null;
		[SerializeField] private Button connectButton = null;

		[Header(nameof(sessionPage))]
		[SerializeField] private NavPage sessionPage = null;
		[SerializeField] private Image sessionIcon = null;
		[SerializeField] private Text sessionStateText = null;
		[SerializeField] private Text sessionIpText = null;
		[SerializeField] private Button disconnectButton = null;

		[Header("Session icons")]
		[SerializeField] private Sprite connectingSprite = null;
		[SerializeField] private Sprite colocatingSprite = null;
		[SerializeField] private Sprite connectedSprite = null;
		[SerializeField] private Sprite hostingSprite = null;

		private void Start()
		{
			manager = NetworkManager.Singleton;
			manager.TryGetComponent(out transport);

			manager.OnConnectionEvent += OnConnectionEvent;
			manager.OnClientStarted += OnClientStarted;

			navView.onPageChange.AddListener(delegate (NavPage page)
			{
				if (page == manuallyConnectPage)
				{
					AutomaticNetworkConnector.Instance.enabled = false;
				} else
				{
					AutomaticNetworkConnector.Instance.enabled = true;
				}
			});

			// home page
			hostButton.onClick.AddListener(Host);

			// manually connect page
			manuallyConnectPage.showBackButton = true;

			string ip = GetLocalIPv4();
			int length = Mathf.Min(ip.Length, ip.LastIndexOf('.') + 1);
			ipField.text = ip.Substring(0, length);

#if UNITY_EDITOR
			ipField.text = "127.0.0.1";
#endif
			connectButton.onClick.AddListener(() => Join(ipField.text));

			// connecting page
			sessionPage.showBackButton = false;
			disconnectButton.onClick.AddListener(Disconnect);

			manager.OnClientStopped += OnClientStopped;

			Colocation.IsColocatedChange += OnColocationChange;
		}

		private void OnDestroy()
		{
			if (manager != null)
			{
				manager.OnConnectionEvent -= OnConnectionEvent;
				manager.OnClientStarted -= OnClientStarted;
			}

			Colocation.IsColocatedChange -= OnColocationChange;
		}

		private void OnClientStarted()
		{
			OpenSessionPage(SessionState.Connecting);
		}

		private void OnClientStopped(bool wasHost)
		{
			homePage.NavigateHere();
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (NetcodeHelpers.ThisClientConnected(data))
				OpenSessionPage(SessionState.Colocating);
		}

		private void OnColocationChange(bool isColocated)
		{
			if(isColocated)
				OpenSessionPage(SessionState.Connected);
		}

		private void OpenSessionPage(SessionState state)
		{
			sessionIpText.text = transport.ConnectionData.Address;

			switch (state)
			{
				case SessionState.Connecting:
					sessionStateText.text = "Connecting...";
					sessionIcon.sprite = connectingSprite;
					break;

				case SessionState.Colocating:
					sessionStateText.text = "Colocating...";
					sessionIcon.sprite = colocatingSprite;
					break;

				case SessionState.Connected:
					if(manager.IsHost)
					{
						sessionStateText.text = "Hosting";
						sessionIcon.sprite = hostingSprite;
					} else
					{
						sessionStateText.text = "Connected!";
						sessionIcon.sprite = connectedSprite;
					}
					break;
			}

			sessionPage.NavigateHere();
		}

		private void Host()
		{
			manager.Shutdown();
			transport.ConnectionData.Address = GetLocalIPv4();
			manager.StartHost();
		}

		private void Join(string ip)
		{
			manager.Shutdown();
			transport.ConnectionData.Address = ip;
			manager.StartClient();
		}

		private void Disconnect()
		{
			homePage.NavigateHere();
			manager.Shutdown();
		}

		private string GetLocalIPv4()
		{
			var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
			foreach(var address in addresses)
				if(address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
					return address.ToString();

			return null;
		}
	}
}
