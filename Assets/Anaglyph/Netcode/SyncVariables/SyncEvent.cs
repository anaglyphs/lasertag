using System;

namespace Anaglyph.Netcode
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
		ToAuthority = 2
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
					// SendTo.Everyone invokes locally too; offline there is no bus,
					// so deliver to ourselves directly.
					if (SyncBus.Active) SyncBus.SendDirect(Id, payload);
					else Receive(SyncBus.LocalClientId, payload);
					break;

				case EventRoute.ViaAuthority:
				case EventRoute.ToAuthority:
					if (SyncBus.IsAuthority) HandleRequest(SyncBus.LocalClientId, payload);
					else SyncBus.SendRequest(Id, payload);
					break;
			}
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

	// Event carrying a raw byte payload (e.g. serialized meshes). Payloads ride
	// reliable fragmented delivery; keep them under ~60KB (NGO caps fragmented
	// messages at 64000 bytes).
	public class SyncEventBytes : SyncEventBase
	{
		public event Action<ulong, byte[]> Received = delegate { };
		public Func<ulong, byte[], bool> Validate;

		public SyncEventBytes(string name, EventRoute route) : base(name, route)
		{
		}

		public void Raise(byte[] payload) => RaiseBytes(payload);

		protected override bool ValidateBytes(ulong sender, byte[] payload) =>
			Validate == null || Validate(sender, payload);

		protected override void Receive(ulong sender, byte[] payload) =>
			Received.Invoke(sender, payload);
	}
}
