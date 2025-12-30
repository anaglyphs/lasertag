using Anaglyph.Netcode;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaSessionDiscovery : MonoBehaviour
	{
		public static MetaSessionDiscovery Instance { get; private set; }
		private NetworkManager NetMan => NetworkManager.Singleton;

		private const string LogHeader = "[SessionDiscovery] ";

		private bool isListening;
		private bool isAdvertising;

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
			if (Instance == null)
				Instance = this;
			else
				throw new Exception($"More than one instance of {typeof(MetaSessionDiscovery)}!");
		}

		private void Start()
		{
			UpdateState();
			SubscribeToEvents(enabled);
		}

		private void OnEnable()
		{
			if (didStart)
			{
				UpdateState();
				SubscribeToEvents(enabled);
			}
		}

		private void OnDisable()
		{
			UpdateState();
			SubscribeToEvents(enabled);
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
				OVRColocationSession.ColocationSessionDiscovered += HandleColocationSessionDiscovered;
			}
			else
			{
				NetMan.OnSessionOwnerPromoted -= OnSessionOwnerPromoted;
				NetcodeManagement.StateChanged -= OnNetworkStateChange;
				OVRColocationSession.ColocationSessionDiscovered -= HandleColocationSessionDiscovered;
			}

			isSubscribed = shouldSubscribe;
		}

		private void OnSessionOwnerPromoted(ulong clientId)
		{
			UpdateState();
		}

		private void OnNetworkStateChange(NetcodeState state)
		{
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
						var isHost = NetMan.CurrentSessionOwner == NetMan.LocalClientId;
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
				var ctkn = PrepareNextTask();

				switch (state)
				{
					case State.Listen:
						await HaltAdvertisement(ctkn);
						await Task.Delay(3000, ctkn);
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

			var result = await OVRColocationSession.StartDiscoveryAsync();

			if (result.Success)
			{
				isListening = true;
				Log("Listening started");
			}
			else
			{
				LogWarning($"Couldn't start listening: {result.Status}");
			}
		}

		private async Task HaltListening(CancellationToken cancelToken)
		{
			if (!isListening) return;

			cancelToken.ThrowIfCancellationRequested();

			var result = await OVRColocationSession.StopDiscoveryAsync();

			if (result.Success)
			{
				isListening = false;
				Log("Listening halted");
			}
			else
			{
				LogWarning($"Couldn't halt listening: {result.Status}");
			}
		}

		private async Task StartAdvertisement(CancellationToken cancelToken)
		{
			cancelToken.ThrowIfCancellationRequested();

			var message = "";

			var transport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

			switch (transport.Protocol)
			{
				case UnityTransport.ProtocolType.UnityTransport:
					var address = transport.ConnectionData.Address;
					message = LanPrefix + address;
					break;

				case UnityTransport.ProtocolType.RelayUnityTransport:
					message = RelayPrefix + NetcodeManagement.CurrentSessionName;
					break;
			}

			var result = await OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(message));
			if (result.Success)
			{
				isAdvertising = true;
				Log($"Advertisement started '{message}'");
			}
			else
			{
				LogWarning($"Couldn't start advertisement '{message}', {result.Status}");
			}
		}

		private async Task HaltAdvertisement(CancellationToken cancelToken)
		{
			if (!isAdvertising) return;

			cancelToken.ThrowIfCancellationRequested();

			var result = await OVRColocationSession.StopAdvertisementAsync();

			if (result.Success)
			{
				isAdvertising = false;
				Log("Advertisement halted");
			}
			else
			{
				LogWarning($"Couldn't halt advertisement: {result.Status}");
			}
		}

		private void HandleColocationSessionDiscovered(OVRColocationSession.Data data)
		{
			if (state != State.Listen) LogWarning("State isn't listening. This shouldn't run!");

			var message = Encoding.ASCII.GetString(data.Metadata);
			Log($"Discovered {message}");

			if (NetworkManager.Singleton.IsListening)
				return;

			if (message.StartsWith(LanPrefix))
				NetcodeManagement.ConnectLAN(message.Remove(0, LanPrefix.Length));
			else if (message.StartsWith(RelayPrefix))
				NetcodeManagement.ConnectUnityServices(message.Remove(0, RelayPrefix.Length));
		}
	}
}