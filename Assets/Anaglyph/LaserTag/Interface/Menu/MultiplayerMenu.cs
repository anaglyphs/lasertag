using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
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
		private bool networkWasConnected;

		[Header("Host settings")] [SerializeField]
		private BoolObject hostRelay = null;

		private CancellationTokenSource networkPollCtknSrc;

		private void Start()
		{
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;

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

		private void OnDestroy()
		{
			networkPollCtknSrc?.Cancel();
			
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			ColocationManager.Colocated -= OnColocationChange;
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
				hostRelay.Value ? NetcodeManagement.Protocol.UnityService : NetcodeManagement.Protocol.LAN;
			NetcodeManagement.Host(service);
		}

		private async void NetworkCheckLoop()
		{
			networkPollCtknSrc?.Cancel();
			networkPollCtknSrc = new CancellationTokenSource();
			CancellationToken ctkn = networkPollCtknSrc.Token;

			// assume network is connected at start
			networkWasConnected = true;

			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					CheckNetworkConnection();
					await Awaitable.WaitForSecondsAsync(0.5f, ctkn);
				}
			}
			catch (OperationCanceledException)
			{

			}
		}

		private void CheckNetworkConnection()
		{
			NetworkingState networkState = NetworkConnectivityTest.GetNetworkState();

			bool networkIsConnected = networkState != NetworkingState.NoConnection;
			
			if (networkWasConnected != networkIsConnected)
			{
				networkWasConnected = networkIsConnected;
				navView.SetModalPresented(networkErrorModal, !networkIsConnected);
			}
		}

		private void Disconnect()
		{
			// State change to Disconnected dismisses the session modal back to home.
			NetcodeManagement.Disconnect();
		}
	}
}
