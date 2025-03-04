using System;
using System.Net;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

namespace Anaglyph.Netcode
{
	public static class NetworkHelper
	{
		public static ushort port = 7777;
		public static string contyp = "dtls";

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport;

		public static Guid allocationId { get; private set; }

		public static async void StartHost(bool useRelay)
		{
			AssignTransport();

			if (useRelay)
			{
				await SetupServices();

				Allocation allocation = await RelayService.Instance.CreateAllocationAsync(20);
				allocationId = allocation.AllocationId;

				transport.SetRelayServerData(allocation.ToRelayServerData(contyp));
			}
			else
			{
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
			}

			StartHost();
		}

		public static void StartClientWithIP(string ip)
		{
			AssignTransport();
			
			transport.SetConnectionData(ip, port);

			StartClient();
		}

		public static async void StartClientWithRelayCode(string code)
		{
			AssignTransport();

			await SetupServices();

			var joinAllocation = await RelayService.Instance.JoinAllocationAsync(code);

			transport.SetRelayServerData(joinAllocation.ToRelayServerData(contyp));

			StartClient();
		}

		private static void AssignTransport()
		{
			if (transport == null)
				manager.TryGetComponent(out transport);
		}

		private static async Task SetupServices()
		{
			if (UnityServices.State == ServicesInitializationState.Uninitialized)
				await UnityServices.InitializeAsync();

			if (!AuthenticationService.Instance.IsSignedIn)
				await AuthenticationService.Instance.SignInAnonymouslyAsync();
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
