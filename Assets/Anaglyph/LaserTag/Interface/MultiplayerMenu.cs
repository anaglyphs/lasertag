using System;
using System.Threading;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.VariableObjects;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	[DefaultExecutionOrder(100)]
	public class MultiplayerMenu : MonoBehaviour
	{
		private enum SessionState
		{
			Connecting,
			Colocating,
			Connected
		}

		private const int ErrorModalPriority = 200;

		private static NetworkManager Manager => NetworkManager.Singleton;

		[SerializeField] private BoolObject hostOnRelaySetting;
		[SerializeField] private BoolObject useAprilTagsSetting;
		[SerializeField] private FloatObject aprilTagSizeSetting;
		[SerializeField] private StringObject buildNumber;

		private NavView navView;
		private NavPage homePage;
		private NavPage manuallyConnectPage;
		private NavPage sessionPage;
		private NavPage networkErrorModal;
		private NavPage bluetoothErrorModal;
		private NavPage errorModal;
		private UIToolkitPanelXRSetup panel;
		private SessionDiscoveryController sessionDiscoveryController;

		private Toggle hostOnRelayToggle;
		private Label hostOnRelayWarning;
		private Toggle useAprilTagsToggle;
		private Label useAprilTagsWarning;
		private FloatField aprilTagSizeField;

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
		private Label noFullInternetWarning;

		private Label errorSubjectText;
		private Label errorDetailsText;
		private string errorSubject;
		private string errorDetails;
		private bool hasUnacknowledgedError;

		private NetworkState networkState;
		private bool hasNetworkState;
		private bool hasFullInternet;
		private bool bluetoothIsEnabled;
		private bool hasBluetoothState;
		private bool hasLoggedBluetoothCheckFailure;
		private CancellationTokenSource networkPollCancellation;

		private void InitializeUI()
		{
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"MultiplayerMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			VisualElement pages = Require<VisualElement>(root, "pages");
			navView = new NavView(pages);
			homePage = navView.AddPage("home-page", false);
			NavPage hostSettingsPage = navView.AddPage("host-settings-page");
			manuallyConnectPage = navView.AddPage("manual-connect-page");
			sessionPage = navView.AddPage("session-page", false);
			networkErrorModal = navView.AddPage("network-error-modal", false);
			bluetoothErrorModal = navView.AddPage("bluetooth-error-modal", false);
			errorModal = navView.AddPage("error-modal", false);

			Require<Button>(root, "host-button").clicked += Host;
			Require<Button>(root, "host-settings-button").clicked += hostSettingsPage.NavigateHere;
			Require<Button>(root, "manual-connect-button").clicked += manuallyConnectPage.NavigateHere;

			hostOnRelayToggle = Require<Toggle>(root, "host-on-relay-toggle");
			hostOnRelayWarning = Require<Label>(root, "host-on-relay-warning");
			useAprilTagsToggle = Require<Toggle>(root, "use-april-tags-toggle");
			useAprilTagsWarning = Require<Label>(root, "use-april-tags-warning");
			aprilTagSizeField = Require<FloatField>(root, "april-tag-size-field");

			hostOnRelayToggle.RegisterValueChangedCallback(
				change => hostOnRelaySetting.Value = change.newValue);
			useAprilTagsToggle.RegisterValueChangedCallback(
				change => useAprilTagsSetting.Value = change.newValue);
			aprilTagSizeField.RegisterValueChangedCallback(OnAprilTagSizeFieldChanged);

			useRelayToggle = Require<Toggle>(root, "use-relay-toggle");
			ipFieldRow = Require<VisualElement>(root, "ip-field-row");
			ipField = Require<TextField>(root, "ip-field");
			roomFieldRow = Require<VisualElement>(root, "room-field-row");
			roomField = Require<TextField>(root, "room-field");
			useRelayToggle.RegisterValueChangedCallback(
				change =>
				{
					useRelay = change.newValue;
					UpdateManualConnectionFields(useRelay);
				});
			ipField.RegisterValueChangedCallback(change => manualIp = change.newValue);
			roomField.RegisterValueChangedCallback(change => manualRoom = change.newValue);

			manualIp ??= NetcodeManagement.GetLocalIPv4();
			useRelayToggle.SetValueWithoutNotify(useRelay);
			ipField.SetValueWithoutNotify(manualIp);
			roomField.SetValueWithoutNotify(manualRoom);
			UpdateManualConnectionFields(useRelay);

			Require<Button>(root, "connect-button").clicked += Connect;

			sessionStateText = Require<Label>(root, "session-state");
			sessionIpText = Require<Label>(root, "session-address");
			Require<Button>(root, "disconnect-button").clicked += Disconnect;

			Require<Button>(root, "dismiss-network-error-button").clicked +=
				() => navView.DismissModal(networkErrorModal);
			Require<Button>(root, "open-wifi-settings-button").clicked += OpenWifiSettings;
			Require<Button>(root, "dismiss-bluetooth-error-button").clicked +=
				() => navView.DismissModal(bluetoothErrorModal);
			Require<Button>(root, "open-bluetooth-settings-button").clicked +=
				OpenBluetoothSettings;

			errorSubjectText = Require<Label>(root, "error-subject");
			errorDetailsText = Require<Label>(root, "error-details");
			Require<Button>(root, "dismiss-error-button").clicked += DismissError;

			noFullInternetWarning = Require<Label>(root, "no-full-internet-warning");
			Label version = Require<Label>(root, "version");
			version.text =
				$"Version: {Application.version}\nBuild: {(buildNumber ? buildNumber.Value : "")}";

			navView.Changed += OnNavPageChange;
			navView.Start(homePage);
		}

		// Subscribed for the component's whole lifetime, not just while enabled —
		// an error raised while this panel is hidden still has to reach the user.
		private void Awake()
		{
			UserErrors.Raised += OnUserErrorRaised;
			sessionDiscoveryController =
				GetComponentInParent<SessionDiscoveryController>();
			if (sessionDiscoveryController == null)
				throw new InvalidOperationException(
					"MultiplayerMenu requires SessionDiscoveryController in its parent hierarchy.");

			// this component owns the build number asset, so it tells netcode
			// what to compare when a client joins
			NetcodeManagement.GameVersion = buildNumber && !string.IsNullOrEmpty(buildNumber.Value)
				? $"{Application.version} ({buildNumber.Value})"
				: Application.version;
		}

		private void OnDestroy()
		{
			sessionDiscoveryController?.SetMenuAllowsListening(true);
			UserErrors.Raised -= OnUserErrorRaised;
		}

		private void OnUserErrorRaised(UserError error)
		{
			ShowError(error.subject, error.details);
		}

		private void OnEnable()
		{
			InitializeUI();
			panel = GetComponent<UIToolkitPanelXRSetup>();
			if (panel == null)
				throw new InvalidOperationException(
					"MultiplayerMenu requires UIToolkitPanelXRSetup on the same object.");

			panel.VisibleChanged += OnPanelVisibilityChanged;

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			ColocationManager.Colocated += OnColocationChange;
			hostOnRelaySetting.Changed += OnHostOnRelaySettingChange;
			useAprilTagsSetting.Changed += OnAprilTagsSettingChange;
			aprilTagSizeSetting.Changed += OnAprilTagSizeSettingChange;
			RefreshSessionDiscoveryState();

			OnHostOnRelaySettingChange(hostOnRelaySetting.Value);
			OnAprilTagsSettingChange(useAprilTagsSetting.Value);
			OnAprilTagSizeSettingChange(aprilTagSizeSetting.Value);
			OnNetcodeStateChanged(NetcodeManagement.State);
			hasNetworkState = false;
			hasBluetoothState = false;
			BeginNetworkCheckLoop();

			// an error may have arrived while this menu was disabled
			if (hasUnacknowledgedError)
				ShowError(errorSubject, errorDetails);
		}

		private void OnDisable()
		{
			networkPollCancellation?.Cancel();
			networkPollCancellation?.Dispose();
			networkPollCancellation = null;

			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			ColocationManager.Colocated -= OnColocationChange;
			hostOnRelaySetting.Changed -= OnHostOnRelaySettingChange;
			useAprilTagsSetting.Changed -= OnAprilTagsSettingChange;
			aprilTagSizeSetting.Changed -= OnAprilTagSizeSettingChange;
			if (panel != null)
				panel.VisibleChanged -= OnPanelVisibilityChanged;
			sessionDiscoveryController?.SetMenuAllowsListening(true);

			if (navView != null)
			{
				navView.Changed -= OnNavPageChange;
				navView.Dispose();
				navView = null;
			}
		}

		private void OnAprilTagSizeFieldChanged(ChangeEvent<float> change)
		{
			aprilTagSizeSetting.Value = change.newValue;
		}

		private void OnAprilTagSizeSettingChange(float value)
		{
			aprilTagSizeField.SetValueWithoutNotify(value);
		}

		private void OnAprilTagsSettingChange(bool value)
		{
			useAprilTagsToggle.SetValueWithoutNotify(value);
			UpdateInternetWarnings();
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
			RefreshSessionDiscoveryState();
		}

		private void OnPanelVisibilityChanged(bool visible)
		{
			RefreshSessionDiscoveryState();
		}

		private bool AutomaticDiscoveryAllowed()
		{
			// A disabled/hidden panel is equivalent to the menu being closed. While it is
			// visible, automatic joining is only allowed from the multiplayer home page.
			return !isActiveAndEnabled || panel == null || !panel.IsVisible ||
			       navView == null || navView.CurrentPage == homePage;
		}

		private void RefreshSessionDiscoveryState()
		{
			sessionDiscoveryController?.SetMenuAllowsListening(
				AutomaticDiscoveryAllowed());
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
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
				sessionIpText.text = $"Relay: {NetcodeManagement.CurrentSessionName}";
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
					sessionStateText.text = "Connecting...";
					break;

				case SessionState.Colocating:
					sessionStateText.text = "Aligning...";
					break;

				case SessionState.Connected:
					sessionStateText.text =
						Manager != null && Manager.CurrentSessionOwner == Manager.LocalClientId
							? "Hosting"
							: "Connected!";
					break;
			}

			navView.SetModalPresented(sessionPage, true, 10, homePage);
		}

		public void ShowError(string subject, string details)
		{
			errorSubject = subject;
			errorDetails = details;
			hasUnacknowledgedError = true;

			if (navView == null)
				return;

			errorSubjectText.text = subject;
			errorDetailsText.text = details;
			navView.SetModalPresented(errorModal, true, ErrorModalPriority);
		}

		public void DismissError()
		{
			hasUnacknowledgedError = false;
			navView?.DismissModal(errorModal);
		}

		private void Host()
		{
			NetcodeManagement.Protocol protocol = hostOnRelaySetting.Value
				? NetcodeManagement.Protocol.UnityService
				: NetcodeManagement.Protocol.LAN;
			NetcodeManagement.Host(protocol);
		}

		private async void BeginNetworkCheckLoop()
		{
			networkPollCancellation?.Cancel();
			networkPollCancellation?.Dispose();
			networkPollCancellation = new CancellationTokenSource();
			CancellationToken token = networkPollCancellation.Token;

			try
			{
				while (!token.IsCancellationRequested)
				{
					CheckNetworkConnection();
					CheckBluetoothConnection();
					await Awaitable.WaitForSecondsAsync(1f, token);
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void CheckNetworkConnection()
		{
			NetworkState newState = NetworkConnectivityTest.GetNetworkState();
			bool networkIsConnected = (newState & NetworkState.ConnectionLAN) != 0;
			hasFullInternet = (newState & NetworkState.FullInternetFlag) != 0;

			bool connectionChanged =
				!hasNetworkState ||
				((newState ^ networkState) & NetworkState.ConnectionLAN) != 0;
			bool fullInternetChanged =
				!hasNetworkState ||
				((newState ^ networkState) & NetworkState.FullInternetFlag) != 0;

			if (connectionChanged)
				navView.SetModalPresented(networkErrorModal, !networkIsConnected, 100);

			if (fullInternetChanged)
				UpdateInternetWarnings();

			networkState = newState;
			hasNetworkState = true;
		}

		private void CheckBluetoothConnection()
		{
			if (!TryGetBluetoothEnabled(out bool newBluetoothIsEnabled))
				return;

			bool bluetoothChanged =
				!hasBluetoothState || newBluetoothIsEnabled != bluetoothIsEnabled;
			if (bluetoothChanged)
				navView.SetModalPresented(
					bluetoothErrorModal, !newBluetoothIsEnabled, 100);

			bluetoothIsEnabled = newBluetoothIsEnabled;
			hasBluetoothState = true;
		}

		private bool TryGetBluetoothEnabled(out bool isEnabled)
		{
#if UNITY_ANDROID
			if (Application.isEditor)
			{
				isEnabled = true;
				return true;
			}

			try
			{
				using AndroidJavaClass unityPlayer =
					new("com.unity3d.player.UnityPlayer");
				using AndroidJavaObject activity =
					unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
				using AndroidJavaObject contentResolver =
					activity.Call<AndroidJavaObject>("getContentResolver");
				using AndroidJavaClass globalSettings =
					new("android.provider.Settings$Global");

				int bluetoothState = globalSettings.CallStatic<int>(
					"getInt", contentResolver, "bluetooth_on", -1);
				if (bluetoothState < 0)
					throw new InvalidOperationException(
						"Android did not report a Bluetooth setting.");

				isEnabled = bluetoothState != 0;
				hasLoggedBluetoothCheckFailure = false;
				return true;
			}
			catch (Exception exception)
			{
				isEnabled = false;
				if (!hasLoggedBluetoothCheckFailure)
				{
					hasLoggedBluetoothCheckFailure = true;
					Debug.LogWarning("Could not check whether Bluetooth is enabled.");
					Debug.LogException(exception);
				}

				return false;
			}
#else
			isEnabled = true;
			return true;
#endif
		}

		private void UpdateInternetWarnings()
		{
			if (noFullInternetWarning == null)
				return;

			noFullInternetWarning.style.display =
				hasFullInternet ? DisplayStyle.None : DisplayStyle.Flex;
			hostOnRelayWarning.style.display =
				hostOnRelaySetting.Value && !hasFullInternet
					? DisplayStyle.Flex
					: DisplayStyle.None;
			useAprilTagsWarning.style.display =
				!useAprilTagsSetting.Value && !hasFullInternet
					? DisplayStyle.Flex
					: DisplayStyle.None;
		}

		private static void Disconnect()
		{
			NetcodeManagement.Disconnect();
		}

		private static void OpenWifiSettings()
		{
#if UNITY_ANDROID
			if (Application.isEditor)
			{
				Debug.LogWarning("Wi-Fi settings can only be opened from an Android player.");
				return;
			}

			const string wifiSettingsAction = "android.settings.WIFI_SETTINGS";
			const string systemSettingsAction = "android.settings.SETTINGS";

			try
			{
				using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
				using AndroidJavaObject activity =
					unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
				using AndroidJavaObject packageManager =
					activity.Call<AndroidJavaObject>("getPackageManager");

				if (TryStartAndroidActivity(activity, packageManager, wifiSettingsAction))
					return;

				if (!TryStartAndroidActivity(activity, packageManager, systemSettingsAction))
					Debug.LogError("No Android system settings activity is available.");
			}
			catch (AndroidJavaException exception)
			{
				Debug.LogException(exception);
			}
#else
			Debug.LogWarning("Wi-Fi settings can only be opened from an Android player.");
#endif
		}

		private static void OpenBluetoothSettings()
		{
#if UNITY_ANDROID
			if (Application.isEditor)
			{
				Debug.LogWarning(
					"Bluetooth settings can only be opened from an Android player.");
				return;
			}

			const string bluetoothSettingsAction =
				"android.settings.BLUETOOTH_SETTINGS";
			const string systemSettingsAction = "android.settings.SETTINGS";

			try
			{
				using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
				using AndroidJavaObject activity =
					unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
				using AndroidJavaObject packageManager =
					activity.Call<AndroidJavaObject>("getPackageManager");

				if (TryStartAndroidActivity(
					    activity, packageManager, bluetoothSettingsAction))
					return;

				if (!TryStartAndroidActivity(activity, packageManager, systemSettingsAction))
					Debug.LogError("No Android system settings activity is available.");
			}
			catch (AndroidJavaException exception)
			{
				Debug.LogException(exception);
			}
#else
			Debug.LogWarning(
				"Bluetooth settings can only be opened from an Android player.");
#endif
		}

#if UNITY_ANDROID
		private static bool TryStartAndroidActivity(
			AndroidJavaObject activity,
			AndroidJavaObject packageManager,
			string action)
		{
			using AndroidJavaObject intent = new("android.content.Intent", action);
			using AndroidJavaObject component =
				intent.Call<AndroidJavaObject>("resolveActivity", packageManager);

			if (component == null)
				return false;

			activity.Call("startActivity", intent);
			return true;
		}
#endif

		private static T Require<T>(VisualElement root, string name)
			where T : VisualElement
		{
			T element = root.Q<T>(name);
			if (element == null)
				throw new InvalidOperationException(
					$"Required UI Toolkit element '{name}' ({typeof(T).Name}) was not found.");

			return element;
		}
	}
}
