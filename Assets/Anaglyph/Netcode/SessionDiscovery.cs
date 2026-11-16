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

	/// <summary>
	/// What this device is doing about nearby sessions: announcing its own, listening for others'
	/// and joining the first it hears, or neither. <see cref="MulticastSessionDiscovery"/> carries it out.
	/// </summary>
	public static class SessionDiscovery
	{
		private const string LanPrefix = "IP:";
		private const string RelayPrefix = "Relay:";
		private const float MinReconnectDelay = 2;
		private const float MaxReconnectDelay = 30;

		private static float reconnectDelay = MinReconnectDelay;
		private static float nextConnectAllowedTime;

		public static SessionDiscoveryActivity Activity { get; private set; }

		/// <summary>Whether announcements can be heard right now, not only asked for.</summary>
		public static bool IsListening => Activity == SessionDiscoveryActivity.Listening && Receiving;

		internal static bool Receiving { get; set; }

		public static void SetActivity(SessionDiscoveryActivity next)
		{
			if (next == SessionDiscoveryActivity.Advertising && Activity != next) reconnectDelay = MinReconnectDelay;
			Activity = next;
		}

		/// <summary>What a host announces: its LAN address, or its relay session's name.</summary>
		public static string CurrentAdvertisement()
		{
			if (NetworkManager.Singleton == null) return "";
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
		/// backing off between attempts. A LAN host is joined where its announcement came from,
		/// which is the address that reached this device.
		/// </summary>
		public static void OnAdvertisementHeard(string message, string senderAddress)
		{
			if (Activity != SessionDiscoveryActivity.Listening) return;

			if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening || Time.time < nextConnectAllowedTime)
				return;

			Debug.Log($"[SessionDiscovery] Discovered {message} from {senderAddress}");
			nextConnectAllowedTime = Time.time + reconnectDelay;
			reconnectDelay = Mathf.Min(reconnectDelay * 2, MaxReconnectDelay);

			if (message.StartsWith(LanPrefix))
				NetcodeManagement.ConnectLAN(senderAddress);
			else if (message.StartsWith(RelayPrefix))
				NetcodeManagement.ConnectUnityServices(message.Remove(0, RelayPrefix.Length));
		}
	}
}
