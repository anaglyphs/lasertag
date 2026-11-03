using System;

namespace Anaglyph.Netcode.SyncVariables
{
	// How an event travels; pick per event based on the ordering you need.
	public enum EventRoute : byte
	{
		// Raiser → everyone (including itself), one server-side fan-out hop. Lowest
		// latency; per-sender ordered but NOT ordered against authority state
		// changes. For fire-and-forget payloads (mesh chunks, cosmetic triggers).
		Direct = 0,

		// Raiser → authority (validate) → rebroadcast to everyone. Totally ordered
		// with all SyncVariable/List/Dictionary changes: whatever the authority
		// applied before rebroadcasting is visible to every receiver first.
		ViaAuthority = 1,

		// Raiser → authority only; no rebroadcast. A command: Received fires solely
		// on the authority, which typically reacts by mutating synced state.
		ToAuthority = 2,

		// Like Direct, but unreliable and unordered: split into MTU-sized pieces, and a
		// payload missing any piece is never delivered. Nothing waits on a lost packet,
		// so a stall can't hold up reliable traffic. For streams where the next payload
		// supersedes a lost one (depth frames). Delivered locally at once.
		DirectUnreliable = 3
	}

	// The transient sibling of SyncVariable — a replicated event with no
	// NetworkObject, replacing both RPCs on singleton NetworkBehaviours and
	// CustomMessagingManager named messages (which cannot reach other clients in
	// distributed authority on the CMB service). Stateless: no snapshot for late
	// joiners, nothing to reset.
	public abstract class SyncEventBase : SyncEndpoint
	{
		private readonly EventRoute route;

		protected SyncEventBase(string name, EventRoute route) : base(name)
		{
			this.route = route;
			ResetOnDeactivate = false;
		}

		protected void RaiseBytes(byte[] payload)
		{
			switch (route)
			{
				case EventRoute.Direct:
				case EventRoute.DirectUnreliable:
					RaiseDirectBytes(payload);
					break;

				case EventRoute.ViaAuthority:
				case EventRoute.ToAuthority:
					if (SyncBus.IsAuthority) HandleRequest(SyncBus.LocalClientId, payload);
					else SyncBus.SendRequest(Id, payload);
					break;
			}
		}

		// Direct routes only. The payload is read before this returns, so its buffer can be reused.
		protected void RaiseDirectBytes(ArraySegment<byte> payload)
		{
			switch (route)
			{
				case EventRoute.Direct:
					// SendTo.Everyone invokes locally too; offline there is no bus,
					// so deliver to ourselves directly.
					if (SyncBus.Active) SyncBus.SendDirect(Id, payload);
					else ApplyDirectBorrowed(SyncBus.LocalClientId, payload);
					break;

				case EventRoute.DirectUnreliable:
					ApplyDirectBorrowed(SyncBus.LocalClientId, payload);
					SyncBus.SendDirectUnreliable(Id, payload);
					break;

				default:
					throw new InvalidOperationException("Only Direct events can raise a borrowed payload");
			}
		}

		// Direct route only: deliver to one peer instead of everyone. Addressed to
		// ourselves (or offline) it is delivered locally.
		protected void RaiseBytesTo(ulong target, ArraySegment<byte> payload)
		{
			if (route != EventRoute.Direct)
				throw new InvalidOperationException("Only reliable Direct events can be addressed to a single peer");

			if (!SyncBus.Active || target == SyncBus.LocalClientId) ApplyDirectBorrowed(SyncBus.LocalClientId, payload);
			else SyncBus.SendDirectTo(Id, target, payload);
		}

		protected virtual bool ValidateBytes(ulong sender, byte[] payload) => true;

		protected abstract void Receive(ulong sender, byte[] payload);

		// Authority side: gate, deliver, and (ViaAuthority) rebroadcast with the
		// original sender's id embedded, since the broadcast itself comes from us.
		private void HandleRequest(ulong sender, byte[] payload)
		{
			if (!ValidateBytes(sender, payload)) return;

			Receive(sender, payload);

			if (route == EventRoute.ViaAuthority)
			{
				byte[] wire = new byte[sizeof(ulong) + payload.Length];
				SyncBytes.Write(wire, 0, sender);
				Buffer.BlockCopy(payload, 0, wire, sizeof(ulong), payload.Length);
				SyncBus.SendBroadcast(Id, wire);
			}
		}

