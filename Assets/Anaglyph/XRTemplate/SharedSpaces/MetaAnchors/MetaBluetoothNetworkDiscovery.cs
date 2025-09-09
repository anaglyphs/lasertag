using Anaglyph.Netcode;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaBluetoothNetworkDiscovery : MonoBehaviour
	{
		public static MetaBluetoothNetworkDiscovery Instance { get; private set; }

		private static NetworkManager manager => NetworkManager.Singleton;
		private static UnityTransport transport => (UnityTransport) NetworkManager.Singleton.NetworkConfig.NetworkTransport;

		private static string LogHeader = "[AutoJoiner] ";
		private static void Log(string str) => Debug.Log(LogHeader + str);
		private static void LogWarning(string str) => Debug.LogWarning(LogHeader + str);

		private static string LanPrefix = "IP:";
		private static string RelayPrefix = "Relay:";

		private Task currentTask = Task.CompletedTask;

		public Task QueueTask(Func<Task> taskFactory)
		{
			var name = taskFactory.Method.Name;
			currentTask = currentTask
				.ContinueWith(_ => taskFactory(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Current)
				.Unwrap();

			currentTask.ContinueWith(t => Debug.LogException(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
			return currentTask;
		}

		private void Awake()
		{
			Instance = this;
			NetcodeManagement.StateChange += OnNetworkStateChange;
			OVRColocationSession.ColocationSessionDiscovered += HandleColocationSessionDiscovered;
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChange -= OnNetworkStateChange;
			OVRColocationSession.ColocationSessionDiscovered -= HandleColocationSessionDiscovered;
		}

		private void OnNetworkStateChange(NetcodeManagement.NetworkState state) => HandleChange();

		private void OnEnable() => HandleChange();
		private void OnDisable() => HandleChange();

#if !UNITY_EDITOR
		private void OnApplicationFocus(bool focus) => HandleChange();
		private void OnApplicationPause(bool pause) => HandleChange();
#endif

		private void HandleChange()
		{
#if UNITY_EDITOR
			if(enabled)
#else
			if (enabled && Application.isFocused)
#endif
			{
				if (manager == null)
					return;

				switch (NetcodeManagement.State)
				{
					case NetcodeManagement.NetworkState.Disconnected:
						QueueTask(StopAdvertisement);
						QueueTask(() => Task.Delay(3000));
						QueueTask(StartDiscovery);
						break;

					case NetcodeManagement.NetworkState.Connecting:
						QueueTask(StopDiscovery);
						break;

					case NetcodeManagement.NetworkState.Connected:
						QueueTask(StartAdvertisement);
						break;
				}
			}
			else
			{
				QueueTask(StopAdvertisement);
				QueueTask(StopDiscovery);
			}
		}


		private static async Task StartDiscovery()
		{
			Log("Starting discovery");
			var result = await OVRColocationSession.StartDiscoveryAsync();
			if (result.Success)
				Log("Discovery started");
			else
				LogWarning("Couldn't start discovery");
		}

		private static async Task StopDiscovery()
		{
			Log("Stopping discovery");
			var result = await OVRColocationSession.StopDiscoveryAsync();
			if (result.Success)
				Log("Discovery stopped");
			else
				LogWarning("Couldn't stop discovery");
		}

		private static async Task StopAdvertisement()
		{
			Log("Stopping advertisement");
			var result = await OVRColocationSession.StopAdvertisementAsync();
			if (result.Success)
				Log("Advertisement stopped");
			else
				LogWarning("Couldn't stop advertisement");
		}

		private async Task StartAdvertisement()
		{
			string message = "";

			switch (transport.Protocol)
			{
				case UnityTransport.ProtocolType.UnityTransport:
					string address = transport.ConnectionData.Address;
					message = LanPrefix + address;
					break;

				case UnityTransport.ProtocolType.RelayUnityTransport:
					message = RelayPrefix + NetcodeManagement.CurrentSessionName;
					break;
			}

			Log($"Starting advertisement {message}");

			var result = await OVRColocationSession.StartAdvertisementAsync(Encoding.ASCII.GetBytes(message));
			if (result.Success)
				Log("Advertisement started");
			else
				LogWarning("Couldn't start advertisement");
		}

		private void HandleColocationSessionDiscovered(OVRColocationSession.Data data)
		{
			string message = Encoding.ASCII.GetString(data.Metadata);
			Log($"Discovered {message}");

			if (NetworkManager.Singleton.IsListening)
				return;

			if(message.StartsWith(LanPrefix))
			{
				NetcodeManagement.ConnectLAN(message.Remove(0, LanPrefix.Length));
			} else if(message.StartsWith(RelayPrefix))
			{
				NetcodeManagement.ConnectUnityServices(message.Remove(0, RelayPrefix.Length));
			}
		}
	}
}