using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.Netcode;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Anaglyph.XR.SharedSpaces.SharedAnchors
{
	public class MetaSessionDiscovery : MonoBehaviour
	{
		public static MetaSessionDiscovery Instance { get; private set; }
		private NetworkManager NetMan => NetworkManager.Singleton;

		/// <summary>
		/// Extra client-side condition for listening, injected by the game layer. Null means
		/// listening is allowed. Whoever owns the gate calls <see cref="RefreshState"/> when
		/// its answer changes.
		/// </summary>
		public Func<bool> ListeningGate;

		/// <summary>
		/// Application-level readiness condition for all automatic discovery activity,
		/// injected by the game layer. Null means the application is ready. Whoever owns
		/// the gate calls <see cref="RefreshState"/> when its answer changes.
		/// </summary>
		public Func<bool> ApplicationReadyGate;

		/// <summary>
		/// Extra host-side condition for advertising, injected by the game layer (assembly
		/// direction prevents referencing it here). Null means no extra condition. Whoever
		/// owns the gate calls <see cref="RefreshState"/> when its answer changes.
		/// </summary>
		public Func<bool> AdvertisementGate;

		public void RefreshState()
		{
			State newState = GetDesiredState();
			if (!hasDesiredState || newState != desiredState)
			{
				hasDesiredState = true;
				desiredState = newState;
				listenStartDelayPending = newState == State.Listen && hasEnteredListen;
				if (newState == State.Listen)
					hasEnteredListen = true;
				stateRetryDelay = MinStateRetryDelay;
			}

			stateRefreshRequested = true;
			stateDelayCancellation?.Cancel();

			if (!stateLoopRunning)
				ReconcileStateLoop(stateLifetimeCancellation.Token);
		}

		private const string LogHeader = "[SessionDiscovery] ";

		// Unity's Meta OpenXR colocation feature. Resolved lazily from the active
		// OpenXR settings; null if the feature isn't enabled (e.g. in-editor play).
		private ColocationDiscoveryFeature colocationFeature;

		private ColocationDiscoveryFeature Colocation =>
			colocationFeature ??= OpenXRSettings.Instance != null
				? OpenXRSettings.Instance.GetFeature<ColocationDiscoveryFeature>()
				: null;

		private static void Log(string str)
		{
			Debug.Log(LogHeader + str);
		}

		private static void LogWarning(string str)
		{
			Debug.LogWarning(LogHeader + str);
		}

		private const string LanPrefix = "IP:";
		private const string RelayPrefix = "Relay:";

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			SubscribeToEvents(true);
			RefreshState();
		}

		private void OnEnable()
		{
			if (!didStart) return;
			SubscribeToEvents(true);
			RefreshState();
		}

		private void OnDisable()
		{
			SubscribeToEvents(false);
			RefreshState();
		}

		private void OnDestroy()
		{
			SubscribeToEvents(false);
			stateLifetimeCancellation.Cancel();
			stateDelayCancellation?.Cancel();

			if (Instance == this)
				Instance = null;
		}

		private bool isSubscribed = false;
		private ColocationDiscoveryFeature subscribedColocation;

		private void SubscribeToEvents(bool shouldSubscribe)
		{
			if (shouldSubscribe)
			{
				if (!isSubscribed)
				{
					NetMan.OnSessionOwnerPromoted += OnSessionOwnerPromoted;
					NetcodeManagement.StateChanged += OnNetworkStateChange;
					isSubscribed = true;
				}

				ColocationDiscoveryFeature feature = Colocation;
				if (feature != null && feature != subscribedColocation)
				{
					if (subscribedColocation != null)
						UnsubscribeFromColocation(subscribedColocation);

					subscribedColocation = feature;
					feature.discoveryStateChanged += HandleDiscoveryStateChanged;
					feature.advertisementStateChanged += HandleAdvertisementStateChanged;
					feature.messageDiscovered += HandleMessageDiscovered;
				}
			}
			else
			{
				if (isSubscribed)
				{
					if (NetMan)
						NetMan.OnSessionOwnerPromoted -= OnSessionOwnerPromoted;

					NetcodeManagement.StateChanged -= OnNetworkStateChange;
					isSubscribed = false;
				}

				if (subscribedColocation != null)
				{
					UnsubscribeFromColocation(subscribedColocation);
					subscribedColocation = null;
				}
			}
		}

		private void UnsubscribeFromColocation(ColocationDiscoveryFeature feature)
		{
			feature.discoveryStateChanged -= HandleDiscoveryStateChanged;
			feature.advertisementStateChanged -= HandleAdvertisementStateChanged;
			feature.messageDiscovered -= HandleMessageDiscovered;
		}

		private void OnSessionOwnerPromoted(ulong clientId)
		{
			RefreshState();
		}

		private void OnNetworkStateChange(NetcodeState state)
		{
			if (state == NetcodeState.Connected)
				reconnectDelay = MinReconnectDelay;

			RefreshState();
		}

		private bool isPaused = false;

		private void OnApplicationPause(bool isPaused)
		{
			this.isPaused = isPaused;

			if (didStart)
			{
				SubscribeToEvents(!this.isPaused);
				RefreshState();
			}
		}

		private enum State
		{
			Disable,
			Listen,
			Advertise
		}

		private const float ListenStartDelaySeconds = 2;
		private const float MinStateRetryDelay = 1;
		private const float MaxStateRetryDelay = 10;

		private readonly CancellationTokenSource stateLifetimeCancellation = new();
		private CancellationTokenSource stateDelayCancellation;
		private State desiredState = State.Disable;
		private bool hasDesiredState;
		private bool hasEnteredListen;
		private bool listenStartDelayPending;
		private bool stateLoopRunning;
		private bool stateRefreshRequested;
		private bool nativeOperationInFlight;
		private bool loggedUnavailableFeature;
		private float stateRetryDelay = MinStateRetryDelay;

		private State GetDesiredState()
		{
			if (!(ApplicationReadyGate?.Invoke() ?? true))
				return State.Disable;

			if (enabled && !isPaused)
				switch (NetcodeManagement.State)
				{
					case NetcodeState.Disconnected:
						return ListeningGate?.Invoke() ?? true
							? State.Listen
							: State.Disable;
					case NetcodeState.Connecting:
						return State.Disable;
					case NetcodeState.Connected:
						bool isHost = NetMan.CurrentSessionOwner == NetMan.LocalClientId;
						// A host that isn't ready to be joined (e.g. not yet localized to its
						// map) stays quiet: joiners arriving early would have nothing to
						// align to. Manual connections are unaffected.
						bool gateOpen = AdvertisementGate?.Invoke() ?? true;
						return isHost && gateOpen ? State.Advertise : State.Disable;
				}

			return State.Disable;
		}

		private async void ReconcileStateLoop(CancellationToken cancelToken)
		{
			if (stateLoopRunning) return;
			stateLoopRunning = true;

			try
			{
				while (!cancelToken.IsCancellationRequested)
				{
					stateRefreshRequested = false;
					State targetState = desiredState;
					bool operationSucceeded;

					try
					{
						operationSucceeded = await ReconcileState(targetState, cancelToken);
					}
					catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception exception)
					{
						LogWarning($"State transition to {targetState} threw an exception; retrying");
						Debug.LogException(exception);
						operationSucceeded = false;
					}

					cancelToken.ThrowIfCancellationRequested();

					// A UI, pause, ownership, or network change superseded the operation. Native
					// calls cannot be cancelled, so reconcile their completed side effects now.
					if (targetState != desiredState || stateRefreshRequested)
						continue;

					if (operationSucceeded && StateIsSatisfied(targetState))
					{
						stateRetryDelay = MinStateRetryDelay;
						break;
					}

					float delay = stateRetryDelay;
					stateRetryDelay = Mathf.Min(stateRetryDelay * 2, MaxStateRetryDelay);
					await WaitForInterruptibleDelay(delay, cancelToken);
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				stateLoopRunning = false;
				if (stateRefreshRequested && !cancelToken.IsCancellationRequested)
					ReconcileStateLoop(cancelToken);
			}
		}

		private async Task<bool> ReconcileState(State targetState, CancellationToken cancelToken)
		{
			ColocationDiscoveryFeature feature = Colocation;
			if (isSubscribed && feature != null && feature != subscribedColocation)
				SubscribeToEvents(true);

			if (feature == null || !feature.enabled)
			{
				if (!loggedUnavailableFeature)
				{
					LogWarning("Colocation feature unavailable");
					loggedUnavailableFeature = true;
				}

				return targetState == State.Disable;
			}

			loggedUnavailableFeature = false;

			switch (targetState)
			{
				case State.Listen:
					if (!await EnsureAdvertisementStopped(feature, cancelToken))
						return false;

					if (targetState != desiredState)
						return true;

					if (listenStartDelayPending &&
					    feature.discoveryState != ColocationState.Active)
					{
						bool delayCompleted = await WaitForInterruptibleDelay(
							ListenStartDelaySeconds, cancelToken);
						if (!delayCompleted || targetState != desiredState)
							return true;

						listenStartDelayPending = false;
					}

					return await EnsureListeningStarted(feature, cancelToken);

				case State.Disable:
					if (!await EnsureAdvertisementStopped(feature, cancelToken))
						return false;

					if (targetState != desiredState)
						return true;

					return await EnsureListeningStopped(feature, cancelToken);

				case State.Advertise:
					if (!await EnsureListeningStopped(feature, cancelToken))
						return false;

					if (targetState != desiredState)
						return true;

					return await EnsureAdvertisementStarted(feature, cancelToken);

				default:
					return false;
			}
		}

		private bool StateIsSatisfied(State targetState)
		{
			ColocationDiscoveryFeature feature = Colocation;
			if (feature == null || !feature.enabled)
				return targetState == State.Disable;

			return targetState switch
			{
				State.Disable =>
					feature.discoveryState == ColocationState.Inactive &&
					feature.advertisementState == ColocationState.Inactive,
				State.Listen =>
					feature.discoveryState == ColocationState.Active &&
					feature.advertisementState == ColocationState.Inactive,
				State.Advertise =>
					feature.discoveryState == ColocationState.Inactive &&
					feature.advertisementState == ColocationState.Active,
				_ => false
			};
		}

		private async Task<bool> WaitForInterruptibleDelay(float seconds,
			CancellationToken cancelToken)
		{
			CancellationTokenSource delayCancellation =
				CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
			stateDelayCancellation = delayCancellation;

			try
			{
				await Awaitable.WaitForSecondsAsync(seconds, delayCancellation.Token);
				return true;
			}
			catch (OperationCanceledException) when (!cancelToken.IsCancellationRequested)
			{
				return false;
			}
			finally
			{
				if (ReferenceEquals(stateDelayCancellation, delayCancellation))
					stateDelayCancellation = null;

				delayCancellation.Dispose();
			}
		}

		private async Task<bool> EnsureListeningStarted(ColocationDiscoveryFeature feature,
			CancellationToken cancelToken)
		{
			if (feature.discoveryState == ColocationState.Active)
				return true;

			if (feature.discoveryState != ColocationState.Inactive)
				return false;

			cancelToken.ThrowIfCancellationRequested();
			nativeOperationInFlight = true;
			XRResultStatus status;
			try
			{
				status = await feature.TryStartDiscoveryAsync();
			}
			finally
			{
				nativeOperationInFlight = false;
			}

			cancelToken.ThrowIfCancellationRequested();

			if (status.IsSuccess())
			{
				Log("Listening started");
				return true;
			}

			LogWarning($"Couldn't start listening: {status}");
			return false;
		}

		private async Task<bool> EnsureListeningStopped(ColocationDiscoveryFeature feature,
			CancellationToken cancelToken)
		{
			if (feature.discoveryState == ColocationState.Inactive)
				return true;

			if (feature.discoveryState != ColocationState.Active)
				return false;

			cancelToken.ThrowIfCancellationRequested();
			nativeOperationInFlight = true;
			XRResultStatus status;
			try
			{
				status = await feature.TryStopDiscoveryAsync();
			}
			finally
			{
				nativeOperationInFlight = false;
			}

			cancelToken.ThrowIfCancellationRequested();

			if (status.IsSuccess())
			{
				Log("Listening halted");
				return true;
			}

			LogWarning($"Couldn't halt listening: {status}");
			return false;
		}

		private async Task<bool> EnsureAdvertisementStarted(ColocationDiscoveryFeature feature,
			CancellationToken cancelToken)
		{
			if (feature.advertisementState == ColocationState.Active)
				return true;

			if (feature.advertisementState != ColocationState.Inactive)
				return false;

			cancelToken.ThrowIfCancellationRequested();

			string message = "";

			UnityTransport transport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

			switch (transport.Protocol)
			{
				case UnityTransport.ProtocolType.UnityTransport:
					string address = transport.ConnectionData.Address;
					message = LanPrefix + address;
					break;

				case UnityTransport.ProtocolType.RelayUnityTransport:
					message = RelayPrefix + NetcodeManagement.CurrentSessionName;
					break;
			}

			nativeOperationInFlight = true;
			Result<SerializableGuid> result;
			try
			{
				result = await feature.TryStartAdvertisementAsync(Encoding.ASCII.GetBytes(message));
			}
			finally
			{
				nativeOperationInFlight = false;
			}

			cancelToken.ThrowIfCancellationRequested();

			if (result.status.IsSuccess())
			{
				Log($"Advertisement started '{message}'");
				return true;
			}

			LogWarning($"Couldn't start advertisement '{message}', {result.status}");
			return false;
		}

		private async Task<bool> EnsureAdvertisementStopped(ColocationDiscoveryFeature feature,
			CancellationToken cancelToken)
		{
			if (feature.advertisementState == ColocationState.Inactive)
				return true;

			if (feature.advertisementState != ColocationState.Active)
				return false;

			cancelToken.ThrowIfCancellationRequested();
			nativeOperationInFlight = true;
			XRResultStatus status;
			try
			{
				status = await feature.TryStopAdvertisementAsync();
			}
			finally
			{
				nativeOperationInFlight = false;
			}

			cancelToken.ThrowIfCancellationRequested();

			if (status.IsSuccess())
			{
				Log("Advertisement halted");
				return true;
			}

			LogWarning($"Couldn't halt advertisement: {status}");
			return false;
		}

		private void HandleDiscoveryStateChanged(object sender, Result<ColocationState> result)
		{
			if (!nativeOperationInFlight)
				RefreshState();
		}

		private void HandleAdvertisementStateChanged(object sender, Result<ColocationState> result)
		{
			if (!nativeOperationInFlight)
				RefreshState();
		}

		// Reconnecting too aggressively after a disconnect can churn the
		// session with rapid connect/disconnect cycles, leaving stale client
		// ids behind. Back off exponentially until a connection sticks.
		private const float MinReconnectDelay = 2;
		private const float MaxReconnectDelay = 30;
		private float reconnectDelay = MinReconnectDelay;
		private float nextConnectAllowedTime = 0;

		private void HandleMessageDiscovered(object sender, ColocationDiscoveryMessage discovered)
		{
			if (desiredState != State.Listen)
			{
				LogWarning("State isn't listening. This shouldn't run!");
				return;
			}

			// discovered.data is a NativeArray<byte> allocated with Allocator.Temp and
			// disposed at end of frame; copy it out before decoding.
			string message = Encoding.ASCII.GetString(discovered.data.ToArray());
			Log($"Discovered {message}");

			if (NetworkManager.Singleton.IsListening)
				return;

			if (Time.time < nextConnectAllowedTime)
				return;

			nextConnectAllowedTime = Time.time + reconnectDelay;
			reconnectDelay = Mathf.Min(reconnectDelay * 2, MaxReconnectDelay);

			if (message.StartsWith(LanPrefix))
				NetcodeManagement.ConnectLAN(message.Remove(0, LanPrefix.Length));
			else if (message.StartsWith(RelayPrefix))
				NetcodeManagement.ConnectUnityServices(message.Remove(0, RelayPrefix.Length));
		}
	}
}
