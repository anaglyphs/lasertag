using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.WifiDirect;
using UnityEngine;

namespace Anaglyph.Netcode
{
	/// <summary>
	/// A headset whose Wi-Fi is joined to nothing scans for networks every few minutes, and each scan stalls the
	/// group it owns for seconds. In a direct session the joiners are joined to the host's group, but the host is
	/// joined to nothing. So the first joiner opens a group of its own on the same frequency and the host joins it:
	/// then every headset is joined to something and none scans.
	///
	/// Every group owner has the same address inside its own group, so those two cannot reach each other by it.
	/// The joiner reaches the host at the address the host got in the joiner's group, where the host relays to the game.
	///
	/// The joiner asks with "link [its token]". The host answers "direct" (join as usual) or "wait [frequency]",
	/// joins the joiner's group, and then sends "addr" from the relay the joiner should connect the game to.
	/// </summary>
	internal sealed class DirectTwoWayLinkHost : IDisposable
	{
		private enum Stage { Open, Joining, Linked }

		// long enough for this device's user to read and answer the system's question about joining
		private const float JoinTimeoutSeconds = 60;
		private const float AddressRepeatSeconds = 0.5f;
		private const int AddressRepeats = 10;

		// joiners whose group the user of this device would not join; asking again would only make them wait
		private static readonly HashSet<string> declinedTokens = new();

		private readonly Socket requests;
		private readonly WifiDirectGroup ownGroup;
		private readonly WifiDirectGroup joinerGroup = new();
		private readonly int gamePort;
		private readonly byte[] received = new byte[DirectTwoWayLink.LargestMessageBytes];

		private Stage stage;
		private EndPoint joiner;
		private string joinerToken;
		private UdpForwarder relay;
		private float joinDeadline;
		private float nextAddressTime;
		private int addressRepeatsLeft;

		public DirectTwoWayLinkHost(WifiDirectGroup ownGroup, int gamePort)
		{
			this.ownGroup = ownGroup;
			this.gamePort = gamePort;
			requests = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
			requests.Bind(new IPEndPoint(IPAddress.Any, DirectTwoWayLink.PortFor(gamePort)));
		}

		/// <summary>Once a frame while hosting.</summary>
		public void Update()
		{
			while (requests.Available > 0)
			{
				EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
				int length = requests.ReceiveFrom(received, ref sender);
				string message = Encoding.ASCII.GetString(received, 0, length);
				if (message.StartsWith(DirectTwoWayLink.LinkRequest))
					Answer(message.Substring(DirectTwoWayLink.LinkRequest.Length), sender);
			}

			float now = Time.realtimeSinceStartup;
			switch (stage)
			{
				case Stage.Joining when joinerGroup.State == WifiDirectGroupState.Ready && joinerGroup.LocalAddress != null:
					relay = new UdpForwarder(IPAddress.Parse(joinerGroup.LocalAddress), new IPEndPoint(IPAddress.Loopback, gamePort));
					stage = Stage.Linked;
					addressRepeatsLeft = AddressRepeats;
					nextAddressTime = now;
					Debug.Log($"[DirectTwoWayLink] joined the joiner's group as {joinerGroup.LocalAddress}, relaying on port {relay.Port}");
					break;

				case Stage.Joining when joinerGroup.State == WifiDirectGroupState.Failed || now > joinDeadline:
					Debug.Log("[DirectTwoWayLink] could not join the joiner's group");
					declinedTokens.Add(joinerToken);
					Unlink();
					break;

				case Stage.Linked when joinerGroup.State != WifiDirectGroupState.Ready:
					Debug.Log("[DirectTwoWayLink] the joiner's group is gone");
					Unlink();
					break;

				case Stage.Linked when addressRepeatsLeft > 0 && now >= nextAddressTime:
					// sent from the relay, since a datagram from this device's group address would look to the joiner like its own
					relay.SendFromListenAddress(Encoding.ASCII.GetBytes(DirectTwoWayLink.RelayAddress), joiner);
					addressRepeatsLeft--;
					nextAddressTime = now + AddressRepeatSeconds;
					break;
			}
		}

		private void Answer(string token, EndPoint sender)
		{
			bool sameJoinerAsking = stage != Stage.Open && sender.Equals(joiner);
			bool canLink = stage == Stage.Open && !declinedTokens.Contains(token) &&
			               DirectTwoWayLink.TryNetworkOf(token, out _, out _) && ownGroup.FrequencyMHz != 0;

			if (canLink)
			{
				DirectTwoWayLink.TryNetworkOf(token, out string networkName, out string passphrase);
				joinerGroup.Join(networkName, passphrase, bindAppToGroup: false);
				stage = Stage.Joining;
				joiner = sender;
				joinerToken = token;
				joinDeadline = Time.realtimeSinceStartup + JoinTimeoutSeconds;
				Debug.Log($"[DirectTwoWayLink] joining {networkName} for {sender}");
			}

			string answer = canLink || sameJoinerAsking
				? DirectTwoWayLink.WaitAnswer + ownGroup.FrequencyMHz
				: DirectTwoWayLink.DirectAnswer;
			requests.SendTo(Encoding.ASCII.GetBytes(answer), sender);
		}

