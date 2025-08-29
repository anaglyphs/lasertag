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

		private static NetworkManager manager => NetworkManager.Singleton;

		[SerializeField] private BoolObject useUnityRelayService;

		[SerializeField] private NavPagesParent navView;

		[Header(nameof(homePage))]
		[SerializeField] private NavPage homePage = null;
		[SerializeField] private Button hostButton = null;

		[Header(nameof(manuallyConnectPage))]
		[SerializeField] private NavPage manuallyConnectPage = null;
		[SerializeField] private InputField ipField = null;
		[SerializeField] private Toggle useRelayToggle = null;
		[SerializeField] private Button connectButton = null;

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
			NetcodeHelper.StateChange += IsNetworkRunningChanged;

			// home page
			hostButton.onClick.AddListener(Host);

			// manually connect page
			manuallyConnectPage.showBackButton = true;

			string ip = NetcodeHelpers.GetLocalIPv4();

#if UNITY_EDITOR
			ip = "127.0.0.1";
#endif

			int length = Mathf.Min(ip.Length, ip.LastIndexOf('.') + 1);
			ipField.text = ip.Substring(0, length);

			connectButton.onClick.AddListener(delegate {
				if(useRelayToggle)
					NetcodeHelper.ConnectUnityServices(ipField.text);
				else
					NetcodeHelper.ConnectLAN(ipField.text);
			});

			// connecting page
			sessionPage.showBackButton = false;
			disconnectButton.onClick.AddListener(Disconnect);

			hostRespawnAnchorButton.onClick.AddListener(RespawnAnchor);

			Colocation.IsColocatedChange += OnColocationChange;

			ShowAnchorOptions(false);
		}

		private void OnDestroy()
		{
			NetcodeHelper.StateChange -= IsNetworkRunningChanged;
			Colocation.IsColocatedChange -= OnColocationChange;
		}

		private void IsNetworkRunningChanged(NetcodeHelper.NetworkState state)
		{
			switch (state)
			{
				case NetcodeHelper.NetworkState.Disconnected:
					sessionIpText.text = "";
					homePage.NavigateHere();
					break;

				case NetcodeHelper.NetworkState.Connecting:
					UpdateIpText();
					OpenSessionPage(SessionState.Connecting);
					break;

				case NetcodeHelper.NetworkState.Connected:
					UpdateIpText();
					OnColocationChange(Colocation.IsColocated);
					break;
			}
		}

		private void UpdateIpText()
		{
			NetworkTransport transport = manager.NetworkConfig.NetworkTransport;
			Type transportType = transport.GetType();

			if (string.Equals(transportType.Name, "DistributedAuthorityTransport"))
			{
				sessionIpText.text = $"Relay: {NetcodeHelper.CurrentSessionName}";
			}
			else if (transport.GetType() == typeof(UnityTransport))
			{
				sessionIpText.text = ((UnityTransport)transport).ConnectionData.Address;
			}
		}

		private void RespawnAnchor()
		{
			MetaAnchorColocator.Current.InstantiateNewAnchor();
		}

		private void OnColocationChange(bool isColocated)
		{
			if(NetcodeHelper.State == NetcodeHelper.NetworkState.Connected)
				OpenSessionPage(Colocation.IsColocated ? SessionState.Connected : SessionState.Colocating);
		}

		private void OpenSessionPage(SessionState state)
		{
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


		private void ShowAnchorOptions(bool b)
		{
			hostRespawnAnchorButton.gameObject.SetActive(b);
			hostRespawnAnchorLabel.gameObject.SetActive(b);
		}

		private void Host()
		{
			var service = useUnityRelayService.Value ? NetcodeHelper.Protocol.UnityService : NetcodeHelper.Protocol.LAN;
			NetcodeHelper.Host(service);
		}

		private void Disconnect()
		{
			NetcodeHelper.Disconnect();
		}
	}
}
