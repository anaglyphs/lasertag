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
using MetaColocationState = UnityEngine.XR.OpenXR.Features.Meta.ColocationState;

namespace Anaglyph.XR.SharedSpaces.SharedAnchors
{
	public class MetaSessionDiscovery : MonoBehaviour
	{
		public static MetaSessionDiscovery Instance { get; private set; }

		private const string LogHeader = "[SessionDiscovery] ";
		private const string LanPrefix = "IP:";
		private const string RelayPrefix = "Relay:";
		private const float RetryDelaySeconds = 1;

		private bool isPaused;
		private bool stateLoopRunning;
		private bool stateRefreshRequested;
		private Activity requestedActivity;

		private readonly CancellationTokenSource lifetimeCancellation = new();
		private CancellationTokenSource retryCancellation;
		private ColocationDiscoveryFeature colocationFeature;
		private ColocationDiscoveryFeature subscribedColocation;

		private ColocationDiscoveryFeature Colocation =>
			colocationFeature ??= OpenXRSettings.Instance != null
				? OpenXRSettings.Instance.GetFeature<ColocationDiscoveryFeature>()
				: null;

		public enum Activity
		{
			Disabled,
			Listening,
			Advertising
		}

		public void SetActivity(Activity activity)
		{
			if (requestedActivity == activity)
				return;

			requestedActivity = activity;
			if (activity == Activity.Advertising)
				reconnectDelay = MinReconnectDelay;
			RequestStateRefresh();
		}

		private static void Log(string message)
		{
			Debug.Log(LogHeader + message);
		}

		private static void LogWarning(string message)
		{
			Debug.LogWarning(LogHeader + message);
		}

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			SubscribeToEvents(true);
			RequestStateRefresh();
		}

		private void OnEnable()
		{
			if (!didStart)
				return;

			SubscribeToEvents(true);
			RequestStateRefresh();
		}

		private void OnDisable()
		{
			SubscribeToEvents(false);
			RequestStateRefresh();
		}

		private void OnDestroy()
		{
			SubscribeToEvents(false);
			lifetimeCancellation.Cancel();
			retryCancellation?.Cancel();

			if (Instance == this)
				Instance = null;
		}

		private void OnApplicationPause(bool paused)
		{
			isPaused = paused;

			if (!didStart)
				return;

			SubscribeToEvents(!paused);
			RequestStateRefresh();
		}

		private void SubscribeToEvents(bool subscribe)
		{
			if (subscribe)
			{
				ColocationDiscoveryFeature feature = Colocation;
				if (feature != null && feature != subscribedColocation)
				{
					if (subscribedColocation != null)
						UnsubscribeFromColocation(subscribedColocation);

					subscribedColocation = feature;
					feature.discoveryStateChanged += OnNativeStateChanged;
					feature.advertisementStateChanged += OnNativeStateChanged;
					feature.messageDiscovered += HandleMessageDiscovered;
				}
			}
			else
			{
				if (subscribedColocation != null)
				{
					UnsubscribeFromColocation(subscribedColocation);
					subscribedColocation = null;
				}
			}
		}

		private void UnsubscribeFromColocation(ColocationDiscoveryFeature feature)
		{
			feature.discoveryStateChanged -= OnNativeStateChanged;
			feature.advertisementStateChanged -= OnNativeStateChanged;
			feature.messageDiscovered -= HandleMessageDiscovered;
		}

		private void OnNativeStateChanged(object sender, Result<MetaColocationState> result)
		{
			RequestStateRefresh();
		}

		private Activity GetDesiredActivity()
		{
			return enabled && !isPaused ? requestedActivity : Activity.Disabled;
		}

		private void RequestStateRefresh()
		{
			if (!didStart)
				return;

			stateRefreshRequested = true;
			retryCancellation?.Cancel();

			if (!stateLoopRunning)
				ReconcileStateLoop(lifetimeCancellation.Token);
		}

