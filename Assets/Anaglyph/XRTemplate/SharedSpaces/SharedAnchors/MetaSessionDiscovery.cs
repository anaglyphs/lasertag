using Anaglyph.Netcode;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaSessionDiscovery : MonoBehaviour
	{
		public static MetaSessionDiscovery Instance { get; private set; }
		private NetworkManager NetMan => NetworkManager.Singleton;

		private const string LogHeader = "[SessionDiscovery] ";

		private bool isListening;
		private bool isAdvertising;

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
			UpdateState();
			SubscribeToEvents(true);
		}

		private void OnEnable()
		{
			if (!didStart) return;
			UpdateState();
			SubscribeToEvents(true);
		}

		private void OnDisable()
		{
			UpdateState();
			SubscribeToEvents(false);
		}

		private bool isSubscribed = false;

		private void SubscribeToEvents(bool shouldSubscribe)
		{
			if (shouldSubscribe == isSubscribed)
				return;

			if (shouldSubscribe)
			{
				NetMan.OnSessionOwnerPromoted += OnSessionOwnerPromoted;
				NetcodeManagement.StateChanged += OnNetworkStateChange;
				if (Colocation != null)
					Colocation.messageDiscovered += HandleMessageDiscovered;
			}
			else
			{
				if (NetMan)
					NetMan.OnSessionOwnerPromoted -= OnSessionOwnerPromoted;

				NetcodeManagement.StateChanged -= OnNetworkStateChange;
				if (Colocation != null)
					Colocation.messageDiscovered -= HandleMessageDiscovered;
			}

			isSubscribed = shouldSubscribe;
		}

		private void OnSessionOwnerPromoted(ulong clientId)
		{
			UpdateState();
		}

		private void OnNetworkStateChange(NetcodeState state)
		{
			if (state == NetcodeState.Connected)
				reconnectDelay = MinReconnectDelay;

			UpdateState();
		}

		private bool isPaused = false;

		private void OnApplicationPause(bool isPaused)
		{
			this.isPaused = isPaused;

			if (didStart)
			{
				SubscribeToEvents(!this.isPaused);
				UpdateState();
			}
		}

		private CancellationTokenSource taskCanceller = new();

		private CancellationToken PrepareNextTask()
		{
			taskCanceller.Cancel();
			taskCanceller = new CancellationTokenSource();
			return taskCanceller.Token;
		}

		private enum State
		{
			Disable,
			Listen,
			Advertise
		}

		private State state = State.Disable;

		private async void UpdateState()
		{
			State newState = default;

			if (enabled && !isPaused)
				switch (NetcodeManagement.State)
				{
					case NetcodeState.Disconnected:
						newState = State.Listen;
						break;
					case NetcodeState.Connecting:
						newState = State.Disable;
						break;
					case NetcodeState.Connected:
						bool isHost = NetMan.CurrentSessionOwner == NetMan.LocalClientId;
						newState = isHost ? State.Advertise : State.Disable;
						break;
				}
			else
				newState = State.Disable;

			// only update if state has changed
			if (newState == state) return;
			state = newState;

			try
			{
				CancellationToken ctkn = PrepareNextTask();

				switch (state)
				{
					case State.Listen:
						await HaltAdvertisement(ctkn);
						await Awaitable.WaitForSecondsAsync(3, ctkn);
						await StartListening(ctkn);
						break;

					case State.Disable:
						await HaltAdvertisement(ctkn);
						await HaltListening(ctkn);
						break;

					case State.Advertise:
						await HaltListening(ctkn);
						await StartAdvertisement(ctkn);
						break;
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private async Task StartListening(CancellationToken cancelToken)
		{
			if (isListening) return;

			cancelToken.ThrowIfCancellationRequested();

			if (Colocation == null)
			{
				LogWarning("Couldn't start listening: colocation feature unavailable");
				return;
			}

			XRResultStatus status = await Colocation.TryStartDiscoveryAsync();

			if (status.IsSuccess())
			{
				isListening = true;
				Log("Listening started");
			}
			else
			{
				LogWarning($"Couldn't start listening: {status}");
			}
		}

		private async Task HaltListening(CancellationToken cancelToken)
		{
			if (!isListening) return;

			cancelToken.ThrowIfCancellationRequested();

			if (Colocation == null)
			{
				isListening = false;
				return;
			}

			XRResultStatus status = await Colocation.TryStopDiscoveryAsync();

			if (status.IsSuccess())
			{
				isListening = false;
				Log("Listening halted");
			}
			else
			{
				LogWarning($"Couldn't halt listening: {status}");
			}
		}

		private async Task StartAdvertisement(CancellationToken cancelToken)
		{
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

			if (Colocation == null)
			{
				LogWarning($"Couldn't start advertisement '{message}': colocation feature unavailable");
				return;
			}

			var result = await Colocation.TryStartAdvertisementAsync(Encoding.ASCII.GetBytes(message));
			if (result.status.IsSuccess())
			{
				isAdvertising = true;
				Log($"Advertisement started '{message}'");
			}
			else
			{
				LogWarning($"Couldn't start advertisement '{message}', {result.status}");
			}
		}

		private async Task HaltAdvertisement(CancellationToken cancelToken)
		{
			if (!isAdvertising) return;

			cancelToken.ThrowIfCancellationRequested();

			if (Colocation == null)
			{
				isAdvertising = false;
				return;
			}

			XRResultStatus status = await Colocation.TryStopAdvertisementAsync();

			if (status.IsSuccess())
			{
				isAdvertising = false;
				Log("Advertisement halted");
			}
			else
			{
				LogWarning($"Couldn't halt advertisement: {status}");
			}
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
			if (state != State.Listen)
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