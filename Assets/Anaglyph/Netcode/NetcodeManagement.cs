using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
		Connected
	}

	public static class NetcodeManagement
	{
		public enum Protocol
		{
			LAN,
			UnityService
		}

		public const float cooldownSeconds = 8;

		private static NetworkManager manager => NetworkManager.Singleton;

		private static UnityTransport transport =>
			(UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		public const ushort port = 7777;
		public const string DefaultIP = "0.0.0.0";

		private static float cooldownDoneTime = 0;

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

		public static ISession CurrentSession { get; private set; }
		public static string CurrentSessionName { get; private set; } = "";

		// statics persist across play sessions while domain reload is disabled
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init()
		{
			taskCanceller?.Cancel();
			taskCanceller = new CancellationTokenSource();
			cooldownDoneTime = 0;

			_state = NetcodeState.Disconnected;
			StateChanged = delegate { };

			CurrentSession = null;
			CurrentSessionName = "";
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
				State = NetcodeState.Connected;
			else if (ThisClientDisconnected(data)) State = NetcodeState.Disconnected;
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

		public static async void Host(Protocol protocol)
		{
			switch (protocol)
			{
				case Protocol.LAN:
					SetNetworkTransportType(Protocol.LAN);
					manager.NetworkConfig.UseCMBService = false;
					transport.SetConnectionData(GetLocalIPv4(), port, DefaultIP);
					manager.StartHost();
					break;

				case Protocol.UnityService:

					try
					{
						await ConnectUnityServices(DateTime.Now.ToString("HHmmssffff"), PrepareNextTask());
					}
					catch (Exception e)
					{
						State = NetcodeState.Disconnected;
						Log($"Failed to connect to Unity services!", LogType.Error);
						Debug.LogException(e);
					}

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

		public static async void ConnectUnityServices(string id)
		{
			try
			{
				await ConnectUnityServices(id, PrepareNextTask());
			}
			catch (Exception e)
			{
				State = NetcodeState.Disconnected;
				Log($"Failed to connect to Unity services!", LogType.Error);
				Debug.LogException(e);
			}
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

			SessionOptions options = new SessionOptions
			{
				Name = id,
				MaxPlayers = 20
			}.WithDistributedAuthorityNetwork();

			CurrentSessionName = id;
			CurrentSession = await MultiplayerService.Instance.CreateOrJoinSessionAsync(id, options);
			CurrentSession.RemovedFromSession += delegate
			{
				manager.Shutdown();
				CurrentSession = null;
			};
		}

		public static async void Disconnect()
		{
			taskCanceller?.Cancel();

			State = NetcodeState.Disconnected;

			try
			{
				if (CurrentSession != null)
					await CurrentSession.LeaveAsync();
			}
			catch (SessionException e)
			{
				Debug.LogException(e);
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
			IPAddress privateAddress = null;
			IPAddress fallbackAddress = null;

			foreach (NetworkInterface netInterface in NetworkInterface.GetAllNetworkInterfaces())
			{
				// Unity/Android may report Unknown for an active Wi-Fi interface.
				if (netInterface.OperationalStatus != OperationalStatus.Up &&
				    netInterface.OperationalStatus != OperationalStatus.Unknown) continue;

				NetworkInterfaceType netType = netInterface.NetworkInterfaceType;

				// Android may also report Wi-Fi as Unknown, so only reject interface
				// types that definitely cannot provide a reachable LAN address.
				if (netType == NetworkInterfaceType.Loopback || netType == NetworkInterfaceType.Tunnel) continue;

				foreach (UnicastIPAddressInformation addressInfo in netInterface.GetIPProperties().UnicastAddresses)
				{
					IPAddress address = addressInfo.Address;

					if (address.AddressFamily != AddressFamily.InterNetwork ||
					    IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any)) continue;

					byte[] b = address.GetAddressBytes();
					if (b[0] == 169 && b[1] == 254) continue; // link-local = no DHCP lease

					bool isPrivate = b[0] == 10 ||
					                 (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
					                 (b[0] == 192 && b[1] == 168);

					if (isPrivate)
					{
						// Prefer a positively identified physical LAN interface, but retain
						// Unknown as the Android/Quest-compatible fallback.
						if (netType == NetworkInterfaceType.Wireless80211 ||
						    netType == NetworkInterfaceType.Ethernet)
							return address.ToString();

						privateAddress ??= address;
					}

					fallbackAddress ??= address;
				}
			}

			return (privateAddress ?? fallbackAddress)?.ToString();
		}

		public static bool GetNetObjById(ulong id, out NetworkObject netObj)
		{
			return NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(id, out netObj);
		}

		private static void Log(string str, LogType logType = LogType.Log)
		{
			Debug.unityLogger.Log($"[{nameof(NetcodeManagement)}] {str}", logType);
		}
	}
}
