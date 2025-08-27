using System;
using System.Net;
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

		public static float cooldownSeconds = 8;
		private static float lastAttemptTime = -1000;

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		public enum Protocol
		{
			LAN,
			UnityService, 
		}

		private static void SetNetworkTransportType(string s)
		{
			manager.NetworkConfig.UseCMBService = false;

			NetworkTransport newTransport = manager.GetComponent(s) as NetworkTransport;
			if (newTransport == null)
				throw new NullReferenceException($"Could not find transport {s}!");
			manager.NetworkConfig.NetworkTransport = newTransport;
		}

		private static bool Cooldown()
		{
			bool coolingDown = !CheckIsReady();

			if (!coolingDown)
				lastAttemptTime = Time.time;

			return coolingDown;
		}

		public static bool CheckIfSessionOwner() => manager.CurrentSessionOwner == manager.LocalClientId;

		public static bool CheckIsReady() => Time.time - lastAttemptTime > cooldownSeconds;

		public static async void Host(Protocol protocol)
		{
			if (Cooldown())
				return;

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

					SetNetworkTransportType("DistributedAuthorityTransport");

					await SetupServices();

					var options = new SessionOptions()
					{
						Name = Guid.NewGuid().ToString(),
						MaxPlayers = 20,
					}.WithDistributedAuthorityNetwork();

					CurrentSession = await MultiplayerService.Instance.CreateSessionAsync(options);

					break;
			}
		}

		public static void ConnectLAN(string ip)
		{
			if (Cooldown())
				return;

			SetNetworkTransportType("UnityTransport");

			transport.SetConnectionData(ip, port);

			StartClient();
		}

		private static async Task SetupServices()
		{
			if (UnityServices.State == ServicesInitializationState.Uninitialized)
				await UnityServices.InitializeAsync();

			if (!AuthenticationService.Instance.IsSignedIn)
				await AuthenticationService.Instance.SignInAnonymouslyAsync();
		}

		public static ISession CurrentSession { get; private set; }

		public static async void ConnectUnityServices(string id)
		{
			if (Cooldown())
				return;

			SetNetworkTransportType("DistributedAuthorityTransport");

			await SetupServices();

			CurrentSession = await MultiplayerService.Instance.JoinSessionByIdAsync(id);
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
	}
}
