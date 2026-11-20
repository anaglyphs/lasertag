using System;
using Anaglyph.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>Owns discovery and automatic LAN joining for a headset.</summary>
	[DefaultExecutionOrder(-50)]
	public sealed class SessionDiscoveryController : MonoBehaviour
	{
		private const float InitialConnectDelaySeconds = 0.5f;
		private const float ReconnectDelaySeconds = 5f;

		private bool requiredPermissionsGranted;
		private bool menuAllowsListening = true;
		private bool menuAllowsOnlyDirectSessions;
		private bool recoveryMenuOpen;
		private bool ownsPinnedAttempt;
		private bool hasAttemptedPinnedConnection;
		private string attemptedAddress;
		private float nextAttemptTime;
		private NetworkManager subscribedNetworkManager;

		public bool IsListening => isActiveAndEnabled && SessionDiscovery.IsListening;

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
			SessionDiscovery.SetActivity(SessionDiscoveryActivity.Disabled);
		}

		public void SetRequiredPermissionsGranted(bool granted)
		{
			if (requiredPermissionsGranted == granted) return;
			requiredPermissionsGranted = granted;
			DelayNextAttempt();
			ApplyConnectionPolicy();
		}

		public void SetMenuAllowsListening(bool allowed, bool onlyForDirectSessions = false)
		{
			if (menuAllowsListening == allowed && menuAllowsOnlyDirectSessions == onlyForDirectSessions) return;
			menuAllowsListening = allowed;
			menuAllowsOnlyDirectSessions = onlyForDirectSessions;
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

			SessionDiscoveryActivity activity = SessionDiscoveryActivity.Disabled;
			if (isActiveAndEnabled && requiredPermissionsGranted)
			{
				activity = NetcodeManagement.State switch
				{
					NetcodeState.Disconnected when menuAllowsListening &&
					                               !HeadsetConfiguration.PinnedHostEnabled =>
						SessionDiscoveryActivity.Listening,
					// The LAN host advertises its IP; the relay session owner advertises its name.
					NetcodeState.Connected when manager != null &&
					                            manager.LocalClient.IsSessionOwner =>
						SessionDiscoveryActivity.Advertising,
					_ => SessionDiscoveryActivity.Disabled
				};
			}

			SessionDiscovery.JoinsOnlyDirectSessions = menuAllowsOnlyDirectSessions;
			SessionDiscovery.SetActivity(activity);
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
	}
}
