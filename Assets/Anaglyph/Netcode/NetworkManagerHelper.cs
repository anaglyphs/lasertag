using System;
using System.Net;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

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


					//Allocation allocation = await RelayService.Instance.CreateAllocationAsync(20);
					//allocationId = allocation.AllocationId;

					//transport.SetRelayServerData(allocation.ToRelayServerData(contyp));
					//StartHost();

					var options = new SessionOptions()
					{
						Name = Guid.NewGuid().ToString(),
						MaxPlayers = 20,
					}.WithDistributedAuthorityNetwork();

					await MultiplayerService.Instance.CreateSessionAsync(options);

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

		//public static async void ConnectRelay(string code)
		//{
		//	await SetupServices();

		//	var joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);

		//	transport.SetRelayServerData(joinAllocation.ToRelayServerData(contyp));

		//	StartClient();
		//}

		public static async void ConnectDistAuth(string id)
		{
			await SetupServices();

			await MultiplayerService.Instance.JoinSessionByIdAsync(id);
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
