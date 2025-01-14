using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.SharedSpaces;
using System.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class MultiplayerMenu : MonoBehaviour
	{
		private const ushort Port = 25001;
		private const string Listen = "0.0.0.0";

		private NetworkManager manager;
		private UnityTransport transport;

		private MenuPositioner menuPositioner;

		[Header(nameof(homePage))]
		[SerializeField] private NavPage homePage = null;
		[SerializeField] private Button hostButton = null;

		[Header(nameof(manuallyConnectPage))]
		[SerializeField] private NavPage manuallyConnectPage = null;
		[SerializeField] private InputField ipField = null;
		[SerializeField] private Button connectButton = null;

		[Header(nameof(connectingPage))]
		[SerializeField] private NavPage connectingPage = null;
		[SerializeField] private Text connectingText = null;
		[SerializeField] private Button connectingCancelButton = null;

		[Header(nameof(joinedPage))]
		[SerializeField] private NavPage joinedPage = null;
		[SerializeField] private Text joinedText = null;
		[SerializeField] private Button joinedDisconnectButton = null;

		[Header(nameof(hostingPage))]
		[SerializeField] private NavPage hostingPage = null;
		[SerializeField] private Text hostingText = null;
		[SerializeField] private Button hostingStopButton = null;

		private void Start()
		{
			manager = NetworkManager.Singleton;
			manager.TryGetComponent(out transport);

			menuPositioner = GetComponentInParent<MenuPositioner>(true);

			manager.OnConnectionEvent += OnConnectionEvent;
			manager.OnClientStarted += OnClientStarted;

			// homepage
			homePage.OnVisible.AddListener((bool v) => AutomaticGameJoiner.Instance.autoJoin = v);
			hostButton.onClick.AddListener(Host);


			// manually connect page

			manuallyConnectPage.showBackButton = true;

			string ip = IpText.GetLocalIPAddress();
			int length = Mathf.Min(ip.Length, ip.LastIndexOf('.') + 1);
			ipField.text = ip.Substring(0, length);

#if UNITY_EDITOR
			ipField.text = "127.0.0.1";
#endif

			connectButton.onClick.AddListener(() => Join(ipField.text));

			// connecting page
			connectingPage.showBackButton = false;
			connectingCancelButton.onClick.AddListener(Disconnect);

			// joined page
			joinedPage.showBackButton = false;
			joinedDisconnectButton.onClick.AddListener(Disconnect);

			// hosting page
			hostingPage.showBackButton = false;
			hostingStopButton.onClick.AddListener(Disconnect);
		}

		//// I wish I didn't have to poll for this
		//private bool wasListening;
		//private void Update()
		//{
		//	if (manager == null) return;

		//	if (manager.IsListening && !wasListening && !manager.IsHost)
		//	{
		//		connectingPage.NavigateHere();
		//		connectingText.text = $"Trying to connect to {transport.ConnectionData.Address}";
		//	}
		//	else if (!manager.IsListening && wasListening)
		//	{
		//		homePage.NavigateHere();
		//	}

		//	wasListening = manager.IsListening;
		//}

		private void OnDestroy()
		{
			if (manager != null)
			{
				manager.OnConnectionEvent -= OnConnectionEvent;
				manager.OnClientStarted -= OnClientStarted;
			}
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if(NetcodeHelpers.ThisClientConnected(data))
			{
				if (manager.IsHost)
				{
					hostingPage.NavigateHere();
					hostingText.text = $"IP address: {transport.ConnectionData.Address}";
				}
				else
				{
					joinedPage.NavigateHere();
					joinedText.text = $"IP address: {transport.ConnectionData.Address}";
				}

				menuPositioner.SetVisible(false);

			} else if(NetcodeHelpers.ThisClientDisconnected(data))
			{
				homePage.NavigateHere();

				menuPositioner.SetVisible(true);
			}
        }

		private void OnClientStarted()
		{
			if (!manager.IsHost)
			{
				connectingText.text = $"IP address: {transport.ConnectionData.Address}";
				connectingPage.NavigateHere();
			}
		}

		private void Host()
		{
			manager.Shutdown();
			transport.SetConnectionData(GetLocalIPv4(), Port, Listen);
			manager.StartHost();
		}

		private void Join(string ip)
		{
			manager.Shutdown();
			transport.SetConnectionData(ip, Port);
			manager.StartClient();
		}

		private void Disconnect()
		{
			manager.Shutdown();
			homePage.NavigateHere();
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
