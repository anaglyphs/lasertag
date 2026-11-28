using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.VariableObjects;
using Anaglyph.WifiDirect;
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
		public enum ErrorKind { JoinFailed, VersionMismatch, ServicesUnavailable, DirectNeedsWifi }

		public readonly struct Error
		{
			public readonly ErrorKind kind;
			public readonly string hostVersion;
			public readonly string clientVersion;

			public Error(ErrorKind kind, string hostVersion = null, string clientVersion = null)
			{
				this.kind = kind;
				this.hostVersion = hostVersion;
				this.clientVersion = clientVersion;
			}
		}

		public static event Action<Error> ErrorRaised = delegate { };

		public enum Protocol
		{
			LAN,
			UnityService,
			/// <summary>LAN play over a Wi-Fi Direct group the host creates, for places with no router.</summary>
			Direct
		}

		public const float cooldownSeconds = 8;

		// Older hosts send this reason when enforcing an exact build match.
		private const string legacyVersionMismatchPrefix = "VERSION_MISMATCH:";

		private static NetworkManager manager => NetworkManager.Singleton;

		private static UnityTransport transport =>
			(UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		/// <summary>The LAN port. In the editor the ANAGLYPH_PORT environment variable replaces it, so two projects' play-mode players can run side by side.</summary>
		public static readonly ushort port = EditorPortOverride() ?? 7777;

		private static ushort? EditorPortOverride()
		{
#if UNITY_EDITOR
			if (ushort.TryParse(System.Environment.GetEnvironmentVariable("ANAGLYPH_PORT"), out ushort overridden)) return overridden;
#endif
			return null;
		}

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
				if (value == NetcodeState.Disconnected) IsHosting = false;
				if (changed)
					StateChanged.Invoke(_state);
			}
		}

		public static ISession CurrentSession { get; private set; }
		public static string CurrentSessionName { get; private set; } = "";

		public static bool DirectHostingSupported => WifiDirectGroup.CanCreate;
		public static bool DirectJoiningSupported => WifiDirectGroup.CanJoin;

		/// <summary>
		/// Names the Wi-Fi Direct group of the current direct session, and is all a nearby device needs to join it.
		/// Empty outside a direct session. Hex digits: 3 bytes of network name, then 8 of passphrase.
		/// </summary>
		public static string DirectSessionToken { get; private set; } = "";

		public const int DirectSessionTokenByteCount = DirectNetworkNameByteCount + DirectPassphraseByteCount;
		internal const int DirectNetworkNameByteCount = 3;
		private const int DirectPassphraseByteCount = 8;
		internal const string DirectNetworkNamePrefix = "DIRECT-Lasertag-";
		private const float DirectGroupCreateTimeoutSeconds = 15;
		private const float DirectGroupJoinTimeoutSeconds = 90;
		private const float DirectSuggestedJoinTimeoutSeconds = 10;
		private const float MaxCountedFrameSeconds = 0.1f;

		/// <summary>
		/// Joining through the system's prompt lets host and joiner stop scanning for networks, which stalls a
		/// direct session every few minutes (<see cref="DirectTwoWayLink"/>). The message the system shows as that
		/// prompt closes exhausts a Quest's memory on Horizon OS 210, so joiners join as a suggested network instead.
		/// </summary>
		private static readonly bool directJoinsThroughSystemPrompt = false;

		private static WifiDirectGroup directGroup;
		// the group a joiner opens for the host to join, and the host's side of that link
		private static WifiDirectGroup directGroupForHost;
		private static DirectTwoWayLinkHost directTwoWayLinkHost;
		// a direct session whose join the user refused, held off until the retry time; a host's token never changes
		private static string directSessionTokenNotToJoin = "";
		private static float refusedDirectJoinRetryTime;
		private const float RefusedDirectJoinRetrySeconds = 60;
		private const string OwnDirectSessionTokenKey = "Anaglyph.Netcode.OwnDirectSessionToken";

		// Written into the asset at build time, so every build of the game reports
		// the same version no matter which rig or menu is running.
		private const string buildNumberResourceName = "BuildNumber";
		private static StringObject buildNumber;

		/// <summary>
		/// Identifies this build for display and diagnostics. Multiplayer compatibility
		/// is determined by NetworkConfig.ProtocolVersion and NGO's configuration checks.
		/// </summary>
		public static string GameVersion => string.IsNullOrEmpty(BuildNumber)
			? Application.version
			: $"{Application.version} ({BuildNumber})";

		public static string BuildNumber
		{
			get
			{
				if (buildNumber == null)
					buildNumber = Resources.Load<StringObject>(buildNumberResourceName);

				return buildNumber != null ? buildNumber.Value : null;
			}
		}

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
			ErrorRaised = delegate { };

			CurrentSession = null;
			CurrentSessionName = "";
			isAttemptingConnection = false;

			directTwoWayLinkHost?.Dispose();
			directTwoWayLinkHost = null;
			directGroupForHost?.Dispose();
			directGroupForHost = null;
			directGroup?.Dispose();
			directGroup = null;
			DirectSessionToken = "";
			directSessionTokenNotToJoin = "";
			Application.quitting -= LeaveDirectGroup;
			Application.quitting += LeaveDirectGroup;
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

			if (DirectJoiningSupported) directGroup = new WifiDirectGroup();
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

			LeaveDirectGroup();
			State = NetcodeState.Disconnected;

			if (failedToJoin)
				RaiseJoinFailureError();
		}

		/// <summary>
		/// NGO has already checked protocol and configuration compatibility before
		/// this LAN approval callback. Build numbers do not need to match.
		/// </summary>
		private static void ApproveJoiningClient(NetworkManager.ConnectionApprovalRequest request,
			NetworkManager.ConnectionApprovalResponse response)
		{
			response.Approved = true;
			response.CreatePlayerObject = manager.NetworkConfig.PlayerPrefab != null;
		}

		private static void RaiseJoinFailureError()
		{
			string reason = manager == null ? "" : manager.DisconnectReason;
			int prefixIndex = reason == null ? -1 : reason.IndexOf(legacyVersionMismatchPrefix, StringComparison.Ordinal);

			if (prefixIndex >= 0)
			{
				string hostVersion = reason.Substring(prefixIndex + legacyVersionMismatchPrefix.Length).Trim();

				ErrorRaised.Invoke(new Error(ErrorKind.VersionMismatch, hostVersion, GameVersion));

				return;
			}

			// NGO can reject a protocol/configuration mismatch before approval without
			// sending a reason or the host's protocol version. Do not invent either.
			ErrorRaised.Invoke(new Error(ErrorKind.JoinFailed));
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

		/// <summary>True from <see cref="Host"/> until the session ends.</summary>
		public static bool IsHosting { get; private set; }

		public static async void Host(Protocol protocol)
		{
			if (State == NetcodeState.Disconnected) IsHosting = true;
			switch (protocol)
			{
				case Protocol.LAN:
					UseLanTransport();
					transport.SetConnectionData(GetLocalIPv4(), port, DefaultIP);
					manager.StartHost();
					break;

				case Protocol.UnityService:
					ConnectUnityServices(DateTime.Now.ToString("HHmmssffff"));
					break;

				case Protocol.Direct:
					await StartDirectSession(OwnDirectSessionToken(), true);
					break;
			}
		}

		/// <summary>
		/// The same token every time this device hosts. A joiner's system asks before joining a Wi-Fi network
		/// it has not joined before, so a host that kept changing its network would be asked about every session.
		/// </summary>
		private static string OwnDirectSessionToken()
		{
			string token = PlayerPrefs.GetString(OwnDirectSessionTokenKey, "");
			if (token.Length == DirectSessionTokenByteCount * 2) return token;

			byte[] bytes = new byte[DirectSessionTokenByteCount];
			System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
			token = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
			PlayerPrefs.SetString(OwnDirectSessionTokenKey, token);
			PlayerPrefs.Save();
			return token;
		}

		/// <summary>Joins the direct session a nearby host announced as <paramref name="sessionToken"/>.</summary>
		public static async void ConnectDirect(string sessionToken) => await StartDirectSession(sessionToken, false);

		private static async Task StartDirectSession(string sessionToken, bool asHost)
		{
			bool refusedRecently = sessionToken == directSessionTokenNotToJoin &&
			                       Time.realtimeSinceStartup < refusedDirectJoinRetryTime;
			if (State != NetcodeState.Disconnected || refusedRecently)
				return;

			if (!(asHost ? DirectHostingSupported : DirectJoiningSupported))
				return;

			if (!NetworkConnectivityTest.IsWifiEnabled())
			{
				ErrorRaised.Invoke(new Error(ErrorKind.DirectNeedsWifi));
				return;
			}

			if (!DirectTwoWayLink.TryNetworkOf(sessionToken, out string networkName, out string passphrase))
			{
				Log($"Malformed direct session token {sessionToken}", LogType.Warning);
				return;
			}

			CancellationToken ct = PrepareNextTask();
			UseLanTransport();
			DirectSessionToken = sessionToken;
			State = NetcodeState.Connecting;
			isAttemptingConnection = true;

			try
			{
				directGroup ??= new WifiDirectGroup();
				if (asHost) directGroup.Create(networkName, passphrase);
				else if (directJoinsThroughSystemPrompt) directGroup.Join(networkName, passphrase);
				else
				{
					// as a suggested network the host's network scans stall the session for less; a device that
					// can't join that way (not allowed by its user, or on a Wi-Fi network) joins as a Wi-Fi Direct client
					if (WifiDirectGroup.CanJoinAsSuggestedNetwork)
					{
						directGroup.JoinAsSuggestedNetwork(networkName, passphrase);
						await WaitWhileDirectGroupForms(DirectSuggestedJoinTimeoutSeconds, ct);
					}
					if (directGroup.State != WifiDirectGroupState.Ready)
						directGroup.JoinWithoutSystemPrompt(networkName, passphrase);
				}

				await WaitWhileDirectGroupForms(asHost ? DirectGroupCreateTimeoutSeconds : DirectGroupJoinTimeoutSeconds, ct);

				if (directGroup.State != WifiDirectGroupState.Ready)
					throw new Exception($"Wi-Fi Direct group {networkName} ended {directGroup.State}");

				if (asHost)
				{
					transport.SetConnectionData(directGroup.OwnerAddress, port, DefaultIP);
					manager.StartHost();
					directTwoWayLinkHost = new DirectTwoWayLinkHost(directGroup, port);
					UpdateDirectTwoWayLinkHost(directTwoWayLinkHost);
				}
				else
				{
					IPEndPoint hostRelay = null;
					if (directJoinsThroughSystemPrompt && WifiDirectGroup.CanCreate)
					{
						directGroupForHost ??= new WifiDirectGroup();
						hostRelay = await DirectTwoWayLink.LinkAsJoiner(directGroup.OwnerAddress, port,
							OwnDirectSessionToken(), directGroupForHost, ct);
						// while this device owns a group, the host's group address is this device's own
						if (hostRelay == null) directGroupForHost.Leave();
					}

					if (hostRelay != null) transport.SetConnectionData(hostRelay.Address.ToString(), (ushort)hostRelay.Port);
					else transport.SetConnectionData(directGroup.OwnerAddress, port);
					manager.StartClient();
				}
			}
			catch (OperationCanceledException)
			{
				// whoever cancelled (Disconnect, a new attempt, app exit) owns the state now
				Log($"Direct session {networkName} cancelled before it started");
				LeaveDirectGroup();
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				if (directGroup != null && directGroup.UserRefused)
				{
					directSessionTokenNotToJoin = sessionToken;
					refusedDirectJoinRetryTime = Time.realtimeSinceStartup + RefusedDirectJoinRetrySeconds;
				}
				bool wifiSwitchedOff = !NetworkConnectivityTest.IsWifiEnabled();
				// reported as Wi-Fi off instead of the generic join failure
				if (wifiSwitchedOff) isAttemptingConnection = false;
				EndAttempt();
				if (wifiSwitchedOff) ErrorRaised.Invoke(new Error(ErrorKind.DirectNeedsWifi));
			}
		}

		/// <summary>Counted in frames shown, so time the app spends behind a system prompt doesn't use it up.</summary>
		private static async Task WaitWhileDirectGroupForms(float seconds, CancellationToken ct)
		{
			while (directGroup.State == WifiDirectGroupState.Forming && seconds > 0)
			{
				await Awaitable.NextFrameAsync(ct);
				seconds -= Mathf.Min(Time.unscaledDeltaTime, MaxCountedFrameSeconds);
			}
		}

		private static async void UpdateDirectTwoWayLinkHost(DirectTwoWayLinkHost linkHost)
		{
			while (directTwoWayLinkHost == linkHost)
			{
				linkHost.Update();
				await Awaitable.NextFrameAsync();
			}
		}

		private static void LeaveDirectGroup()
		{
			DirectSessionToken = "";
			directTwoWayLinkHost?.Dispose();
			directTwoWayLinkHost = null;
			directGroupForHost?.Leave();
			directGroup?.Leave();
		}

		private static void UseLanTransport()
		{
			SetNetworkTransportType(Protocol.LAN);
			manager.NetworkConfig.UseCMBService = false;
			ConfigureConnectionApproval(true);
		}

		public static void ConnectLAN(string ip)
		{
			UseLanTransport();

			transport.SetConnectionData(ip, port);

			State = NetcodeState.Connecting;
			isAttemptingConnection = true;

			manager.StartClient();
		}

		/// <summary>
		/// Keep LAN approval and the build payload for interoperability with older
		/// builds. New hosts ignore the payload; older hosts may still require it.
		/// The multiplayer service performs its own approval for relay sessions.
		/// </summary>
		private static void ConfigureConnectionApproval(bool enabled)
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
			// checked before PrepareNextTask so a repeat call can't cancel the attempt in flight
			if (State != NetcodeState.Disconnected)
				return;

			try
			{
				await ConnectUnityServices(id, PrepareNextTask());
			}
			catch (OperationCanceledException)
			{
				// whoever cancelled (Disconnect, a new attempt, app exit) owns the state now
				Log("Cancelled connecting to Unity services");
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

			ErrorRaised.Invoke(new Error(ErrorKind.ServicesUnavailable));
		}

		private static async Task ConnectUnityServices(string id, CancellationToken ct)
		{
			SetNetworkTransportType(Protocol.UnityService);

			manager.NetworkConfig.UseCMBService = true;

			ConfigureConnectionApproval(false);

			CurrentSessionName = id;
			State = NetcodeState.Connecting;
			isAttemptingConnection = true;

			if (Time.time < cooldownDoneTime)
			{
				float waitTime = cooldownDoneTime - Time.time;
				await Awaitable.WaitForSecondsAsync(waitTime, ct);
			}

			ct.ThrowIfCancellationRequested();

			cooldownDoneTime = Time.time + cooldownSeconds;

			// services init and sign-in take no token
			await SetupServices();

			ct.ThrowIfCancellationRequested();

			SessionOptions options = new SessionOptions
			{
				Name = id,
				MaxPlayers = 20
			}.WithDistributedAuthorityNetwork();

			// cancelling also stops the network start the join triggers
			ISession session = await MultiplayerService.Instance.CreateOrJoinSessionAsync(id, options, ct);

			// cancelled after the join completed but before this continuation ran.
			// leaving also stops the session's network
			if (ct.IsCancellationRequested)
			{
				await session.LeaveAsync();
				ct.ThrowIfCancellationRequested();
			}

			CurrentSession = session;
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
			LeaveDirectGroup();
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
