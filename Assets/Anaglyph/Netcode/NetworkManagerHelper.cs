using System;
using System.Net;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Multiplayer;
using Unity.Services.Relay;
using UnityEngine;

namespace Anaglyph.Netcode
{
	public static class NetworkHelper
	{
		public static ushort port = 7777;
		public static string contyp = "dtls";

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		public enum Protocol
		{
			LAN,
			UnityService, 
		}

		public static async void Host(Protocol protocol)
		{
			switch(protocol)
			{
				case Protocol.LAN:

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

		private static float timeSinceAttempt = 0;
		public static ISession CurrentSession { get; private set; }

		public static async void ConnectUnityServices(string id)
		{
			if (Time.time - timeSinceAttempt < 10)
				return;

			timeSinceAttempt = Time.time;

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
