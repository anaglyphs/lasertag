using System;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.VariableObjects;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	[DefaultExecutionOrder(100)]
	public class ConnectionMenu : MonoBehaviour
	{
		private enum SessionState
		{
			Connecting,
			Colocating,
			Connected
		}

		private static NetworkManager Manager => NetworkManager.Singleton;

		[SerializeField] private BoolObject hostOnRelaySetting;

		private NavView navView;
		private VisualElement multiplayerPanel;
		private NavPage homePage;
		private NavPage sessionPage;
		private NavPage networkErrorModal;
		private NavPage bluetoothErrorModal;
		private MenuErrorPresenter errors;
		private UIToolkitPanelXRSetup panel;
		private SessionDiscoveryController sessionDiscoveryController;

		private Toggle hostOnRelayToggle;
		private Label hostOnRelayWarning;
		private FlashingLabel sessionDiscoveryStatus;
		private string shownDiscoveryKey;

		private Toggle useRelayToggle;
		private VisualElement ipFieldRow;
		private TextField ipField;
		private VisualElement roomFieldRow;
		private TextField roomField;
		private bool useRelay;
		private string manualIp;
		private string manualRoom = "";

		private Label sessionStateText;
		private Label sessionIpText;

		private readonly ConnectionConnectivityMonitor connectivity = new();
		private readonly UIEventBindings bindings = new();

		private void InitializeUI()
		{
			bindings.Dispose();
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"ConnectionMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			multiplayerPanel = root.Q<VisualElement>(className: "multiplayer-panel");
			navView = NavView.RequireIn(root);
			homePage = navView.GetPage("home-page");
			sessionPage = navView.GetPage("session-page");
			networkErrorModal = navView.GetPage("network-error-modal");
			bluetoothErrorModal = navView.GetPage("bluetooth-error-modal");
			sessionDiscoveryStatus = Require<FlashingLabel>(root, "session-discovery-status");
			shownDiscoveryKey = null;

			bindings.Click(Require<Button>(root, "host-button"), Host);

			hostOnRelayToggle = Require<Toggle>(root, "host-on-relay-toggle");
			hostOnRelayWarning = Require<Label>(root, "host-on-relay-warning");

			bindings.Value(hostOnRelayToggle, change => hostOnRelaySetting.Value = change.newValue);

			useRelayToggle = Require<Toggle>(root, "use-relay-toggle");
			ipFieldRow = Require<VisualElement>(root, "ip-field-row");
			ipField = Require<TextField>(root, "ip-field");
			roomFieldRow = Require<VisualElement>(root, "room-field-row");
			roomField = Require<TextField>(root, "room-field");
			bindings.Value(useRelayToggle, change =>
			{
				useRelay = change.newValue;
				UpdateManualConnectionFields(useRelay);
			});
			bindings.Value(ipField, change => manualIp = change.newValue);
			bindings.Value(roomField, change => manualRoom = change.newValue);

			manualIp ??= NetcodeManagement.GetLocalIPv4();
			useRelayToggle.SetValueWithoutNotify(useRelay);
			ipField.SetValueWithoutNotify(manualIp);
			roomField.SetValueWithoutNotify(manualRoom);
			UpdateManualConnectionFields(useRelay);

			bindings.Click(Require<Button>(root, "connect-button"), Connect);

			sessionStateText = Require<Label>(root, "session-state");
			sessionIpText = Require<Label>(root, "session-address");
			bindings.Click(Require<Button>(root, "disconnect-button"), Disconnect);

			bindings.Click(Require<Button>(root, "dismiss-network-error-button"),
				() => navView.DismissModal(networkErrorModal));
			bindings.Click(Require<Button>(root, "open-wifi-settings-button"),
				ConnectionConnectivityMonitor.OpenWifiSettings);
			bindings.Click(Require<Button>(root, "dismiss-bluetooth-error-button"),
				() => navView.DismissModal(bluetoothErrorModal));
			bindings.Click(Require<Button>(root, "open-bluetooth-settings-button"),
				ConnectionConnectivityMonitor.OpenBluetoothSettings);

			navView.Changed += OnNavPageChange;
		}

		// Subscribed for the component's whole lifetime, not just while enabled —
		// an error raised while this panel is hidden still has to reach the user.
		private void Awake()
		{
			errors = new MenuErrorPresenter(MenuErrorArea.Connection);
			sessionDiscoveryController =
				GetComponentInParent<SessionDiscoveryController>();
			if (sessionDiscoveryController == null)
				throw new InvalidOperationException(
					"ConnectionMenu requires SessionConnectionController in its parent hierarchy.");
		}

		private void OnDestroy()
		{
			sessionDiscoveryController?.SetMenuAllowsListening(true);
			errors?.Dispose();
			connectivity.Dispose();
		}

		private void OnEnable()
		{
			InitializeUI();
			MenuCopy.Changed += OnCopyChanged;
			panel = GetComponent<UIToolkitPanelXRSetup>();
			if (panel == null)
				throw new InvalidOperationException(
					"ConnectionMenu requires UIToolkitPanelXRSetup on the same object.");

			panel.VisibleChanged += OnPanelVisibilityChanged;

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			ColocationManager.Colocated += OnColocationChange;
			hostOnRelaySetting.Changed += OnHostOnRelaySettingChange;
			RefreshSessionConnectionState();

			OnHostOnRelaySettingChange(hostOnRelaySetting.Value);
			OnNetcodeStateChanged(NetcodeManagement.State);
			connectivity.LanConnectionChanged += OnLanConnectionChanged;
			connectivity.InternetConnectionChanged += UpdateInternetWarnings;
			connectivity.BluetoothEnabledChanged += OnBluetoothEnabledChanged;
			connectivity.Start();

			errors.Bind(navView);
		}

		private void OnDisable()
		{
			errors.Unbind();
			MenuCopy.Changed -= OnCopyChanged;
			if (sessionDiscoveryStatus != null)
				sessionDiscoveryStatus.Flashing = false;

			connectivity.Stop();
			connectivity.LanConnectionChanged -= OnLanConnectionChanged;
			connectivity.InternetConnectionChanged -= UpdateInternetWarnings;
			connectivity.BluetoothEnabledChanged -= OnBluetoothEnabledChanged;
			bindings.Dispose();

			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			ColocationManager.Colocated -= OnColocationChange;
			hostOnRelaySetting.Changed -= OnHostOnRelaySettingChange;
			if (panel != null)
				panel.VisibleChanged -= OnPanelVisibilityChanged;
			sessionDiscoveryController?.SetMenuAllowsListening(true);

			if (navView != null)
			{
				navView.Changed -= OnNavPageChange;
				navView = null;
			}
		}

		private void OnCopyChanged()
		{
			shownDiscoveryKey = null;
			OnNetcodeStateChanged(NetcodeManagement.State);
		}

		private void Update()
		{
			if (sessionDiscoveryStatus == null)
				return;

			string key = connectivity.HasBluetoothState && !connectivity.BluetoothIsEnabled
				? "discovery.bluetooth-off"
				: sessionDiscoveryController != null && sessionDiscoveryController.IsListening
					? "discovery.searching"
					: "discovery.preparing";
			if (key == shownDiscoveryKey) return;
			shownDiscoveryKey = key;
			sessionDiscoveryStatus.text = MenuCopy.Get("ConnectionMenu", key);
		}

		private void OnHostOnRelaySettingChange(bool value)
		{
			hostOnRelayToggle.SetValueWithoutNotify(value);
			UpdateInternetWarnings();
		}

		private void UpdateManualConnectionFields(bool usingRelay)
		{
			ipFieldRow.style.display = usingRelay ? DisplayStyle.None : DisplayStyle.Flex;
			roomFieldRow.style.display = usingRelay ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private void Connect()
		{
			if (useRelayToggle.value)
				NetcodeManagement.ConnectUnityServices(roomField.value);
			else
				NetcodeManagement.ConnectLAN(ipField.value);
		}

		private void OnNavPageChange(NavPage page)
		{
			RefreshSessionConnectionState();
		}

		private void OnPanelVisibilityChanged(bool visible)
		{
			RefreshSessionConnectionState();
		}

		private bool AutomaticDiscoveryAllowed()
		{
			// A disabled/hidden panel is equivalent to the menu being closed. While it is
			// visible, automatic joining is only allowed from the multiplayer home page.
			return !isActiveAndEnabled || panel == null || !panel.IsVisible ||
			       navView == null || navView.CurrentPage == homePage;
		}

		private void RefreshSessionConnectionState()
		{
			sessionDiscoveryController?.SetMenuAllowsListening(
				AutomaticDiscoveryAllowed());
			if (sessionDiscoveryStatus != null)
				sessionDiscoveryStatus.Flashing = isActiveAndEnabled &&
					panel != null && panel.IsVisible && navView?.CurrentPage == homePage;
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			multiplayerPanel.EnableInClassList("session-connected", state == NetcodeState.Connected);

			switch (state)
			{
				case NetcodeState.Disconnected:
					sessionIpText.text = "";
					navView.SetModalPresented(sessionPage, false);
					break;

				case NetcodeState.Connecting:
					UpdateIpText();
					OpenSessionPage(SessionState.Connecting);
					break;

				case NetcodeState.Connected:
					UpdateIpText();
					OnColocationChange(ColocationManager.IsColocated);
					break;
			}
		}

		private void UpdateIpText()
		{
			NetworkTransport transport = Manager?.NetworkConfig?.NetworkTransport;
			if (transport == null)
			{
				sessionIpText.text = "";
				return;
			}

			Type transportType = transport.GetType();
			if (string.Equals(transportType.Name, "DistributedAuthorityTransport"))
			{
				sessionIpText.text = MenuCopy.Format("ConnectionMenu", "session.relay", NetcodeManagement.CurrentSessionName);
			}
			else if (transport is UnityTransport unityTransport)
			{
				sessionIpText.text = unityTransport.ConnectionData.Address;
			}
		}

		private void OnColocationChange(bool isColocated)
		{
			if (NetcodeManagement.State == NetcodeState.Connected)
			{
				OpenSessionPage(
					isColocated ? SessionState.Connected : SessionState.Colocating);
			}
		}

		private void OpenSessionPage(SessionState state)
		{
			switch (state)
			{
				case SessionState.Connecting:
					sessionStateText.text = MenuCopy.Get("ConnectionMenu", "session.connecting");
					break;

				case SessionState.Colocating:
					sessionStateText.text = MenuCopy.Get("ConnectionMenu", "session.aligning");
					break;

				case SessionState.Connected:
					sessionStateText.text =
						Manager != null && Manager.CurrentSessionOwner == Manager.LocalClientId
							? MenuCopy.Get("ConnectionMenu", "session.hosting")
							: MenuCopy.Get("ConnectionMenu", "session.connected");
					break;
			}

			navView.SetModalPresented(sessionPage, true, 10, homePage);
		}

		private void Host()
		{
			NetcodeManagement.Protocol protocol = hostOnRelaySetting.Value
				? NetcodeManagement.Protocol.UnityService
				: NetcodeManagement.Protocol.LAN;
			NetcodeManagement.Host(protocol);
		}

		private void OnLanConnectionChanged(bool isConnected)
		{
			navView.SetModalPresented(networkErrorModal, !isConnected, 100);
		}

		private void OnBluetoothEnabledChanged(bool isEnabled)
		{
			navView.SetModalPresented(bluetoothErrorModal, !isEnabled, 100);
		}

		private void UpdateInternetWarnings()
		{
			if (hostOnRelayWarning == null)
				return;

			hostOnRelayWarning.style.display =
				hostOnRelaySetting.Value && !connectivity.HasFullInternet
					? DisplayStyle.Flex
					: DisplayStyle.None;
		}

		private static void Disconnect()
		{
			NetcodeManagement.Disconnect();
		}
	}
}
