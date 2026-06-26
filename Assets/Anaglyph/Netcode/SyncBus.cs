using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode
{
	// One registered replicated object (a SyncVariable<T> or SyncList<T>). The bus
	// routes traffic to it by integer id; the object owns its own wire format and
	// just sees opaque byte payloads.
	internal interface ISyncEndpoint
	{
		// Any peer: apply an authority broadcast or a snapshot reply.
		void ReceiveBroadcast(byte[] data);

		// Authority only: full current state, for a late joiner's snapshot pull.
		byte[] SerializeSnapshot();

		// Authority only: a non-authority peer's proposed change to validate/apply.
		void ReceiveSubmit(ulong sender, byte[] data);
	}

	// A single NetworkObject-backed relay that lets ordinary MonoBehaviour
	// singletons replicate state without each becoming a NetworkBehaviour.
	//
	// The problem: a NetworkVariable only lives inside a NetworkBehaviour, and the
	// netcode runtime instantiates/destroys those GameObjects per session — hostile
	// to singletons. So instead, the singletons stay plain MonoBehaviours holding
	// SyncVariable<T> / SyncList<T> fields; those register here under a unique
	// integer id and get all their cross-client traffic carried by this one
	// NetworkBehaviour's RPCs. Only this dummy relay is a NetworkObject.
	//
	// Why RPCs and not CustomMessagingManager: in distributed authority, custom
	// (named/unnamed) messages are not relayed peer-to-peer — a broadcast to >1
	// client throws "not yet supported in distributed authority." SendTo.Everyone
	// RPCs are relayed, so they work on both LAN (DAHost) and the CMB service.
	//
	// Authority is the owner of this object's NetworkObject (SendTo.Authority
	// resolves to it in DA: the DAHost on LAN, the session owner on CMB). The
	// authority is the single writer — it applies a change locally then broadcasts
	// it. Non-authority peers route writes through SubmitRpc and pull current state
	// on join via RequestSnapshotRpc. Authority is shared across every registered
	// object, so it moves as a unit (see RequestAuthority).
	//
	// Place this component on an in-scene NetworkObject in the networked scene (like
	// the other shared singletons); NGO spawns it per session and the registry below
	// survives across those spawns because the endpoints are owned by the stable
	// singletons, not by this transient relay.
	public class SyncBus : NetworkBehaviour
	{
		// Static so registration is independent of this object's spawn lifetime: a
		// singleton can register before the bus spawns or after it despawns.
		private static readonly Dictionary<int, ISyncEndpoint> endpoints = new();

		public static SyncBus Instance { get; private set; }

		// True when the relay exists and the session is up. Off-network (single
		// player) there is no bus and every SyncVariable/SyncList is purely local.
		public static bool Active => Instance != null && Instance.IsSpawned;

		// Whether the local peer may apply writes directly. True when it owns the
		// bus, or when there is no bus at all (offline / not yet connected).
		public static bool IsAuthority => Instance == null || Instance.HasAuthority;

		public override void OnNetworkSpawn()
		{
			Instance = this;

			// Late join: pull current state for everything already registered. (Newly
			// registering endpoints request their own snapshot from Register.)
			if (!HasAuthority)
				foreach (int id in endpoints.Keys)
					RequestSnapshotRpc(id);
		}

		public override void OnNetworkDespawn()
		{
			if (Instance == this) Instance = null;
		}

		// Take authority over all synced state for the local peer. Mirrors the
		// NetworkObject.ChangeOwnership pattern used elsewhere for "realign everyone."
		public static void RequestAuthority()
		{
			if (Active && !Instance.HasAuthority)
				Instance.NetworkObject.ChangeOwnership(NetworkManager.Singleton.LocalClientId);
		}

		// ---- registry --------------------------------------------------------

		internal static void Register(int id, ISyncEndpoint endpoint)
		{
			if (endpoints.TryGetValue(id, out ISyncEndpoint existing) && existing != endpoint)
				Debug.LogError($"[SyncBus] Two sync objects registered with id {id}; ids must be unique.");

			endpoints[id] = endpoint;

			// Bus already up and we're not the authority → pull this object's state now.
			// (If the bus isn't up yet, OnNetworkSpawn will pull for it.)
			if (Active && !Instance.HasAuthority)
				Instance.RequestSnapshotRpc(id);
		}

		internal static void Unregister(int id, ISyncEndpoint endpoint)
		{
			if (endpoints.TryGetValue(id, out ISyncEndpoint existing) && existing == endpoint)
				endpoints.Remove(id);
		}

		// ---- send helpers (called by endpoints) ------------------------------

		// Authority → everyone: an applied value/delta for endpoint `id`.
		internal static void Broadcast(int id, byte[] data)
		{
			if (Active) Instance.BroadcastRpc(id, data);
		}

		// Non-authority → authority: propose a change to endpoint `id`.
		internal static void Submit(int id, byte[] data)
		{
			if (Active) Instance.SubmitRpc(id, data);
		}

		// ---- RPCs ------------------------------------------------------------

		// Authority broadcasts an already-applied change to all peers.
		// InvokePermission.Owner stops non-authority peers from broadcasting directly
		// (they must Submit); the HasAuthority guard skips the sender's own loopback,
		// since the authority applied the change before broadcasting.
		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
		private void BroadcastRpc(int id, byte[] data)
		{
			if (HasAuthority) return;
			if (endpoints.TryGetValue(id, out ISyncEndpoint e))
				e.ReceiveBroadcast(data);
		}

		// A non-authority peer proposes a change; only the authority receives this.
		[Rpc(SendTo.Authority)]
		private void SubmitRpc(int id, byte[] data, RpcParams rpc = default)
		{
			if (endpoints.TryGetValue(id, out ISyncEndpoint e))
				e.ReceiveSubmit(rpc.Receive.SenderClientId, data);
		}

		// A joiner asks the authority for endpoint `id`'s current state.
		[Rpc(SendTo.Authority)]
		private void RequestSnapshotRpc(int id, RpcParams rpc = default)
		{
			if (!endpoints.TryGetValue(id, out ISyncEndpoint e)) return;

			byte[] snapshot = e.SerializeSnapshot();
			if (snapshot != null)
				SnapshotRpc(id, snapshot, RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
		}

		// The authority's snapshot reply to the one client that asked. Goes through
		// the same apply path as a broadcast (the payload self-describes its op).
		[Rpc(SendTo.SpecifiedInParams)]
		private void SnapshotRpc(int id, byte[] data, RpcParams rpc)
		{
			if (HasAuthority) return; // never overwrite the source of truth
			if (endpoints.TryGetValue(id, out ISyncEndpoint e))
				e.ReceiveBroadcast(data);
		}
	}

	// Blittable (de)serialization for the opaque payloads above. FastBufferWriter's
	// typed overloads split by constraint (ForPrimitives / ForEnums / ForStructs /
	// ForNetworkSerializable) and a bare `T : unmanaged` matches none, so we memcpy
	// raw bytes instead — the same approach NGO's INetworkSerializeByMemcpy uses.
	// No endianness handling: every peer here is the same architecture. Requires
	// allowUnsafeCode (already set on this asmdef).
	internal static class SyncBytes
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
