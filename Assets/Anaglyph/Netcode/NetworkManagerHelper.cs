using StrikerLink.ThirdParty.WebSocketSharp;
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
	public static class NetworkHelper
	{
		public static ushort port = 7777;
		public static string contyp = "dtls";

		public const float cooldownSeconds = 3;
		private static float lastAttemptTime = -1000;

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		private static float lastConnectAttemptTime = -100;

		public enum Protocol
		{
			LAN,
			UnityService, 
		}

		public static void SetNetworkTransportType(string s)
		{
			manager.NetworkConfig.UseCMBService = false;

			NetworkTransport newTransport = manager.GetComponent(s) as NetworkTransport;
			if (newTransport == null)
				throw new NullReferenceException($"Could not find transport {s}!");
			manager.NetworkConfig.NetworkTransport = newTransport;
		}

		public static bool CheckIfSessionOwner() => manager.CurrentSessionOwner == manager.LocalClientId;

		public static bool CheckIsReady() => Time.time - lastAttemptTime > cooldownSeconds;

		public static async void Host(Protocol protocol)
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
					StartHost();
					break;

				case Protocol.UnityService:

					await HostUnityServices();

					break;
			}
		}

		private static bool CoolingDown()
		{
			bool coolingDown = Time.time - lastConnectAttemptTime < cooldownSeconds;
			
			if(!coolingDown)
				lastConnectAttemptTime = Time.time;

			return coolingDown;
		}

		public static void HostLAN()
		{
			SetNetworkTransportType("UnityTransport");

			string localAddress = "127.0.0.1";
			var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
			foreach (var address in addresses)
			{
				if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
				{
					localAddress = address.ToString();
					// break;
				}
			}

			transport.SetConnectionData(localAddress, port);
			StartHost();
		}

		public static async Task<ISession> HostUnityServices(string roomName = "")
		{
			if (CoolingDown())
				return null;

			SetNetworkTransportType("DistributedAuthorityTransport");

			await SetupServices();

			var options = new SessionOptions()
			{
				Name =  roomName.IsNullOrEmpty() ? Guid.NewGuid().ToString() : roomName,
				MaxPlayers = 20,
			}.WithDistributedAuthorityNetwork();

			CurrentSession = await MultiplayerService.Instance.CreateSessionAsync(options);

			return CurrentSession;
		}

		public static void ConnectLAN(string ip)
		{
			SetNetworkTransportType("UnityTransport");

			transport.SetConnectionData(ip, port);

			StartClient();
		}

		public static async Task SetupServices()
		{
			if (UnityServices.State == ServicesInitializationState.Uninitialized)
				await UnityServices.InitializeAsync();

			if (!AuthenticationService.Instance.IsSignedIn)
				await AuthenticationService.Instance.SignInAnonymouslyAsync();
		}

		public static ISession CurrentSession { get; private set; }

		public static async Task<ISession> ConnectUnityServices(string id)
		{
			if (CoolingDown())
				return null;

			SetNetworkTransportType("DistributedAuthorityTransport");

			await SetupServices();

			CurrentSession = await MultiplayerService.Instance.JoinSessionByIdAsync(id);

			return CurrentSession;
		}

		public static async void StartOrJoinByNameAsync(
		string sessionName,
		int maxPlayers = 20,
		string region = null,
		CancellationToken ct = default)
		{
			SetNetworkTransportType("DistributedAuthorityTransport");

			await SetupServices();

			// 1) Try to find an existing public, non-full session with this name.
			var query = new QuerySessionsOptions
			{
				Count = 10,
				// Name supports EQ and CONTAINS. We use EQ for exact arena binding.
				FilterOptions =
				{
					new FilterOption(FilterField.Name, sessionName, FilterOperation.Equal),
					new FilterOption(FilterField.AvailableSlots, "0", FilterOperation.Greater)
				},
				// Optional: newest first
				SortOptions =
				{
					new SortOption(SortOrder.Descending, SortField.CreationTime)
				}
			};

			var results = await MultiplayerService.Instance.QuerySessionsAsync(query);
			if (results.Sessions.Count > 0)
			{
				await ConnectUnityServices(results.Sessions[0].Id);
				return;
			}

			// 2) Nothing found. Attempt to create. If a race occurs, requery and join.
			try
			{
				await HostUnityServices(sessionName);
				return;
			}
			catch (SessionException)
			{
				// Likely a race where another device created it. Requery and join.
				results = await MultiplayerService.Instance.QuerySessionsAsync(query);
				if (results.Sessions.Count > 0)
				{
					await ConnectUnityServices(results.Sessions[0].Id);
					return;
				}
			}
		}

		public static async void Disconnect()
		{
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

		private static void StartClient()
		{
			manager.Shutdown();
			manager.StartClient();
		}

		private static void StartHost()
		{
			manager.Shutdown();
			manager.StartHost();
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

	public static class WritePerm
	{
		public static NetworkVariableWritePermission Owner => NetworkVariableWritePermission.Owner;
	}

	public static class ReadPerm
	{
		public static NetworkVariableReadPermission Everyone => NetworkVariableReadPermission.Everyone;
	}
}