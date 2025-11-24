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
	public enum NetcodeState
	{
		Disconnected = 0,
		Connecting,
		Connected,
	}

	public static class NetcodeManagement
	{
		public static ushort port = 7777;
		public static string contyp = "dtls";

		private static float cooldownDoneTime = 0;
		public const float cooldownSeconds = 8;

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport =>
			(UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		public enum Protocol
		{
			LAN,
			UnityService,
		}

		private static NetcodeState _state = NetcodeState.Disconnected;
		public static event Action<NetcodeState> StateChanged = delegate { };
		public static NetcodeState State
		{
			get => _state;
			private set
			{
				bool changed = value != _state;
				_state = value;
				if (changed)
					StateChanged?.Invoke(_state);
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void OnSceneLoad()
		{
			if (!manager) return;
			
			manager.OnClientStarted += () => State = NetcodeState.Connecting;
			manager.OnClientStopped += _ => State = NetcodeState.Disconnected;
			manager.OnConnectionEvent += OnConnectionEvent;
			manager.OnTransportFailure += () => State = NetcodeState.Disconnected;
		}

		private static void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (ThisClientConnected(data))
			{
				State = NetcodeState.Connected;
			}
			else if (ThisClientDisconnected(data))
			{
				State = NetcodeState.Disconnected;
			}
		}

		private static CancellationTokenSource taskCanceller = new();

		private static CancellationToken PrepareNextTask()
		{
			taskCanceller?.Cancel();
			taskCanceller = new CancellationTokenSource();
			return taskCanceller.Token;
		}

		private static void SetNetworkTransportType(Protocol protocol)
		{
			if (State != NetcodeState.Disconnected)
				throw new Exception("You can only change the transport while disconnected!");

			UnityTransport newTransport;

			switch (protocol)
			{
				case Protocol.LAN:
					newTransport = manager.GetComponent<UnityTransport>();
					break;

				case Protocol.UnityService:
					newTransport = manager.GetComponent("DistributedAuthorityTransport") as UnityTransport;
					Debug.Log(newTransport.name);
					break;

				default:
					return;
			}

			if (newTransport == null)
				throw new Exception($"Could not find transport!");

			newTransport.GetNetworkDriver().Dispose();

			manager.NetworkConfig.NetworkTransport = newTransport;
		}

		public static void Host(Protocol protocol)
		{
			switch (protocol)
			{
				case Protocol.LAN:
					SetNetworkTransportType(Protocol.LAN);
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

					_ = ConnectUnityServices(DateTime.Now.ToString("HHmmssffff"), PrepareNextTask());
					break;
			}
		}

		public static void ConnectLAN(string ip)
		{
			SetNetworkTransportType(Protocol.LAN);

			manager.NetworkConfig.UseCMBService = false;

			transport.SetConnectionData(ip, port);

			State = NetcodeState.Connecting;

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
			_ = ConnectUnityServices(id, PrepareNextTask());
		}

		private static async Task ConnectUnityServices(string id, CancellationToken ct)
		{
			if (State != NetcodeState.Disconnected)
				return;
			
			SetNetworkTransportType(Protocol.UnityService);

			manager.NetworkConfig.UseCMBService = true;

			State = NetcodeState.Connecting;

			if (Time.time < cooldownDoneTime)
			{
				float waitTime = cooldownDoneTime - Time.time;
				await Awaitable.WaitForSecondsAsync(waitTime);
			}

			ct.ThrowIfCancellationRequested();

			cooldownDoneTime = Time.time + cooldownSeconds;

			await SetupServices();

			ct.ThrowIfCancellationRequested();

			var options = new SessionOptions()
			{
				Name = id,
				MaxPlayers = 20,
			}.WithDistributedAuthorityNetwork();

			CurrentSessionName = id;
			CurrentSession = await MultiplayerService.Instance.CreateOrJoinSessionAsync(id, options);
		}

		public static async void Disconnect()
		{
			taskCanceller?.Cancel();

			State = NetcodeState.Disconnected;

			try
			{
				if (CurrentSession != null)
					await CurrentSession.LeaveAsync();

				CurrentSession = null;
			}
			catch (SessionException)
			{

			}

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

		public static bool GetNetObjById(ulong id, out NetworkObject netObj) =>
			NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(id, out netObj);
	}
}
