using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Anaglyph.Netcode
{
	public enum SessionDiscoveryActivity
	{
		Disabled,
		Listening,
		Advertising
	}

	/// <summary>Why a session heard nearby can't be joined from this device's current network.</summary>
	public enum UnjoinableSessionReason
	{
		None,
		WifiOff,
		NoLocalNetwork,
		NoInternet,
		DirectUnsupported
	}

	/// <summary>
	/// What this device is doing about nearby sessions: announcing its own, listening for others'
	/// and joining the first it hears, or neither. <see cref="MulticastSessionDiscovery"/> carries it out
	/// on the local network and <see cref="BluetoothSessionDiscovery"/> between nearby devices.
	/// </summary>
	public static class SessionDiscovery
	{
		internal const string LanPrefix = "IP:";
		internal const string RelayPrefix = "Relay:";
		internal const string DirectPrefix = "Direct:";
		private const float MinReconnectDelay = 2;
		private const float MaxReconnectDelay = 30;
		// a session just left is still announced for a moment, and would be rejoined at once
		private const float JoinDelayAfterListeningStartsSeconds = 3;
		// long enough to outlast gaps between a host's announcements
		private const float UnjoinableSessionForgetSeconds = 5;

		private static float reconnectDelay = MinReconnectDelay;
		private static float nextConnectAllowedTime;
		private static UnjoinableSessionReason lastUnjoinableReason;
		private static float lastUnjoinableHeardTime = float.NegativeInfinity;

		public static SessionDiscoveryActivity Activity { get; private set; }

		/// <summary>Why the nearby session heard most recently could not be joined, while it is still being heard.</summary>
		public static UnjoinableSessionReason UnjoinableSessionNearby =>
			IsListening && Time.unscaledTime - lastUnjoinableHeardTime < UnjoinableSessionForgetSeconds
				? lastUnjoinableReason
				: UnjoinableSessionReason.None;

		/// <summary>Whether announcements can be heard right now, not only asked for.</summary>
		public static bool IsListening => Activity == SessionDiscoveryActivity.Listening && receivers.Count > 0;

		private static readonly HashSet<object> receivers = new();

		/// <summary>Each way of hearing announcements reports whether it can hear them right now.</summary>
		internal static void SetReceiving(object receiver, bool receiving)
		{
			if (receiving) receivers.Add(receiver);
			else receivers.Remove(receiver);
		}

		public static void SetActivity(SessionDiscoveryActivity next)
		{
			if (next == Activity) return;

			if (next == SessionDiscoveryActivity.Advertising) reconnectDelay = MinReconnectDelay;
			if (next == SessionDiscoveryActivity.Listening)
				nextConnectAllowedTime = Mathf.Max(nextConnectAllowedTime, Time.time + JoinDelayAfterListeningStartsSeconds);
			Activity = next;
		}

		/// <summary>What a host announces: its LAN address, its relay session's name, or its direct session's token.</summary>
		public static string CurrentAdvertisement()
		{
			if (NetworkManager.Singleton == null) return "";
			if (NetcodeManagement.DirectSessionToken != "") return DirectPrefix + NetcodeManagement.DirectSessionToken;
			UnityTransport transport = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
			return transport.Protocol switch
			{
				UnityTransport.ProtocolType.UnityTransport => LanPrefix + transport.ConnectionData.Address,
				UnityTransport.ProtocolType.RelayUnityTransport => RelayPrefix + NetcodeManagement.CurrentSessionName,
				_ => ""
			};
		}

		/// <summary>
		/// An announcement heard while listening, from <paramref name="senderAddress"/>: joins it,
		/// backing off between attempts, unless this device's network can't reach it.
		/// A LAN host is joined where its announcement came from, which is the address that reached this device.
		/// </summary>
		public static void OnAdvertisementHeard(string message, string senderAddress)
		{
			if (Activity != SessionDiscoveryActivity.Listening) return;
			if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening) return;

			UnjoinableSessionReason unjoinableReason = WhyUnjoinable(message);
			if (unjoinableReason != UnjoinableSessionReason.None)
			{
				lastUnjoinableReason = unjoinableReason;
				lastUnjoinableHeardTime = Time.unscaledTime;
				return;
			}

			if (Time.time < nextConnectAllowedTime) return;

			Debug.Log($"[SessionDiscovery] Discovered {message} from {senderAddress}");
			nextConnectAllowedTime = Time.time + reconnectDelay;
			reconnectDelay = Mathf.Min(reconnectDelay * 2, MaxReconnectDelay);

			if (message.StartsWith(LanPrefix))
				NetcodeManagement.ConnectLAN(senderAddress);
			else if (message.StartsWith(RelayPrefix))
				NetcodeManagement.ConnectUnityServices(message.Remove(0, RelayPrefix.Length));
			else if (message.StartsWith(DirectPrefix))
				NetcodeManagement.ConnectDirect(message.Remove(0, DirectPrefix.Length));
		}

		private static UnjoinableSessionReason WhyUnjoinable(string message)
		{
			if (message.StartsWith(DirectPrefix))
			{
				if (!NetcodeManagement.DirectJoiningSupported) return UnjoinableSessionReason.DirectUnsupported;
				return NetworkConnectivityTest.IsWifiEnabled()
					? UnjoinableSessionReason.None
					: UnjoinableSessionReason.WifiOff;
			}

			NetworkState network = NetworkConnectivityTest.GetNetworkState();
			if (network == NetworkState.WifiOff) return UnjoinableSessionReason.WifiOff;
			if (message.StartsWith(LanPrefix) && network < NetworkState.LocalNetworkOnly)
				return UnjoinableSessionReason.NoLocalNetwork;
			if (message.StartsWith(RelayPrefix) && network != NetworkState.FullInternet)
				return UnjoinableSessionReason.NoInternet;
			return UnjoinableSessionReason.None;
		}
	}
}
