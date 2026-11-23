using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Anaglyph.Netcode
{
	/// <summary>
	/// Relays datagrams arriving at one local address to a target, and the target's replies back to whoever sent them.
	/// Each sender is relayed through a socket of its own, so the target tells senders apart by port.
	/// </summary>
	public sealed class UdpForwarder : IDisposable
	{
		private const int WaitForDatagramMicroseconds = 100_000;
		private const int LargestDatagramBytes = 65535;

		private readonly Socket listener;
		private readonly IPEndPoint target;
		private readonly Dictionary<EndPoint, Socket> relayBySender = new();
		private readonly Dictionary<Socket, EndPoint> senderByRelay = new();
		private readonly Thread relayThread;
		private volatile bool running = true;

		public int Port => ((IPEndPoint)listener.LocalEndPoint).Port;

		/// <summary>Listens on a free port of <paramref name="listenAddress"/>.</summary>
		public UdpForwarder(IPAddress listenAddress, IPEndPoint target)
		{
			this.target = target;
			listener = new Socket(listenAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
			listener.Bind(new IPEndPoint(listenAddress, 0));
			relayThread = new Thread(Relay) { IsBackground = true, Name = nameof(UdpForwarder) };
			relayThread.Start();
		}

		/// <summary>Sends a datagram of this forwarder's own, from the address and port it listens on.</summary>
		public void SendFromListenAddress(byte[] datagram, EndPoint receiver) => listener.SendTo(datagram, receiver);

		private void Relay()
		{
			byte[] buffer = new byte[LargestDatagramBytes];
			List<Socket> readable = new();

			while (running)
			{
				readable.Clear();
				readable.Add(listener);
				readable.AddRange(senderByRelay.Keys);

				try
				{
					Socket.Select(readable, null, null, WaitForDatagramMicroseconds);
					foreach (Socket socket in readable)
					{
						if (socket == listener)
						{
							EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
							int length = listener.ReceiveFrom(buffer, ref sender);
							RelayFor(sender).Send(buffer, length, SocketFlags.None);
						}
						else
						{
							int length = socket.Receive(buffer);
							listener.SendTo(buffer, length, SocketFlags.None, senderByRelay[socket]);
						}
					}
				}
				catch (SocketException)
				{
					// a datagram nobody was there to take; the next one is unaffected
				}
			}
		}

		private Socket RelayFor(EndPoint sender)
		{
			if (relayBySender.TryGetValue(sender, out Socket relay)) return relay;

			relay = new Socket(target.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
			relay.Connect(target);
			relayBySender[sender] = relay;
			senderByRelay[relay] = sender;
			return relay;
		}

		public void Dispose()
		{
			running = false;
			// joined before any socket closes: closing one a thread is waiting on stalls the closer on device
			relayThread.Join();
			listener.Close();
			foreach (Socket relay in senderByRelay.Keys) relay.Close();
		}
	}
}
