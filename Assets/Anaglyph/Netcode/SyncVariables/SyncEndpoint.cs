using System;

namespace Anaglyph.Netcode
{
	// Base of every replicated object carried by the SyncBus (SyncVariable, SyncList,
	// SyncDictionary, SyncEvent). An endpoint is a plain C# object owned by a stable
	// MonoBehaviour singleton; it registers with the bus under the hash of its string
	// name and exchanges opaque byte payloads through the bus's RPCs.
	//
	// Lifecycle contract:
	// - Construct with a name and (for stateful types) an initial value. The initial
	//   state must be identical on every peer — it is never sent over the network.
	// - Register() in Awake, Unregister() in OnDestroy. Registering resets state back
	//   to the initial value (see ResetOnDeactivate), so an endpoint always starts a
	//   scene load in a known state even with domain reload disabled.
	// - Synced fires when full state is in place: on the authority right after
	//   SyncBus.Activated, on other peers whenever a snapshot applies (join, late
	//   registration, authority change).
	public abstract class SyncEndpoint
	{
		public string Name { get; }
		internal uint Id { get; }

		// Return to the constructor-default state when a session ends (and on
		// Register). Stateless endpoints (events) ignore this.
		public bool ResetOnDeactivate = true;

		public event Action Synced = delegate { };

		protected SyncEndpoint(string name)
		{
			Name = name;
			Id = HashName(name);
		}

		public void Register() => SyncBus.Register(this);
		public void Unregister() => SyncBus.Unregister(this);

		// A delta (or event payload) the authority applied and broadcast.
		internal abstract void ApplyBroadcast(byte[] data);

		// A non-authority peer's proposed change, handled on the authority:
		// validate, then apply + broadcast.
		internal virtual void ApplyRequest(ulong sender, byte[] data) { }

		// Direct (non-serialized) event traffic from any peer, including ourselves.
		internal virtual void ApplyDirect(ulong sender, byte[] data) { }

		// Full current state for a joiner's snapshot; null = stateless (events).
		internal virtual byte[] SerializeSnapshot() => null;

		// Applies WITHOUT firing change events; the bus applies every endpoint in a
		// combined snapshot first, then calls FlushSnapshotEvents on each, so no
		// handler can observe partially-applied cross-endpoint state.
		internal virtual void ApplySnapshot(byte[] data) { }

		internal virtual void FlushSnapshotEvents() { }

		internal virtual void ResetState() { }

		internal void InvokeSynced() => Synced.Invoke();

		protected static void RequireAuthority(string name)
		{
			if (!SyncBus.IsAuthority)
				throw new InvalidOperationException(
					$"'{name}': only the bus authority may mutate directly. Use the Request methods from other peers.");
		}

		// FNV-1a, deterministic across devices/sessions so both sides of a connection
		// derive the same id from the same string.
		internal static uint HashName(string name)
		{
			uint hash = 2166136261u;
			foreach (char c in name)
			{
				hash ^= c;
				hash *= 16777619u;
			}

			return hash;
		}
	}

	// Blittable (de)serialization for endpoint payloads (public so consumers can
	// build SyncEventBytes payloads with it). FastBufferWriter's typed overloads
	// split by constraint and a bare `T : unmanaged` matches none, so we memcpy raw
	// bytes — the same thing NGO's INetworkSerializeByMemcpy does. No endianness
	// handling: every target (Quest/ARM64, desktop editors) is little-endian.
	// Requires allowUnsafeCode (already set on this asmdef).
	public static class SyncBytes
	{
		public static unsafe int Size<T>() where T : unmanaged => sizeof(T);

		public static unsafe void Write<T>(byte[] dst, int offset, T value) where T : unmanaged
		{
			fixed (byte* p = dst) *(T*)(p + offset) = value;
		}

		public static unsafe T Read<T>(byte[] src, int offset) where T : unmanaged
		{
			fixed (byte* p = src) return *(T*)(p + offset);
		}

		public static byte[] Of<T>(T value) where T : unmanaged
		{
			byte[] data = new byte[Size<T>()];
			Write(data, 0, value);
			return data;
		}
	}
}
