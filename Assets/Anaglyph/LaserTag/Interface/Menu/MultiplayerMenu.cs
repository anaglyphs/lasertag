using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using VariableObjects;

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

		[SerializeField] private BoolObject useUnityRelayService;

		[SerializeField] private NavPagesParent navView;

		[Header(nameof(homePage))]
		[SerializeField] private NavPage homePage = null;
		[SerializeField] private Button hostButton = null;

		[Header(nameof(manuallyConnectPage))]
		[SerializeField] private NavPage manuallyConnectPage = null;
		[SerializeField] private InputField ipField = null;
		[SerializeField] private Button connectButton = null;
		[SerializeField] private InputField roomField = null;
		[SerializeField] private Button roomConnectButton = null;

		[Header(nameof(sessionPage))]
		[SerializeField] private NavPage sessionPage = null;
		[SerializeField] private Image sessionIcon = null;
		[SerializeField] private Text sessionStateText = null;
		[SerializeField] private Text sessionIpText = null;
		[SerializeField] private Button disconnectButton = null;
		[SerializeField] private Button hostRespawnAnchorButton = null;
		[SerializeField] private Text hostRespawnAnchorLabel = null;

		[Header("Session icons")]
		[SerializeField] private Sprite connectingSprite = null;
		[SerializeField] private Sprite colocatingSprite = null;
		[SerializeField] private Sprite connectedSprite = null;
		[SerializeField] private Sprite hostingSprite = null;

		private void Start()
		{
			manager = NetworkManager.Singleton;

			manager.OnConnectionEvent += OnConnectionEvent;
			manager.OnClientStarted += OnClientStarted;

			// home page
			hostButton.onClick.AddListener(Host);

			// manually connect page
			manuallyConnectPage.showBackButton = true;

			string ip = NetworkHelper.GetLocalIPv4();

#if UNITY_EDITOR
			ip = "127.0.0.1";
#endif

			int length = Mathf.Min(ip.Length, ip.LastIndexOf('.') + 1);
			ipField.text = ip.Substring(0, length);

			connectButton.onClick.AddListener(() => NetworkHelper.ConnectLAN(ipField.text));
			roomConnectButton.onClick.AddListener(() => NetworkHelper.StartOrJoinByNameAsync(roomField.text));

			// connecting page
			sessionPage.showBackButton = false;
			disconnectButton.onClick.AddListener(Disconnect);

			hostRespawnAnchorButton.onClick.AddListener(RespawnAnchor);

			manager.OnClientStopped += OnClientStopped;

			Colocation.IsColocatedChange += OnColocationChange;

			ShowAnchorOptions(false);
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
			if (NetworkHelper.ThisClientConnected(data))
				OpenSessionPage(SessionState.Colocating);
		}

		private void RespawnAnchor()
		{
			MetaAnchorColocator.Current.InstantiateNewAnchor();
		}

		private void OnColocationChange(bool isColocated)
		{
			if(isColocated)
				OpenSessionPage(SessionState.Connected);
		}

		private void ShowAnchorOptions(bool b)
		{
			hostRespawnAnchorButton.gameObject.SetActive(b);
			hostRespawnAnchorLabel.gameObject.SetActive(b);
		}

		private void OpenSessionPage(SessionState state)
		{
			sessionIpText.text = "";

			NetworkTransport transport = manager.NetworkConfig.NetworkTransport;
			Type transportType = transport.GetType();

			if (string.Equals(transportType.Name, "DistributedAuthorityTransport"))
			{
				sessionIpText.text = "Relay Server";
			} else if (transport.GetType() == typeof(UnityTransport))
			{
				sessionIpText.text = ((UnityTransport)transport).ConnectionData.Address;
			}

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
					if(manager.CurrentSessionOwner == manager.LocalClientId)
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

			bool shouldShowAnchorOptions = Colocation.IsColocated && 
				Colocation.ActiveColocator.GetType() == typeof(MetaAnchorColocator);
			ShowAnchorOptions(shouldShowAnchorOptions);

			sessionPage.NavigateHere();
		}

		private void Host()
		{
			OpenSessionPage(SessionState.Connecting);

			var service = useUnityRelayService.Value ? NetworkHelper.Protocol.UnityService : NetworkHelper.Protocol.LAN;

			NetworkHelper.Host(service);
		}

		private void Disconnect()
		{
			homePage.NavigateHere();

			NetworkHelper.Disconnect();
		}
	}
}
