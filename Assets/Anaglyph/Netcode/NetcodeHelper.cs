using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Anaglyph.Netcode
{
	public static class NetcodeHelper
	{
		public static ushort port = 7777;
		public static string contyp = "dtls";

		private static float cooldownDoneTime = 0;
		public const float cooldownSeconds = 8;

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		public enum NetworkState
		{
			Disconnected = 0,
			Connecting,
			Connected,
		}

		public enum Protocol
		{
			LAN,
			UnityService,
		}

		private static NetworkState _state;
		public static event Action<NetworkState> StateChange = delegate { };
		public static NetworkState State
		{
			get => _state;
			private set
			{
				bool changed = value != _state;
				_state = value;
				if (changed)
					StateChange?.Invoke(_state);
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void OnSceneLoad()
		{
			manager.OnClientStarted += () => State = NetworkState.Connecting;
			manager.OnClientStopped += _ => State = NetworkState.Disconnected;
			manager.OnConnectionEvent += OnConnectionEvent;
		}

		private static void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if(NetcodeHelper.ThisClientConnected(data))
			{
				State = NetworkState.Connected;
			} else if(NetcodeHelper.ThisClientDisconnected(data))
			{
				State = NetworkState.Disconnected;
			}
		}

		private static Task currentTask;
		private static CancellationTokenSource CancelTokenSource = new();

		private static void SetNetworkTransportType(string s)
		{
			if (State != NetworkState.Disconnected)
				throw new Exception("You can only change the transport while disconnected!");

			NetworkTransport newTransport = manager.GetComponent(s) as NetworkTransport;
			if (newTransport == null)
				throw new Exception($"Could not find transport {s}!");

			manager.NetworkConfig.NetworkTransport = newTransport;
		}

		public static void Host(Protocol protocol)
		{
			if (currentTask != null && !currentTask.IsCompleted)
				return;

			currentTask = Host(protocol, CancelTokenSource.Token);
		}

		private static async Task Host(Protocol protocol, CancellationToken ct)
		{
			switch (protocol)
			{
				case Protocol.LAN:

					SetNetworkTransportType("UnityTransport");
					manager.NetworkConfig.UseCMBService = false;

					string localAddress = "";
					var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
					foreach (var address in addresses)
					{
						if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
						{
							localAddress = address.ToString();
							break;
						}
					}

					transport.SetConnectionData(localAddress, port);
					manager.StartHost();
					break;

				case Protocol.UnityService:

					await ConnectUnityServices(DateTime.Now.ToString("HHmmssffff"), ct);

					break;
			}
		}

		public static void ConnectLAN(string ip)
		{
			State = NetworkState.Connecting;

			SetNetworkTransportType("UnityTransport");
			manager.NetworkConfig.UseCMBService = false;

			transport.SetConnectionData(ip, port);

			manager.StartClient();
		}

		private static async Task SetupServices()
		{
			if (UnityServices.State == ServicesInitializationState.Uninitialized)
				await UnityServices.InitializeAsync();

			if (!AuthenticationService.Instance.IsSignedIn)
				await AuthenticationService.Instance.SignInAnonymouslyAsync();
		}

		public static ISession CurrentSession { get; private set; }
		public static string CurrentSessionName { get; private set; } = "";

		public static void ConnectUnityServices(string id)
		{
			if (currentTask != null && !currentTask.IsCompleted)
				return;

			currentTask = ConnectUnityServices(id, CancelTokenSource.Token);
		}

		private static async Task ConnectUnityServices(string id, CancellationToken ct)
		{
			SetNetworkTransportType("DistributedAuthorityTransport");
			manager.NetworkConfig.UseCMBService = true;

			State = NetworkState.Connecting;

			if (Time.time < cooldownDoneTime) {
				float waitTime = cooldownDoneTime - Time.time;
				await Awaitable.WaitForSecondsAsync(waitTime);
			}

			if (ct.IsCancellationRequested) return;

			cooldownDoneTime = Time.time + cooldownSeconds;

			await SetupServices();

			if (ct.IsCancellationRequested) return;

			var options = new SessionOptions()
			{
				Name = id,
				MaxPlayers = 20,
			}.WithDistributedAuthorityNetwork();

			CurrentSessionName = id;
			CurrentSession = await MultiplayerService.Instance.CreateOrJoinSessionAsync(id, options);
			if (ct.IsCancellationRequested) Disconnect();
		}

		public static async void Disconnect()
		{
			if(currentTask != null && !currentTask.IsCompleted)
				CancelTokenSource.Cancel();

			try
			{
				if (CurrentSession != null)
					await CurrentSession.LeaveAsync();

				CurrentSession = null;
			}
			catch (SessionException)
			{

			}

			State = NetworkState.Disconnected;

			manager.Shutdown();
			
		}

		public static bool ThisClientConnected(ConnectionEventData data)
		{
			return data.EventType == ConnectionEvent.ClientConnected &&
				data.ClientId == NetworkManager.Singleton.LocalClientId;
		}

		public static bool ThisClientDisconnected(ConnectionEventData data)
		{
			return data.EventType == ConnectionEvent.ClientDisconnected &&
				data.ClientId == NetworkManager.Singleton.LocalClientId;
		}

		public static string GetLocalIPv4()
		{
			var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
			foreach (var address in addresses)
			{
				if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
				{
					return address.ToString();
				}
			}

			return null;
		}
	}
}