		private async void ReconcileStateLoop(CancellationToken cancelToken)
		{
			if (stateLoopRunning)
				return;

			stateLoopRunning = true;

			try
			{
				while (!cancelToken.IsCancellationRequested)
				{
					stateRefreshRequested = false;
					Activity targetActivity = GetDesiredActivity();
					bool succeeded;

					try
					{
						succeeded = await ReconcileState(targetActivity, cancelToken);
					}
					catch (OperationCanceledException) when (cancelToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception exception)
					{
						LogWarning($"State transition to {targetActivity} threw an exception; retrying");
						Debug.LogException(exception);
						succeeded = false;
					}

					cancelToken.ThrowIfCancellationRequested();

					if (stateRefreshRequested || targetActivity != GetDesiredActivity())
						continue;

					if (succeeded && ActivityIsSatisfied(targetActivity))
						break;

					await WaitForRetry(cancelToken);
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

		private async Task<bool> ReconcileState(Activity targetActivity,
			CancellationToken cancelToken)
		{
			ColocationDiscoveryFeature feature = Colocation;
			if (feature != null && feature != subscribedColocation)
				SubscribeToEvents(true);

			if (feature == null || !feature.enabled)
				return targetActivity == Activity.Disabled;

			switch (targetActivity)
			{
				case Activity.Listening:
					if (!await EnsureAdvertisementStopped(feature, cancelToken))
						return false;
					if (targetActivity != GetDesiredActivity())
						return true;
					return await EnsureListeningStarted(feature, cancelToken);

				case Activity.Advertising:
					if (!await EnsureListeningStopped(feature, cancelToken))
						return false;
					if (targetActivity != GetDesiredActivity())
						return true;
					return await EnsureAdvertisementStarted(feature, cancelToken);

				case Activity.Disabled:
					if (!await EnsureAdvertisementStopped(feature, cancelToken))
						return false;
					if (targetActivity != GetDesiredActivity())
						return true;
					return await EnsureListeningStopped(feature, cancelToken);

				default:
					return false;
			}
		}

		private bool ActivityIsSatisfied(Activity activity)
		{
			ColocationDiscoveryFeature feature = Colocation;
			if (feature == null || !feature.enabled)
				return activity == Activity.Disabled;

			return activity switch
			{
				Activity.Disabled =>
					feature.discoveryState == MetaColocationState.Inactive &&
					feature.advertisementState == MetaColocationState.Inactive,
				Activity.Listening =>
					feature.discoveryState == MetaColocationState.Active &&
					feature.advertisementState == MetaColocationState.Inactive,
				Activity.Advertising =>
					feature.discoveryState == MetaColocationState.Inactive &&
					feature.advertisementState == MetaColocationState.Active,
				_ => false
			};
		}

		private async Task WaitForRetry(CancellationToken cancelToken)
		{
			CancellationTokenSource delay =
				CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
			retryCancellation = delay;

			try
			{
				await Awaitable.WaitForSecondsAsync(RetryDelaySeconds, delay.Token);
			}
			catch (OperationCanceledException) when (!cancelToken.IsCancellationRequested)
			{
			}
			finally
			{
				if (ReferenceEquals(retryCancellation, delay))
					retryCancellation = null;

				delay.Dispose();
			}
		}

		private static async Task<bool> EnsureListeningStarted(
			ColocationDiscoveryFeature feature, CancellationToken cancelToken)
		{
			if (feature.discoveryState == MetaColocationState.Active)
				return true;
			if (feature.discoveryState != MetaColocationState.Inactive)
				return false;

			cancelToken.ThrowIfCancellationRequested();
			XRResultStatus status = await feature.TryStartDiscoveryAsync();
			cancelToken.ThrowIfCancellationRequested();

			if (status.IsSuccess())
			{
				Log("Listening started");
				return true;
			}

			LogWarning($"Couldn't start listening: {status}");
			return false;
		}

		private static async Task<bool> EnsureListeningStopped(
			ColocationDiscoveryFeature feature, CancellationToken cancelToken)
		{
			if (feature.discoveryState == MetaColocationState.Inactive)
				return true;
			if (feature.discoveryState != MetaColocationState.Active)
				return false;

			cancelToken.ThrowIfCancellationRequested();
			XRResultStatus status = await feature.TryStopDiscoveryAsync();
			cancelToken.ThrowIfCancellationRequested();

			if (status.IsSuccess())
			{
				Log("Listening halted");
				return true;
			}

			LogWarning($"Couldn't halt listening: {status}");
			return false;
		}

		private static async Task<bool> EnsureAdvertisementStarted(
			ColocationDiscoveryFeature feature, CancellationToken cancelToken)
		{
			if (feature.advertisementState == MetaColocationState.Active)
				return true;
			if (feature.advertisementState != MetaColocationState.Inactive)
				return false;

			cancelToken.ThrowIfCancellationRequested();

			UnityTransport transport =
				(UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
			string message = transport.Protocol switch
			{
				UnityTransport.ProtocolType.UnityTransport =>
					LanPrefix + transport.ConnectionData.Address,
				UnityTransport.ProtocolType.RelayUnityTransport =>
					RelayPrefix + NetcodeManagement.CurrentSessionName,
				_ => ""
			};

			Result<SerializableGuid> result =
				await feature.TryStartAdvertisementAsync(Encoding.ASCII.GetBytes(message));
			cancelToken.ThrowIfCancellationRequested();

			if (result.status.IsSuccess())
			{
				Log($"Advertisement started '{message}'");
				return true;
			}

			LogWarning($"Couldn't start advertisement '{message}', {result.status}");
			return false;
		}

		private static async Task<bool> EnsureAdvertisementStopped(
			ColocationDiscoveryFeature feature, CancellationToken cancelToken)
		{
			if (feature.advertisementState == MetaColocationState.Inactive)
				return true;
			if (feature.advertisementState != MetaColocationState.Active)
				return false;

			cancelToken.ThrowIfCancellationRequested();
			XRResultStatus status = await feature.TryStopAdvertisementAsync();
			cancelToken.ThrowIfCancellationRequested();

			if (status.IsSuccess())
			{
				Log("Advertisement halted");
				return true;
			}

			LogWarning($"Couldn't halt advertisement: {status}");
			return false;
		}

		private const float MinReconnectDelay = 2;
		private const float MaxReconnectDelay = 30;
		private float reconnectDelay = MinReconnectDelay;
		private float nextConnectAllowedTime;

		private void HandleMessageDiscovered(object sender,
			ColocationDiscoveryMessage discovered)
		{
			if (GetDesiredActivity() != Activity.Listening)
				return;

			string message = Encoding.ASCII.GetString(discovered.data.ToArray());
			Log($"Discovered {message}");

			if (NetworkManager.Singleton.IsListening || Time.time < nextConnectAllowedTime)
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