		private void Unlink()
		{
			relay?.Dispose();
			relay = null;
			joinerGroup.Leave();
			stage = Stage.Open;
		}

		public void Dispose()
		{
			Unlink();
			joinerGroup.Dispose();
			requests.Close();
		}
	}

	internal static class DirectTwoWayLink
	{
		internal const string LinkRequest = "link ";
		internal const string WaitAnswer = "wait ";
		internal const string DirectAnswer = "direct";
		internal const string RelayAddress = "addr";
		internal const int LargestMessageBytes = 64;

		private const float AnswerTimeoutSeconds = 3;
		private const float RequestRepeatSeconds = 0.5f;
		// a little longer than the host waits for its own user
		private const float RelayTimeoutSeconds = 65;

		internal static int PortFor(int gamePort) => gamePort + 1;

		/// <summary>The group a token names. Hex digits: 3 bytes of network name, then 8 of passphrase.</summary>
		internal static bool TryNetworkOf(string token, out string networkName, out string passphrase)
		{
			networkName = passphrase = null;
			if (token.Length != NetcodeManagement.DirectSessionTokenByteCount * 2) return false;
			foreach (char digit in token)
				if (!Uri.IsHexDigit(digit)) return false;

			int hexDigitsInName = NetcodeManagement.DirectNetworkNameByteCount * 2;
			networkName = NetcodeManagement.DirectNetworkNamePrefix + token.Substring(0, hexDigitsInName);
			passphrase = token.Substring(hexDigitsInName);
			return true;
		}

		/// <summary>
		/// For a joiner already joined to the host's group: asks the host to join <paramref name="ownGroup"/>,
		/// which this opens as the group <paramref name="ownToken"/> names. Returns where to connect the game,
		/// or null when the host stays unlinked and the game connects to the host's group address as usual.
		/// On null <paramref name="ownGroup"/> may be left open and is the caller's to close.
		/// </summary>
		internal static async Task<IPEndPoint> LinkAsJoiner(string hostAddress, int gamePort, string ownToken,
			WifiDirectGroup ownGroup, CancellationToken ct)
		{
			if (!TryNetworkOf(ownToken, out string networkName, out string passphrase)) return null;

			using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
			socket.Bind(new IPEndPoint(IPAddress.Any, 0));
			IPEndPoint host = new(IPAddress.Parse(hostAddress), PortFor(gamePort));
			byte[] request = Encoding.ASCII.GetBytes(LinkRequest + ownToken);
			byte[] received = new byte[LargestMessageBytes];

			string answer = null;
			float nextRequestTime = 0;
			for (float deadline = Time.realtimeSinceStartup + AnswerTimeoutSeconds; answer == null && Time.realtimeSinceStartup < deadline;)
			{
				if (Time.realtimeSinceStartup >= nextRequestTime)
				{
					socket.SendTo(request, host);
					nextRequestTime = Time.realtimeSinceStartup + RequestRepeatSeconds;
				}
				await Awaitable.NextFrameAsync(ct);
				answer = TakeMessage(socket, received, out _);
			}

			if (answer == null || !answer.StartsWith(WaitAnswer) ||
			    !int.TryParse(answer.Substring(WaitAnswer.Length), out int hostFrequencyMHz))
				return null;

			// on the host's frequency, or the radio would hop between the two groups
			ownGroup.Create(networkName, passphrase, hostFrequencyMHz);
			Debug.Log($"[DirectTwoWayLink] opened {networkName} at {hostFrequencyMHz} MHz for the host to join");

			for (float deadline = Time.realtimeSinceStartup + RelayTimeoutSeconds; Time.realtimeSinceStartup < deadline;)
			{
				await Awaitable.NextFrameAsync(ct);
				if (ownGroup.State == WifiDirectGroupState.Failed) return null;
				if (TakeMessage(socket, received, out EndPoint sender) == RelayAddress) return (IPEndPoint)sender;
			}
			return null;
		}

		private static string TakeMessage(Socket socket, byte[] buffer, out EndPoint sender)
		{
			sender = new IPEndPoint(IPAddress.Any, 0);
			if (socket.Available == 0) return null;
			int length = socket.ReceiveFrom(buffer, ref sender);
			return Encoding.ASCII.GetString(buffer, 0, length);
		}
	}
}
