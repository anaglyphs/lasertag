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
using static Anaglyph.Netcode.NetcodeHelper;

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

		private static bool _isRunning;
		public static event Action<bool> IsRunningChange = delegate { };
		public static bool IsRunning
		{
			get => _isRunning;
			private set
			{
				bool changed = value != _isRunning;
				_isRunning = value;
				if (changed)
					IsRunningChange?.Invoke(_isRunning);
			}
		}

		private static Task currentTask;
		private static CancellationTokenSource CancelTokenSource = new();

		public enum Protocol
		{
			LAN,
			UnityService, 
		}

		private static void SetNetworkTransportType(string s)
		{
			NetworkTransport newTransport = manager.GetComponent(s) as NetworkTransport;
			if (newTransport == null)
				throw new NullReferenceException($"Could not find transport {s}!");
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
					StartHost();
					break;

				case Protocol.UnityService:

					await ConnectUnityServices(DateTime.Now.ToString("HHmmssffff"), ct);

					break;
			}
		}

		public static void ConnectLAN(string ip)
		{
			SetNetworkTransportType("UnityTransport");
			manager.NetworkConfig.UseCMBService = false;

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
		public static string CurrentSessionName { get; private set; } = "";

		public static void ConnectUnityServices(string id)
		{
			if (currentTask != null && !currentTask.IsCompleted)
				return;

			currentTask = ConnectUnityServices(id, CancelTokenSource.Token);
		}

		private static async Task ConnectUnityServices(string id, CancellationToken ct)
		{
			IsRunning = true;

			if (Time.time < cooldownDoneTime) {
				float waitTime = cooldownDoneTime - Time.time;
				await Awaitable.WaitForSecondsAsync(waitTime);
			}

			if (ct.IsCancellationRequested) return;

			SetNetworkTransportType("DistributedAuthorityTransport");
			manager.NetworkConfig.UseCMBService = true;

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

			IsRunning = false;

			manager.Shutdown();
		}

		private static void StartClient()
		{
			manager.Shutdown();
			manager.StartClient();
			IsRunning = true;
		}

		private static void StartHost()
		{
			manager.Shutdown();
			manager.StartHost();
			IsRunning = true;
		}
	}
}
