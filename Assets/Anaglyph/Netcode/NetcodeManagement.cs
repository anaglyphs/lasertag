using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
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

		// Marks a rejection as a version mismatch and carries the host's version.
		// Sent as the netcode disconnect reason, so the client can say which
		// version it needs instead of just "couldn't connect".
		private const string versionMismatchPrefix = "VERSION_MISMATCH:";

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

		/// <summary>
		/// Identifies this build to hosts and joiners. Two headsets can only play
		/// together if these match. Set by whoever owns the build number asset.
		/// </summary>
		public static string GameVersion { get; set; } = Application.version;

		// A disconnect only means "couldn't join" while an attempt is in flight.
		private static bool isAttemptingConnection;

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
			GameVersion = Application.version;
			isAttemptingConnection = false;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void OnSceneLoad()
		{
			if (!manager) return;

			manager.OnClientStarted += () => State = NetcodeState.Connecting;
			manager.OnClientStopped += _ => EndAttempt();
			manager.OnConnectionEvent += OnConnectionEvent;
			manager.OnTransportFailure += EndAttempt;

			if (manager.ConnectionApprovalCallback == null)
				manager.ConnectionApprovalCallback = ApproveJoiningClient;
		}

		private static void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (ThisClientConnected(data))
			{
				isAttemptingConnection = false;
				State = NetcodeState.Connected;
			}
			else if (ThisClientDisconnected(data)) EndAttempt();
		}

		private static void EndAttempt()
		{
			bool failedToJoin = isAttemptingConnection;
			isAttemptingConnection = false;

			// closes the session page before the error page opens over the menu
			State = NetcodeState.Disconnected;

			if (failedToJoin)
				RaiseJoinFailureError();
		}

		/// <summary>
		/// Rejects joiners built from a different version of the game. Only runs on
		/// a LAN host — relay sessions are approved by the multiplayer service.
		/// </summary>
		private static void ApproveJoiningClient(NetworkManager.ConnectionApprovalRequest request,
			NetworkManager.ConnectionApprovalResponse response)
		{
			string joinerVersion = request.Payload == null
				? ""
				: Encoding.UTF8.GetString(request.Payload);

			if (joinerVersion != GameVersion)
			{
				response.Approved = false;
				response.Reason = versionMismatchPrefix + GameVersion;
				return;
			}

			response.Approved = true;
			response.CreatePlayerObject = manager.NetworkConfig.PlayerPrefab != null;
		}

		private static void RaiseJoinFailureError()
		{
			string reason = manager == null ? "" : manager.DisconnectReason;
			int prefixIndex = reason == null ? -1 : reason.IndexOf(versionMismatchPrefix, StringComparison.Ordinal);

			if (prefixIndex >= 0)
			{
				string hostVersion = reason.Substring(prefixIndex + versionMismatchPrefix.Length).Trim();

				UserErrors.Raise("Different version of the game",
					$"This host is running version {hostVersion} and you're running {GameVersion}. " +
					"Both headsets need the same version to play together.");

				return;
			}

			UserErrors.Raise("Couldn't join",
				"The host didn't accept the connection. It may be running a different version " +
				"of the game, may have stopped hosting, or may be at a different address.");
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
					// DistributedAuthorityTransport is annoyingly marked internal
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
					EnableVersionCheck(true);
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
						RaiseServicesError();
					}

					break;
			}
		}

		public static void ConnectLAN(string ip)
		{
			SetNetworkTransportType(Protocol.LAN);

			manager.NetworkConfig.UseCMBService = false;

			EnableVersionCheck(true);

			transport.SetConnectionData(ip, port);

			State = NetcodeState.Connecting;
			isAttemptingConnection = true;

			manager.StartClient();
		}

		/// <summary>
		/// Netcode's approval handshake carries the version between the two ends.
		/// The multiplayer service performs its own approval, so relay sessions
		/// leave it off and rely on the joiner reporting an unexplained failure.
		/// </summary>
		private static void EnableVersionCheck(bool enabled)
		{
			manager.NetworkConfig.ConnectionApproval = enabled;
			manager.NetworkConfig.ConnectionData =
				enabled ? Encoding.UTF8.GetBytes(GameVersion) : Array.Empty<byte>();
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
				RaiseServicesError();
			}
		}

		private static void RaiseServicesError()
		{
			// this error explains the failure, so don't also report it as a failed join
			isAttemptingConnection = false;

			UserErrors.Raise("Couldn't reach the relay service",
				"Hosting or joining over the internet needs a working internet connection. " +
				"Check this headset's Wi-Fi, or host over the local network instead.");
		}

		private static async Task ConnectUnityServices(string id, CancellationToken ct)
		{
			if (State != NetcodeState.Disconnected)
				return;

			SetNetworkTransportType(Protocol.UnityService);

			manager.NetworkConfig.UseCMBService = true;

			EnableVersionCheck(false);

			State = NetcodeState.Connecting;
			isAttemptingConnection = true;

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

			isAttemptingConnection = false;
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
