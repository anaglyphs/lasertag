using Anaglyph.Menu;
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

			// homepage
			hostButton.onClick.AddListener(StartHost);

			// manually connect page

			manuallyConnectPage.showBackButton = true;

			ipField.onValueChanged.AddListener((string address) => transport.ConnectionData.Address = address);
			string ip = IpText.GetLocalIPAddress();
			int length = Mathf.Min(ip.Length, ip.LastIndexOf('.') + 1);
			ipField.text = ip.Substring(0, length);

			connectButton.onClick.AddListener(() => manager.StartClient());

			// connecting page
			connectingPage.showBackButton = false;
			connectingCancelButton.onClick.AddListener(() => manager.Shutdown());

			// joined page
			joinedPage.showBackButton = false;
			joinedDisconnectButton.onClick.AddListener(() => manager.Shutdown());

			// hosting page
			hostingPage.showBackButton = false;
			hostingStopButton.onClick.AddListener(() => manager.Shutdown());
		}

		private void OnDestroy()
		{
			if(manager != null)
				manager.OnConnectionEvent -= OnConnectionEvent;
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if(data.EventType == ConnectionEvent.ClientConnected)
			{

				if (manager.IsServer)
				{
					hostingPage.NavigateHere();
					hostingText.text = $"Hosting at {transport.ConnectionData.Address}";
				}
				else
				{
					connectingPage.NavigateHere();
					connectingText.text = $"Trying to connect to {transport.ConnectionData.Address}";
				}

			} 
			else if(data.EventType == ConnectionEvent.ClientDisconnected)
			{

				homePage.NavigateHere();

			}
        }

		private void StartHost()
		{
			transport.SetConnectionData(GetLocalIPv4(), Port, Listen);
			manager.StartHost();
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
