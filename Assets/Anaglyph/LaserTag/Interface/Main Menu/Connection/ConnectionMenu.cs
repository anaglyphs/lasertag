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

		[SerializeField] private IntObject hostModeSetting;

		private NavView navView;
		private VisualElement multiplayerPanel;
		private NavPage homePage;
		private NavPage sessionPage;
		private NavPage networkErrorModal;
		private NavPage wifiOffModal;
		private NavPage bluetoothErrorModal;
		private MenuErrorPresenter errors;
		private UIToolkitPanelXRSetup panel;
		private SessionDiscoveryController sessionDiscoveryController;

		private HostModePicker hostModePicker;
		private bool directHostingSupported;
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
			wifiOffModal = navView.GetPage("wifi-off-modal");
			bluetoothErrorModal = navView.GetPage("bluetooth-error-modal");
			sessionDiscoveryStatus = Require<FlashingLabel>(root, "session-discovery-status");
			shownDiscoveryKey = null;

			bindings.Click(Require<Button>(root, "host-button"), Host);

			hostModePicker = new HostModePicker(root, bindings, hostModeSetting,
				directHostingSupported || Application.isEditor, errors.Show);

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
			bindings.Click(Require<Button>(root, "dismiss-wifi-off-button"),
				() => navView.DismissModal(wifiOffModal));
			bindings.Click(Require<Button>(root, "open-wifi-settings-from-wifi-off-button"),
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
			directHostingSupported = NetcodeManagement.DirectHostingSupported;
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
			ColocationManager.LocalizedChanged += OnLocalizedChanged;
			hostModeSetting.Changed += OnHostModeSettingChanged;
			RefreshSessionConnectionState();

			RefreshHostModes();
			OnNetcodeStateChanged(NetcodeManagement.State);
			connectivity.WifiEnabledChanged += OnWifiEnabledChanged;
			connectivity.LanConnectionChanged += OnLanConnectionChanged;
			connectivity.InternetConnectionChanged += RefreshHostModes;
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
			connectivity.WifiEnabledChanged -= OnWifiEnabledChanged;
			connectivity.LanConnectionChanged -= OnLanConnectionChanged;
			connectivity.InternetConnectionChanged -= RefreshHostModes;
			connectivity.BluetoothEnabledChanged -= OnBluetoothEnabledChanged;
			bindings.Dispose();

			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			ColocationManager.LocalizedChanged -= OnLocalizedChanged;
			hostModeSetting.Changed -= OnHostModeSettingChanged;
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
			RefreshHostModes();
		}

		private void Update()
		{
			if (sessionDiscoveryStatus == null)
				return;

			string unjoinableKey = UnjoinableSessionKey(SessionDiscovery.UnjoinableSessionNearby);
			string key = connectivity.HasBluetoothState && !connectivity.BluetoothIsEnabled
				? "discovery.bluetooth-off"
				: unjoinableKey != null
					? unjoinableKey
					: sessionDiscoveryController != null && sessionDiscoveryController.IsListening
						? "discovery.searching"
						: "discovery.preparing";
			if (key == shownDiscoveryKey) return;
			shownDiscoveryKey = key;
			sessionDiscoveryStatus.text = MenuCopy.Get("ConnectionMenu", key);
			sessionDiscoveryStatus.EnableInClassList("warning", key == unjoinableKey);
		}

		private static string UnjoinableSessionKey(UnjoinableSessionReason reason) => reason switch
		{
			UnjoinableSessionReason.WifiOff => "discovery.unjoinable.wifi-off",
			UnjoinableSessionReason.NoLocalNetwork => "discovery.unjoinable.no-local-network",
			UnjoinableSessionReason.NoInternet => "discovery.unjoinable.no-internet",
			UnjoinableSessionReason.DirectUnsupported => "discovery.unjoinable.direct-unsupported",
			_ => null
		};

		private void OnHostModeSettingChanged(int _) => RefreshHostModes();

		private void RefreshHostModes()
		{
			bool bluetoothOn = !connectivity.HasBluetoothState || connectivity.BluetoothIsEnabled;
			hostModePicker?.Refresh(connectivity.WifiIsEnabled, connectivity.HasLocalNetwork,
				connectivity.HasFullInternet, bluetoothOn);
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
			sessionDiscoveryController?.SetMenuAllowsListening(AutomaticDiscoveryAllowed());
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
					OnLocalizedChanged(ColocationManager.IsLocalized);
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
				// a Wi-Fi Direct session only gets its address once the group forms and the transport starts
				sessionIpText.text = Manager.IsListening ? unityTransport.ConnectionData.Address : "";
			}
		}

		private void OnLocalizedChanged(bool isLocalized)
		{
			if (NetcodeManagement.State == NetcodeState.Connected)
			{
				OpenSessionPage(
					isLocalized ? SessionState.Connected : SessionState.Colocating);
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
			if (hostModePicker.SelectedMode is { } mode)
				NetcodeManagement.Host(mode);
		}

		private void OnWifiEnabledChanged(bool isEnabled)
		{
			navView.SetModalPresented(wifiOffModal, !isEnabled, 100);
			RefreshHostModes();
		}

		private void OnLanConnectionChanged(bool isConnected)
		{
			// with direct hosting the mode cards explain a missing network instead; Wi-Fi off has its own modal
			navView.SetModalPresented(networkErrorModal,
				!isConnected && !directHostingSupported && connectivity.WifiIsEnabled, 100);
			RefreshSessionConnectionState();
			RefreshHostModes();
		}

		private void OnBluetoothEnabledChanged(bool isEnabled)
		{
			navView.SetModalPresented(bluetoothErrorModal, !isEnabled, 100);
			RefreshHostModes();
		}

		private static void Disconnect()
		{
			NetcodeManagement.Disconnect();
		}
	}
}