		// ---- bus plumbing ------------------------------------------------------

		internal override void ApplyBroadcast(byte[] data)
		{
			ulong sender = SyncBytes.Read<ulong>(data, 0);
			byte[] payload = new byte[data.Length - sizeof(ulong)];
			Buffer.BlockCopy(data, sizeof(ulong), payload, 0, payload.Length);
			Receive(sender, payload);
		}

		internal override void ApplyRequest(ulong sender, byte[] data)
		{
			HandleRequest(sender, data);
		}

		internal override void ApplyDirect(ulong sender, byte[] data)
		{
			Receive(sender, data);
		}
	}

	// Event with no arguments.
	public class SyncEvent : SyncEventBase
	{
		public event Action<ulong> Received = delegate { };
		public Func<ulong, bool> Validate;

		public SyncEvent(string name, EventRoute route) : base(name, route)
		{
		}

		public void Raise() => RaiseBytes(Array.Empty<byte>());

		protected override bool ValidateBytes(ulong sender, byte[] payload) =>
			Validate == null || Validate(sender);

		protected override void Receive(ulong sender, byte[] payload) =>
			Received.Invoke(sender);
	}

	// Event carrying one unmanaged argument.
	public class SyncEvent<T> : SyncEventBase where T : unmanaged
	{
		public event Action<ulong, T> Received = delegate { };
		public Func<ulong, T, bool> Validate;

		public SyncEvent(string name, EventRoute route) : base(name, route)
		{
		}

		public void Raise(T argument) => RaiseBytes(SyncBytes.Of(argument));

		protected override bool ValidateBytes(ulong sender, byte[] payload) =>
			Validate == null || Validate(sender, SyncBytes.Read<T>(payload, 0));

		protected override void Receive(ulong sender, byte[] payload) =>
			Received.Invoke(sender, SyncBytes.Read<T>(payload, 0));
	}

	// Event carrying a raw byte payload (e.g. serialized meshes). Direct payloads of any
	// size are split by the bus and paced at SyncBus.ReliableBytesPerSecond; via-authority
	// ones ride reliable fragmented delivery whole, so keep them under ~60KB (NGO caps
	// fragmented messages at 64000 bytes). DirectUnreliable payloads are split by the bus
	// too, and every extra piece is one more chance to lose the whole payload.
	public class SyncEventBytes : SyncEventBase
	{
		/// <summary>The handler owns the array and may keep it.</summary>
		public event Action<ulong, byte[]> Received;

		/// <summary>The payload is valid only during the call. Costs no array when <see cref="Received"/> has no handlers.</summary>
		public event Action<ulong, ArraySegment<byte>> ReceivedBorrowed;

		public Func<ulong, byte[], bool> Validate;

		public SyncEventBytes(string name, EventRoute route) : base(name, route)
		{
		}

		public void Raise(byte[] payload) => RaiseBytes(payload);

		/// <summary>Direct routes only. The payload is read before this returns, so its buffer can be reused.</summary>
		public void Raise(ArraySegment<byte> payload) => RaiseDirectBytes(payload);

		/// <summary>Direct route only: deliver to a single peer. The payload is read before this returns.</summary>
		public void RaiseTo(ulong target, ArraySegment<byte> payload) => RaiseBytesTo(target, payload);

		protected override bool ValidateBytes(ulong sender, byte[] payload) =>
			Validate == null || Validate(sender, payload);

		protected override void Receive(ulong sender, byte[] payload)
		{
			ReceivedBorrowed?.Invoke(sender, payload);
			Received?.Invoke(sender, payload);
		}

		internal override void ApplyDirectBorrowed(ulong sender, ArraySegment<byte> data)
		{
			ReceivedBorrowed?.Invoke(sender, data);
			Received?.Invoke(sender, data.ToArray());
		}
	}
}
