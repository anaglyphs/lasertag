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

		private const string LogHeader = "[SessionDiscovery] ";
		private static void Log(string str) => Debug.Log(LogHeader + str);
		private static void LogWarning(string str) => Debug.LogWarning(LogHeader + str);

		private const string LanPrefix = "IP:";
		private const string RelayPrefix = "Relay:";

		private void Awake()
		{
			if(Instance == null)
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
				NetcodeManagement.StateChanged += OnNetworkStateChange;
				OVRColocationSession.ColocationSessionDiscovered += HandleColocationSessionDiscovered;
			} else
			{
				NetcodeManagement.StateChanged -= OnNetworkStateChange;
				OVRColocationSession.ColocationSessionDiscovered -= HandleColocationSessionDiscovered;
			}

			isSubscribed = shouldSubscribe;
		}

		private void OnNetworkStateChange(NetcodeState state)
		{
			UpdateState();
		}

		private bool isPaused = false;
		private void OnApplicationPause(bool isPaused) {
			
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

		public enum State
		{
			Disabled,
			NetcodeDisconnected,
			NetcodeConnecting,
			NetcodeConnected,
		}

		private State state = State.Disabled;
		private async void UpdateState()
		{
			State newState = default;

			if (enabled && !isPaused)
			{
				switch (NetcodeManagement.State)
				{
					case NetcodeState.Disconnected:
						newState = State.NetcodeDisconnected;
						break;
					case NetcodeState.Connecting:
						newState = State.NetcodeConnecting;
						break;
					case NetcodeState.Connected:
						newState = State.NetcodeConnected;
						break;
				}
			}
			else
			{
				newState = State.Disabled;
			}

			// only update if state has changed
			if (newState == state)
				return;

			state = newState;

			try
			{
				var ctkn = PrepareNextTask();

				switch (state)
				{
					case State.Disabled:
						await HaltAdvertisement(ctkn);
						await HaltDiscovery(ctkn);
						break;

					case State.NetcodeDisconnected:
						await HaltAdvertisement(ctkn);
						await Task.Delay(1000, ctkn);
						await StartDiscovery(ctkn);
						break;

					case State.NetcodeConnecting:
						await HaltDiscovery(ctkn);
						break;

					case State.NetcodeConnected:
						await HaltDiscovery(ctkn);
						await StartAdvertisement(ctkn);
						break;
				}

			}
			catch (OperationCanceledException) { }
		}

		private async Task StartDiscovery(CancellationToken cancelToken)
		{
			cancelToken.ThrowIfCancellationRequested();

			var result = await OVRColocationSession.StartDiscoveryAsync();

			if (result.Success)
				Log("Discovery started");
			else
				LogWarning($"Couldn't start discovery: {result.Status}");
		}

		private async Task HaltDiscovery(CancellationToken cancelToken)
		{
			cancelToken.ThrowIfCancellationRequested();

			var result = await OVRColocationSession.StopDiscoveryAsync();

			if (result.Success)
				Log("Discovery halted");
			else
				LogWarning($"Couldn't halt discovery: {result.Status}");
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

			var result = await OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(message));
			if (result.Success)
				Log($"Advertisement started '{message}'");
			else
				LogWarning($"Couldn't start advertisement '{message}', {result.Status}");
		}

		private async Task HaltAdvertisement(CancellationToken cancelToken)
		{
			cancelToken.ThrowIfCancellationRequested();

			var result = await OVRColocationSession.StopAdvertisementAsync();

			if (result.Success)
				Log("Advertisement halted");
			else
				LogWarning($"Couldn't halt advertisement: {result.Status}");
		}

		private void HandleColocationSessionDiscovered(OVRColocationSession.Data data)
		{
			string message = Encoding.ASCII.GetString(data.Metadata);
			Log($"Discovered {message}");

			if (NetworkManager.Singleton.IsListening)
				return;

			if(message.StartsWith(LanPrefix))
			{
				NetcodeManagement.ConnectLAN(message.Remove(0, LanPrefix.Length));
			} else if(message.StartsWith(RelayPrefix))
			{
				NetcodeManagement.ConnectUnityServices(message.Remove(0, RelayPrefix.Length));
			}
		}
	}
}