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

		[SerializeField] private NavPagesParent navView;

		[Header(nameof(homePage))]
		[SerializeField] private NavPage homePage = null;
		[SerializeField] private Button hostButton = null;

		[Header(nameof(manuallyConnectPage))]
		[SerializeField] private NavPage manuallyConnectPage = null;
		[SerializeField] private Toggle useRelayToggle = null;
		[SerializeField] private InputField ipField = null;
		[SerializeField] private InputField roomField = null;
		[SerializeField] private GameObject roomFieldLabel = null;
		[SerializeField] private Button connectButton = null;

		[Header(nameof(sessionPage))]
		[SerializeField] private NavPage sessionPage = null;
		[SerializeField] private Image sessionIcon = null;
		[SerializeField] private Text sessionStateText = null;
		[SerializeField] private Text sessionIpText = null;
		[SerializeField] private Button disconnectButton = null;
		[SerializeField] private Button recalibrateButton = null;

		[Header("Session icons")]
		[SerializeField] private Sprite connectingSprite = null;
		[SerializeField] private Sprite colocatingSprite = null;
		[SerializeField] private Sprite connectedSprite = null;
		[SerializeField] private Sprite hostingSprite = null;

		[Header("Host settings")]
		[SerializeField] private BoolObject hostRelay = null;

		private const string IpPrefsKey = "Ip";
		private const string RelayPrefsKey = "Relay";

		private void Start()
		{
			NetcodeManagement.StateChanged += IsNetworkRunningChanged;

			// home page
			hostButton.onClick.AddListener(Host);

			// manually connect page
			manuallyConnectPage.showBackButton = true;

			// #if UNITY_EDITOR
			//             ip = "127.0.0.1";
			// #endif

			// int length = Mathf.Min(ip.Length, ip.LastIndexOf('.') + 1);

			useRelayToggle.onValueChanged.AddListener(useRelay =>
			{
				if (useRelay)
				{
					ipField.gameObject.SetActive(false);
					roomField.gameObject.SetActive(true);
					roomFieldLabel.gameObject.SetActive(true);
				}
				else
				{
					ipField.gameObject.SetActive(true);
					roomField.gameObject.SetActive(false);
					roomFieldLabel.gameObject.SetActive(false);
				}
			});
			useRelayToggle.onValueChanged.Invoke(useRelayToggle.isOn);

			ipField.text = NetcodeManagement.GetLocalIPv4();

			connectButton.onClick.AddListener(delegate
			{
				if (useRelayToggle.isOn)
					NetcodeManagement.ConnectUnityServices(roomField.text);
				else
					NetcodeManagement.ConnectLAN(ipField.text);
			});

			// connecting page
			sessionPage.showBackButton = false;
			disconnectButton.onClick.AddListener(Disconnect);

			recalibrateButton.onClick.AddListener(RecalibrateColocation);

			Colocation.IsColocatedChange += OnColocationChange;

			recalibrateButton.gameObject.SetActive(false);
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= IsNetworkRunningChanged;
			Colocation.IsColocatedChange -= OnColocationChange;
		}

		private void IsNetworkRunningChanged(NetcodeState state)
		{
			switch (state)
			{
				case NetcodeState.Disconnected:
					sessionIpText.text = "";
					homePage.NavigateHere();
					break;

				case NetcodeState.Connecting:
					UpdateIpText();
					OpenSessionPage(SessionState.Connecting);
					break;

				case NetcodeState.Connected:
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
				sessionIpText.text = $"Relay: {NetcodeManagement.CurrentSessionName}";
			}
			else if (transport.GetType() == typeof(UnityTransport))
			{
				sessionIpText.text = ((UnityTransport)transport).ConnectionData.Address;
			}
		}

		// TODO: move to colocation manager
		private void RecalibrateColocation()
		{
			var type = Colocation.ActiveColocator.GetType();

			if (type == typeof(MetaAnchorColocator))
			{
				MetaAnchorColocator.Current.InstantiateNewAnchor();
			}
			else if (type == typeof(AprilTagColocator))
			{
				AprilTagColocator aprilTagColocator = (AprilTagColocator)Colocation.ActiveColocator;

				aprilTagColocator.NetworkObject.ChangeOwnership(manager.LocalClientId);

				aprilTagColocator.ClearCanonTagsRpc();
			}
		}

		private void OnColocationChange(bool isColocated)
		{
			if (NetcodeManagement.State == NetcodeState.Connected)
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
					sessionStateText.text = "Aligning...";
					sessionIcon.sprite = colocatingSprite;
					break;

				case SessionState.Connected:
					if (manager.CurrentSessionOwner == manager.LocalClientId)
					{
						sessionStateText.text = "Hosting";
						sessionIcon.sprite = hostingSprite;
					}
					else
					{
						sessionStateText.text = "Connected!";
						sessionIcon.sprite = connectedSprite;
					}
					break;
			}

			recalibrateButton.gameObject.SetActive(state != SessionState.Connecting);

			sessionPage.NavigateHere();
		}

		private void Host()
		{
			var service = hostRelay.Value ? NetcodeManagement.Protocol.UnityService : NetcodeManagement.Protocol.LAN;
			NetcodeManagement.Host(service);
		}

		private void Disconnect()
		{
			NetcodeManagement.Disconnect();
		}
	}
}
