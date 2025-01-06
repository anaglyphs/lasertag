using Anaglyph.Menu;
using Anaglyph.Netcode;
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

		[Header(nameof(homePage))]
		[SerializeField] private NavPage homePage;
		[SerializeField] private Button hostButton;

		[Header(nameof(manuallyConnectPage))]
		[SerializeField] private NavPage manuallyConnectPage;
		[SerializeField] private InputField ipField;
		[SerializeField] private Button connectButton;

		[Header(nameof(connectingPage))]
		[SerializeField] private NavPage connectingPage;
		[SerializeField] private Text connectingText;
		[SerializeField] private Button connectingCancelButton;

		[Header(nameof(joinedPage))]
		[SerializeField] private NavPage joinedPage;
		[SerializeField] private Text joinedText;
		[SerializeField] private Button joinedDisconnectButton;

		[Header(nameof(hostingPage))]
		[SerializeField] private NavPage hostingPage;
		[SerializeField] private Text hostingText;
		[SerializeField] private Button hostingStopButton;

		private void Start()
		{
			manager = NetworkManager.Singleton;
			manager.TryGetComponent(out transport);

			manager.OnConnectionEvent += OnConnectionEvent;
			manager.OnClientStarted += OnClientStarted;

			// homepage
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
					hostingText.text = $"Hosting at {transport.ConnectionData.Address}";
				}
				else
				{
					joinedPage.NavigateHere();
					joinedText.text = $"Joined {transport.ConnectionData.Address}";
				}
			} else if(NetcodeHelpers.ThisClientDisconnected(data))
			{
				homePage.NavigateHere();
			}
        }

		private void OnClientStarted()
		{
			if (!manager.IsHost)
				connectingPage.NavigateHere();
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
