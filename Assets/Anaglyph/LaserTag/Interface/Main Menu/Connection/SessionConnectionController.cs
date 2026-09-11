using System;
using Anaglyph.Netcode;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>Owns discovery and automatic LAN joining for a headset.</summary>
	[DefaultExecutionOrder(-50)]
	public sealed class SessionConnectionController : MonoBehaviour
	{
		private const float InitialConnectDelaySeconds = 0.5f;
		private const float ReconnectDelaySeconds = 5f;

		private bool requiredPermissionsGranted;
		private bool menuAllowsListening = true;
		private bool recoveryMenuOpen;
		private bool ownsPinnedAttempt;
		private bool hasAttemptedPinnedConnection;
		private string attemptedAddress;
		private float nextAttemptTime;
		private MetaSessionDiscovery sessionDiscovery;
		private NetworkManager subscribedNetworkManager;

		public bool IsListening =>
			isActiveAndEnabled && sessionDiscovery != null && sessionDiscovery.IsListening;

		private bool PinnedConnectionAllowed => isActiveAndEnabled &&
			requiredPermissionsGranted && !recoveryMenuOpen && HeadsetConfiguration.PinnedHostEnabled;

		private void OnEnable()
		{
			NetcodeManagement.StateChanged += OnNetworkStateChanged;
			HeadsetConfiguration.Changed += ApplyConnectionPolicy;
			DelayNextAttempt();
			ApplyConnectionPolicy();
		}

		private void Start() => ApplyConnectionPolicy();

		private void OnDisable()
		{
			NetcodeManagement.StateChanged -= OnNetworkStateChanged;
			HeadsetConfiguration.Changed -= ApplyConnectionPolicy;
			CancelPinnedAttempt();
			SetNetworkManager(null);
			SetDiscoveryActivity(MetaSessionDiscovery.Activity.Disabled);
		}

		public void SetRequiredPermissionsGranted(bool granted)
		{
			if (requiredPermissionsGranted == granted) return;
			requiredPermissionsGranted = granted;
			DelayNextAttempt();
			ApplyConnectionPolicy();
		}

		public void SetMenuAllowsListening(bool allowed)
		{
			if (menuAllowsListening == allowed) return;
			menuAllowsListening = allowed;
			ApplyConnectionPolicy();
		}

		// Opening the menu explicitly gives staff time to enter the password and
		// reach Settings. Only our own pending LAN attempt may be cancelled.
		public void SetRecoveryMenuOpen(bool open)
		{
			if (recoveryMenuOpen == open) return;
			recoveryMenuOpen = open;
			DelayNextAttempt();
			ApplyConnectionPolicy();
		}

		private void OnNetworkStateChanged(NetcodeState state)
		{
			if (state != NetcodeState.Connecting)
			{
				ownsPinnedAttempt = false;
				DelayNextAttempt();
			}
			ApplyConnectionPolicy();
		}

		private void DelayNextAttempt()
		{
			nextAttemptTime = Time.unscaledTime + (hasAttemptedPinnedConnection
				? ReconnectDelaySeconds : InitialConnectDelaySeconds);
		}

		private void ApplyConnectionPolicy()
		{
			NetworkManager manager = isActiveAndEnabled ? NetworkManager.Singleton : null;
			SetNetworkManager(manager);

			if (!PinnedConnectionAllowed || attemptedAddress != HeadsetConfiguration.PinnedHostAddress)
				CancelPinnedAttempt();

			MetaSessionDiscovery.Activity activity = MetaSessionDiscovery.Activity.Disabled;
			if (isActiveAndEnabled && requiredPermissionsGranted)
			{
				activity = NetcodeManagement.State switch
				{
					NetcodeState.Disconnected when menuAllowsListening &&
					                               !HeadsetConfiguration.PinnedHostEnabled =>
						MetaSessionDiscovery.Activity.Listening,
					// The LAN host advertises its IP; the relay session owner advertises its name.
					NetcodeState.Connected when manager != null &&
					                            (manager.NetworkConfig.UseCMBService
					                             ? manager.LocalClient.IsSessionOwner
					                             : manager.IsHost) =>
						MetaSessionDiscovery.Activity.Advertising,
					_ => MetaSessionDiscovery.Activity.Disabled
				};
			}
			SetDiscoveryActivity(activity);
		}

		private void Update()
		{
			if (NetworkManager.Singleton != subscribedNetworkManager)
				ApplyConnectionPolicy();

			// Polling readiness also handles a late NetworkManager and asynchronous
			// shutdown, without abandoning retries when the transport is still busy.
			if (!PinnedConnectionAllowed || Time.unscaledTime < nextAttemptTime ||
			    NetcodeManagement.State != NetcodeState.Disconnected)
				return;

			NetworkManager manager = NetworkManager.Singleton;
			if (manager == null || manager.IsListening || manager.ShutdownInProgress)
				return;

			hasAttemptedPinnedConnection = true;
			attemptedAddress = HeadsetConfiguration.PinnedHostAddress;
			ownsPinnedAttempt = true;
			DelayNextAttempt();
			try
			{
				NetcodeManagement.ConnectLAN(attemptedAddress);
			}
			catch (Exception exception)
			{
				CancelPinnedAttempt();
				Debug.LogException(exception);
			}
		}

		private void SetNetworkManager(NetworkManager manager)
		{
			if (ReferenceEquals(manager, subscribedNetworkManager)) return;
			if (!ReferenceEquals(subscribedNetworkManager, null))
				subscribedNetworkManager.OnSessionOwnerPromoted -= OnSessionOwnerPromoted;
			subscribedNetworkManager = manager;
			if (subscribedNetworkManager != null)
				subscribedNetworkManager.OnSessionOwnerPromoted += OnSessionOwnerPromoted;
		}

		private void OnSessionOwnerPromoted(ulong sessionOwner) => ApplyConnectionPolicy();

		private void CancelPinnedAttempt()
		{
			if (!ownsPinnedAttempt) return;
			ownsPinnedAttempt = false;
			if (NetworkManager.Singleton != null && NetcodeManagement.State == NetcodeState.Connecting)
				NetcodeManagement.Disconnect();
		}

		private void SetDiscoveryActivity(MetaSessionDiscovery.Activity activity)
		{
			MetaSessionDiscovery current = MetaSessionDiscovery.Instance;
			if (current != sessionDiscovery)
			{
				// Unity's destroyed components are not CLR-null during scene teardown.
				if (sessionDiscovery != null)
					sessionDiscovery.SetActivity(MetaSessionDiscovery.Activity.Disabled);
				sessionDiscovery = current;
			}
			if (sessionDiscovery != null)
				sessionDiscovery.SetActivity(activity);
		}
	}
}
