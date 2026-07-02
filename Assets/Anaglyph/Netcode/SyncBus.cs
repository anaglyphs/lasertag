using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode
{
	// A single NetworkObject-backed relay that lets ordinary MonoBehaviour singletons
	// replicate state without each becoming a NetworkBehaviour (whose GameObjects the
	// netcode runtime manages per session — hostile to singletons).
	//
	// Why RPCs and not CustomMessagingManager: in distributed authority, custom
	// named/unnamed messages cannot reach other clients at all when using the CMB
	// service — a multi-client send errors ("not yet supported in distributed
	// authority") and a single-client send throws ("Clients may only send messages to
	// ServerClientId") because no peer is a server and the service does not relay
	// them. RPCs are relayed (wrapped in ProxyMessage, fanned out by the DAHost on
	// LAN or the service on CMB), so they are the only client→clients channel that
	// works on both.
	//
	// Ordering model — all consistency comes from one rule: the owner of this
	// NetworkObject (the "authority") is the single serialization point.
	// - Only the authority mutates state: it applies locally, then broadcasts. All
	//   traffic for every endpoint rides this one object's RPCs on reliable
	//   sequenced delivery, so every peer applies changes in the authority's exact
	//   apply order — including across different endpoints ("write dependencies
	//   first" holds).
	// - Other peers request changes (RequestRpc); the authority validates and
	//   applies. Requesters never apply locally first.
	// - Snapshots leave the authority on the same sequenced channel as broadcasts,
	//   so a joiner can never see a snapshot/delta race. Joining peers pull ONE
	//   combined snapshot of every endpoint (point-in-time consistent), applied in a
	//   single frame before any Synced fires.
	// - Broadcasts use SendTo.NotMe rather than a loopback guard, so an authority
	//   change mid-flight can't make the new authority misapply its own message.
	// - On ownership change every non-authority peer re-pulls the combined snapshot,
	//   healing anything lost in the transfer window.
	//
	// Place this on ONE in-scene GameObject with a NetworkObject (ownership
	// Transferable, so RequestAuthority works). The endpoint registry is static and
	// owned by the stable singletons, so it survives this object's spawn/despawn.
	public class SyncBus : NetworkBehaviour
	{
		private static readonly Dictionary<uint, SyncEndpoint> endpoints = new();

		public static SyncBus Instance { get; private set; }

		// True when the relay exists and the session is up. Off-network there is no
		// bus and every endpoint is purely local.
		public static bool Active => Instance != null && Instance.IsSpawned;

		// Whether the local peer may mutate endpoints directly. True when it owns the
		// bus, or when there is no bus at all (offline / not yet connected).
		public static bool IsAuthority => !Active || Instance.HasAuthority;

		public static ulong LocalClientId =>
			NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

		// Session came up / went down. On Activated the authority is already known:
		// write host-configured initial values in an Activated handler and they land
		// before any endpoint's Synced fires.
		public static event System.Action Activated = delegate { };
		public static event System.Action Deactivated = delegate { };

		// The bus changed owner; argument is whether the local peer is now authority.
		public static event System.Action<bool> AuthorityChanged = delegate { };

		// Statics persist across play sessions while domain reload is disabled.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init()
		{
			endpoints.Clear();
			Instance = null;
			Activated = delegate { };
			Deactivated = delegate { };
			AuthorityChanged = delegate { };
		}

		// Take authority over all synced state for the local peer (e.g. "realign
		// everyone" flows). Completion is signalled by AuthorityChanged(true).
		public static void RequestAuthority()
		{
			if (Active && !Instance.HasAuthority)
				Instance.NetworkObject.ChangeOwnership(NetworkManager.Singleton.LocalClientId);
		}

		// ---- lifecycle ---------------------------------------------------------

		public override void OnNetworkSpawn()
		{
			Instance = this;

			Activated.Invoke();

			if (HasAuthority)
			{
				// Everything registered locally is canonical (Activated handlers just
				// wrote host initial values).
				foreach (SyncEndpoint endpoint in endpoints.Values)
					endpoint.InvokeSynced();
			}
			else
			{
				RequestSyncRpc();
			}
		}

		public override void OnNetworkDespawn()
		{
			if (Instance == this) Instance = null;

			Deactivated.Invoke();

			foreach (SyncEndpoint endpoint in endpoints.Values)
				if (endpoint.ResetOnDeactivate)
					endpoint.ResetState();
		}

		protected override void OnOwnershipChanged(ulong previous, ulong current)
		{
			if (!IsSpawned) return;

			AuthorityChanged.Invoke(HasAuthority);

			// State may have moved while a broadcast was in flight to/from the old
			// authority; re-pulling the full snapshot self-heals.
			if (!HasAuthority)
				RequestSyncRpc();
		}

		// ---- registry ----------------------------------------------------------

		internal static void Register(SyncEndpoint endpoint)
		{
			if (endpoints.TryGetValue(endpoint.Id, out SyncEndpoint existing))
			{
				if (existing == endpoint) return;

				if (existing.Name != endpoint.Name)
				{
					Debug.LogError(
						$"[SyncBus] Name hash collision: '{endpoint.Name}' vs '{existing.Name}'. Rename one of them.");
					return;
				}
				// Same name, new object (recreated singleton): replace.
			}

			if (endpoint.ResetOnDeactivate)
				endpoint.ResetState();

			endpoints[endpoint.Id] = endpoint;

			if (!Active) return;

			if (Instance.HasAuthority) endpoint.InvokeSynced();
			else Instance.RequestSnapshotRpc(endpoint.Id);
		}

		internal static void Unregister(SyncEndpoint endpoint)
		{
			if (endpoints.TryGetValue(endpoint.Id, out SyncEndpoint existing) && existing == endpoint)
				endpoints.Remove(endpoint.Id);
		}

		// ---- send helpers (called by endpoints) --------------------------------

		// Authority → everyone else: an already-applied change.
		internal static void SendBroadcast(uint id, byte[] data)
		{
			if (Active) Instance.BroadcastRpc(id, data);
		}

		// Any peer → authority: a proposed change or a via-authority event.
		internal static void SendRequest(uint id, byte[] data)
		{
			if (Active) Instance.RequestRpc(id, data);
		}

		// Any peer → everyone (including itself): a direct event.
		internal static void SendDirect(uint id, byte[] data)
		{
			if (Active) Instance.RaiseDirectRpc(id, data);
		}

		// ---- RPCs --------------------------------------------------------------

		[Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Owner)]
		private void BroadcastRpc(uint id, byte[] data)
		{
			if (endpoints.TryGetValue(id, out SyncEndpoint endpoint))
				endpoint.ApplyBroadcast(data);
		}

		[Rpc(SendTo.Authority)]
		private void RequestRpc(uint id, byte[] data, RpcParams rpc = default)
		{
			if (!HasAuthority)
			{
				// Authority moved while this request was in flight; the sender's
				// change is dropped rather than applied by a stale authority.
				Debug.LogWarning($"[SyncBus] Dropped a request for {id}: no longer authority.");
				return;
			}

			if (endpoints.TryGetValue(id, out SyncEndpoint endpoint))
				endpoint.ApplyRequest(rpc.Receive.SenderClientId, data);
		}

		[Rpc(SendTo.Everyone)]
		private void RaiseDirectRpc(uint id, byte[] data, RpcParams rpc = default)
		{
			if (endpoints.TryGetValue(id, out SyncEndpoint endpoint))
				endpoint.ApplyDirect(rpc.Receive.SenderClientId, data);
		}

		// ---- snapshots ---------------------------------------------------------

		// Combined-snapshot wire format: [int count] then per endpoint
		// [uint id][int length][length bytes].

		[Rpc(SendTo.Authority)]
		private void RequestSyncRpc(RpcParams rpc = default)
		{
			if (!HasAuthority) return;

			List<(uint id, byte[] snapshot)> snapshots = new();
			int total = sizeof(int);

			foreach (SyncEndpoint endpoint in endpoints.Values)
			{
				byte[] snapshot = endpoint.SerializeSnapshot();
				if (snapshot == null) continue; // stateless (events)

				snapshots.Add((endpoint.Id, snapshot));
				total += sizeof(uint) + sizeof(int) + snapshot.Length;
			}

			byte[] combined = new byte[total];
			int offset = 0;

			SyncBytes.Write(combined, offset, snapshots.Count);
			offset += sizeof(int);

			foreach ((uint id, byte[] snapshot) in snapshots)
			{
				SyncBytes.Write(combined, offset, id);
				offset += sizeof(uint);
				SyncBytes.Write(combined, offset, snapshot.Length);
				offset += sizeof(int);
				System.Buffer.BlockCopy(snapshot, 0, combined, offset, snapshot.Length);
				offset += snapshot.Length;
			}

			SyncRpc(combined, RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
		}

		[Rpc(SendTo.SpecifiedInParams)]
		private void SyncRpc(byte[] combined, RpcParams rpc = default)
		{
			if (HasAuthority) return; // never overwrite the source of truth

			// Apply every snapshot silently, then flush change events, then fire
			// Synced — so no handler can observe partially-applied cross-endpoint
			// state, and Synced handlers see everything post-snapshot.
			List<SyncEndpoint> applied = new();
			int offset = 0;

			int count = SyncBytes.Read<int>(combined, offset);
			offset += sizeof(int);

			for (int i = 0; i < count; i++)
			{
				uint id = SyncBytes.Read<uint>(combined, offset);
				offset += sizeof(uint);
				int length = SyncBytes.Read<int>(combined, offset);
				offset += sizeof(int);

				if (endpoints.TryGetValue(id, out SyncEndpoint endpoint))
				{
					byte[] snapshot = new byte[length];
					System.Buffer.BlockCopy(combined, offset, snapshot, 0, length);
					endpoint.ApplySnapshot(snapshot);
					applied.Add(endpoint);
				}

				offset += length;
			}

			foreach (SyncEndpoint endpoint in applied)
				endpoint.FlushSnapshotEvents();

			foreach (SyncEndpoint endpoint in applied)
				endpoint.InvokeSynced();
		}

		// Single-endpoint pull, for endpoints registered after the join snapshot.

		[Rpc(SendTo.Authority)]
		private void RequestSnapshotRpc(uint id, RpcParams rpc = default)
		{
			if (!HasAuthority) return;
			if (!endpoints.TryGetValue(id, out SyncEndpoint endpoint)) return;

			byte[] snapshot = endpoint.SerializeSnapshot();
			if (snapshot != null)
				SnapshotRpc(id, snapshot, RpcTarget.Single(rpc.Receive.SenderClientId, RpcTargetUse.Temp));
		}

		[Rpc(SendTo.SpecifiedInParams)]
		private void SnapshotRpc(uint id, byte[] data, RpcParams rpc = default)
		{
			if (HasAuthority) return;
			if (!endpoints.TryGetValue(id, out SyncEndpoint endpoint)) return;

			endpoint.ApplySnapshot(data);
			endpoint.FlushSnapshotEvents();
			endpoint.InvokeSynced();
		}
	}
}
