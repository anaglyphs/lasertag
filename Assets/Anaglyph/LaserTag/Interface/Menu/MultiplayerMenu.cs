using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Globalization;
using System.Threading;
using TMPro;
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
			Connected
		}

		private static NetworkManager manager => NetworkManager.Singleton;

		[SerializeField] private NavPagesParent navView;

		[Header(nameof(homePage))] [SerializeField]
		private NavPage homePage = null;

		[SerializeField] private Button hostButton = null;

		[Header(nameof(hostSettingsPage))] [SerializeField] private NavPage hostSettingsPage = null;
		[SerializeField] private Toggle hostOnRelayToggle = null;
		[SerializeField] private Graphic hostOnRelayWarningGraphic = null;
		[SerializeField] private BoolObject hostOnRelaySetting = null;
		[SerializeField] private Toggle useAprilTagsToggle = null;
		[SerializeField] private Graphic useAprilTagsWarningGraphic = null;
		[SerializeField] private BoolObject useAprilTagsSetting = null;
		[SerializeField] private TMP_InputField aprilTagSizeField = null;
		[SerializeField] private FloatObject aprilTagSizeSetting = null;

		[Header(nameof(manuallyConnectPage))] [SerializeField]
		private NavPage manuallyConnectPage = null;
		
		[SerializeField] private Toggle useRelayToggle = null;
		[SerializeField] private TMP_InputField ipField = null;
		[SerializeField] private TMP_InputField roomField = null;
		[SerializeField] private GameObject roomFieldLabel = null;
		[SerializeField] private Button connectButton = null;

		[Header(nameof(sessionPage))] [SerializeField]
		private NavPage sessionPage = null;

		// [SerializeField] private Image sessionIcon = null;
		[SerializeField] private TMP_Text sessionStateText = null;
		[SerializeField] private TMP_Text sessionIpText = null;
		[SerializeField] private Button disconnectButton = null;
		// [SerializeField] private Button recalibrateButton = null;

		// [Header("Session icons")] [SerializeField]
		// private Sprite connectingSprite = null;
		//
		// [SerializeField] private Sprite colocatingSprite = null;
		// [SerializeField] private Sprite connectedSprite = null;
		// [SerializeField] private Sprite hostingSprite = null;

		[Header(nameof(networkErrorModal))] [SerializeField]
		private NavPage networkErrorModal;
		[SerializeField] private Button dismissNetworkErrorModal = null;
		[SerializeField] private Button openWifiSettingsButton = null;

		private NetworkState networkState;
		private bool networkIsConnected;
		private bool hasFullInternet;

		private CancellationTokenSource networkPollCtknSrc;

		[SerializeField] private Color warningColor;
		
		[SerializeField] private Graphic[] showWhenNoFullInternet = Array.Empty<Graphic>();

		private void Start()
		{
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;

			// home page
			hostButton.onClick.AddListener(Host);
			
			// host settings page
			hostOnRelayToggle.onValueChanged.AddListener(hostOnRelaySetting.Set);
			hostOnRelaySetting.AddChangeListenerAndCheck(OnHostOnRelaySettingChange);
			
			useAprilTagsToggle.onValueChanged.AddListener(useAprilTagsSetting.Set);
			useAprilTagsSetting.AddChangeListenerAndCheck(OnAprilTagsSettingChange);
			
			aprilTagSizeField.onValueChanged.AddListener(delegate(string str)
			{
				if (!float.TryParse(str, out float f))
					f = 10;
				
				aprilTagSizeSetting.Value = f;
			});
			aprilTagSizeSetting.AddChangeListenerAndCheck(OnAprilTagSizeSettingChange);
			

			// manually connect page
			manuallyConnectPage.showBackButton = true;

			useRelayToggle.onValueChanged.AddListener(useRelay =>
			{
				ipField.gameObject.SetActive(!useRelay);
				roomField.gameObject.SetActive(useRelay);
				roomFieldLabel.gameObject.SetActive(useRelay);
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

			ColocationManager.Colocated += OnColocationChange;

			navView.Changed += OnNavPageChange;
			
			// network error modal
			networkErrorModal.showBackButton = false;
			dismissNetworkErrorModal.onClick.AddListener(delegate
			{
				navView.DismissModal(networkErrorModal);
			});
			openWifiSettingsButton.onClick.AddListener(OpenWifiSettings);
			
			NetworkCheckLoop();
		}

		private void OnEnable()
		{
			if(didStart)
				NetworkCheckLoop();
		}

		private void OnDisable()
		{
			networkPollCtknSrc?.Cancel();
		}
		
		private void OnAprilTagSizeSettingChange(float val)
		{
			aprilTagSizeField.SetTextWithoutNotify(val.ToString(CultureInfo.InvariantCulture));
		}

		private void OnAprilTagsSettingChange(bool val)
		{
			UpdateApriltagSettingWarnGraphic();
			useAprilTagsToggle.SetIsOnWithoutNotify(val);
		}

		private void OnHostOnRelaySettingChange(bool val)
		{
			UpdateHostSettingWarnGraphic();
			hostOnRelayToggle.SetIsOnWithoutNotify(val);
		}

		private void OnDestroy()
		{
			networkPollCtknSrc?.Cancel();
			
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			ColocationManager.Colocated -= OnColocationChange;
			
			hostOnRelaySetting.Changed -= OnHostOnRelaySettingChange;
			useAprilTagsSetting.Changed -= OnAprilTagsSettingChange;
			aprilTagSizeSetting.Changed -= OnAprilTagSizeSettingChange;
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

		private void OnNavPageChange(NavPage page)
		{
			bool onManuallyConnectPage = page == manuallyConnectPage;
			MetaSessionDiscovery.Instance.enabled = !onManuallyConnectPage;
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
			NetworkTransport transport = manager.NetworkConfig.NetworkTransport;
			Type transportType = transport.GetType();

			if (string.Equals(transportType.Name, "DistributedAuthorityTransport"))
				sessionIpText.text = $"Relay: {NetcodeManagement.CurrentSessionName}";
			else if (transport.GetType() == typeof(UnityTransport))
				sessionIpText.text = ((UnityTransport)transport).ConnectionData.Address;
		}

		private void OnColocationChange(bool isColocated)
		{
			if (NetcodeManagement.State == NetcodeState.Connected)
				OpenSessionPage(ColocationManager.IsColocated ? SessionState.Connected : SessionState.Colocating);
		}

		private void OpenSessionPage(SessionState state)
		{
			switch (state)
			{
				case SessionState.Connecting:
					sessionStateText.text = "Connecting...";
					// sessionIcon.sprite = connectingSprite;
					break;

				case SessionState.Colocating:
					sessionStateText.text = "Aligning...";
					// sessionIcon.sprite = colocatingSprite;
					break;

				case SessionState.Connected:
					if (manager.CurrentSessionOwner == manager.LocalClientId)
						sessionStateText.text = "Hosting";
					// sessionIcon.sprite = hostingSprite;
					else
						sessionStateText.text = "Connected!";
					// sessionIcon.sprite = connectedSprite;
					break;
			}

			// recalibrateButton.gameObject.SetActive(state != SessionState.Connecting);

			navView.SetModalPresented(sessionPage, true, returnTo: homePage);
		}

		private void Host()
		{
			NetcodeManagement.Protocol service =
				hostOnRelaySetting.Value ? NetcodeManagement.Protocol.UnityService : NetcodeManagement.Protocol.LAN;
			NetcodeManagement.Host(service);
		}

		private async void NetworkCheckLoop()
		{
			networkPollCtknSrc?.Cancel();
			networkPollCtknSrc = new CancellationTokenSource();
			CancellationToken ctkn = networkPollCtknSrc.Token;
			
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					CheckNetworkConnection();
					await Awaitable.WaitForSecondsAsync(1f, ctkn);
				}
			}
			catch (OperationCanceledException)
			{

			}
		}

		private void CheckNetworkConnection()
		{
			NetworkState newNetworkState = NetworkConnectivityTest.GetNetworkState();

			networkIsConnected = (newNetworkState & NetworkState.ConnectionLAN) != 0;
			hasFullInternet = (newNetworkState & NetworkState.FullInternetFlag) != 0;
			
			bool connectionChanged =
				((newNetworkState ^ networkState) & NetworkState.ConnectionLAN) != 0;
			
			bool fullInternetChanged =
				((newNetworkState ^ networkState) & NetworkState.FullInternetFlag) != 0;
			
			if (connectionChanged)
			{
				// show network error modal
				navView.SetModalPresented(networkErrorModal, !networkIsConnected);
			}

			if (fullInternetChanged)
			{
				foreach (Graphic graphic in showWhenNoFullInternet)
				{
					graphic.enabled = !hasFullInternet;
					graphic.color = warningColor;
				}

				UpdateInternetWarnGraphics();
			}
			
			networkState = newNetworkState;
		}

		private void UpdateInternetWarnGraphics()
		{
			UpdateHostSettingWarnGraphic();
			UpdateApriltagSettingWarnGraphic();
		}

		private void UpdateHostSettingWarnGraphic()
		{
			bool warn = hostOnRelaySetting.Value && !hasFullInternet;
			hostOnRelayWarningGraphic.color = warn ? warningColor : Color.white;
		}

		private void UpdateApriltagSettingWarnGraphic()
		{
			bool warn = !useAprilTagsSetting.Value && !hasFullInternet;
			useAprilTagsWarningGraphic.color = warn ? warningColor : Color.white;
		}

		private void Disconnect()
		{
			// State change to Disconnected dismisses the session modal back to home.
			NetcodeManagement.Disconnect();
		}
	}
}
