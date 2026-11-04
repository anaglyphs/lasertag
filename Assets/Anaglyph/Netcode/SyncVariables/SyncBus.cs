using System;
using System.Buffers;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode.SyncVariables
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

		public static SyncBus Current { get; private set; }

		// True when the relay exists and the session is up. Off-network there is no
		// bus and every endpoint is purely local.
		public static bool Active => Current != null && Current.IsSpawned;

		// Whether the local peer may mutate endpoints directly. True when it owns the
		// bus, or when there is no bus at all (offline / not yet connected).
		public static bool IsAuthority => !Active || Current.HasAuthority;

		public static ulong LocalClientId =>
			NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;

		// Session came up / went down. On Activated the authority is already known:
		// write host-configured initial values in an Activated handler and they land
		// before any endpoint's Synced fires.
		public static event Action Activated = delegate { };
		public static event Action Deactivated = delegate { };

		// The bus changed owner; argument is whether the local peer is now authority.
		public static event Action<bool> AuthorityChanged = delegate { };

		// Statics persist across play sessions while domain reload is disabled.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init()
		{
			endpoints.Clear();
			partialPayloads.Clear();
			unreliableSendSequences.Clear();
			ClearLargeMessages();
			Current = null;
			Activated = delegate { };
			Deactivated = delegate { };
			AuthorityChanged = delegate { };
		}

		// Take authority over all synced state for the local peer (e.g. "realign
		// everyone" flows). Completion is signalled by AuthorityChanged(true).
		public static void RequestAuthority()
		{
			if (Active && !Current.HasAuthority)
				Current.NetworkObject.ChangeOwnership(NetworkManager.Singleton.LocalClientId);
		}

		// ---- lifecycle ---------------------------------------------------------

		public override void OnNetworkSpawn()
		{
			Current = this;

			Activated.Invoke();

			if (HasAuthority)
				// Everything registered locally is canonical (Activated handlers just
				// wrote host initial values).
				foreach (SyncEndpoint endpoint in endpoints.Values)
					endpoint.InvokeSynced();
			else
				RequestSyncRpc();
		}

		public override void OnNetworkDespawn()
		{
			if (Current == this) Current = null;

			partialPayloads.Clear();
			unreliableSendSequences.Clear();
			ClearLargeMessages();

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

			if (Current.HasAuthority) endpoint.InvokeSynced();
			else Current.RequestSnapshotRpc(endpoint.Id);
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
			if (Active) Current.BroadcastRpc(id, data);
		}

		// Any peer → authority: a proposed change or a via-authority event.
		internal static void SendRequest(uint id, byte[] data)
		{
			if (Active) Current.RequestRpc(id, data);
		}

		// Any peer → everyone (including itself): a direct event. One over a reliable piece,
		// or behind one still going out on this endpoint, is delivered here at once and
		// queued for everyone else (see "large messages").
		internal static void SendDirect(uint id, ArraySegment<byte> data)
		{
			if (!Active) return;
			if (data.Count <= ReliablePieceBytes && !outgoing.ContainsKey((id, Everyone)))
			{
				Current.RaiseDirectRpc(id, new BusBytes(data));
				return;
			}

			if (endpoints.TryGetValue(id, out SyncEndpoint endpoint))
				endpoint.ApplyDirectBorrowed(LocalClientId, data);
			Enqueue(id, Everyone, data);
		}

		// Any peer → everyone else, unreliable: split into pieces that each fit NGO's
		// unreliable cap (1296 bytes, less the RPC header and, on DA, the proxy's target
		// list). Does not invoke locally.
		internal static void SendDirectUnreliable(uint id, ArraySegment<byte> data)
		{
			if (!Active) return;

			int count = Mathf.Max(1, (data.Count + UnreliablePieceBytes - 1) / UnreliablePieceBytes);
			if (count > byte.MaxValue)
			{
				Debug.LogError($"[SyncBus] Unreliable payload of {data.Count}B for {id} needs {count} pieces (max {byte.MaxValue})");
				return;
			}

			ushort sequence = unreliableSendSequences.TryGetValue(id, out ushort last) ? (ushort)(last + 1) : (ushort)0;
			unreliableSendSequences[id] = sequence;

			for (int i = 0; i < count; i++)
			{
				int offset = i * UnreliablePieceBytes;
				ArraySegment<byte> piece = new(data.Array, data.Offset + offset, Mathf.Min(UnreliablePieceBytes, data.Count - offset));
				Current.RaiseDirectPieceRpc(id, sequence, (byte)i, (byte)count, new BusBytes(piece));
			}
		}

		// Any peer → one peer: a direct event addressed to a single client (e.g. a
		// snapshot for a joiner), relayed like any other RPC. Does not invoke locally.
		internal static void SendDirectTo(uint id, ulong target, ArraySegment<byte> data)
		{
			if (!Active) return;
			if (data.Count <= ReliablePieceBytes && !outgoing.ContainsKey((id, target)))
			{
				Current.RaiseDirectToRpc(id, new BusBytes(data), Current.RpcTarget.Single(target, RpcTargetUse.Temp));
				return;
			}

			Enqueue(id, target, data);
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
		private void RaiseDirectRpc(uint id, BusBytes data, RpcParams rpc = default)
		{
			if (endpoints.TryGetValue(id, out SyncEndpoint endpoint))
				endpoint.ApplyDirectBorrowed(rpc.Receive.SenderClientId, data.Bytes);
			data.ReturnToPool();
		}

		[Rpc(SendTo.SpecifiedInParams)]
		private void RaiseDirectToRpc(uint id, BusBytes data, RpcParams rpc = default)
		{
			if (endpoints.TryGetValue(id, out SyncEndpoint endpoint))
				endpoint.ApplyDirectBorrowed(rpc.Receive.SenderClientId, data.Bytes);
			data.ReturnToPool();
		}

		// A slice of any buffer on the sending side, an array from the shared pool on the
		// receiving one: no array per message at either end. The receiving RPC returns it.
		private struct BusBytes : INetworkSerializable
		{
			public ArraySegment<byte> Bytes;

			public BusBytes(ArraySegment<byte> bytes) => Bytes = bytes;

			public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
			{
				int count = Bytes.Count;
				serializer.SerializeValue(ref count);

				if (serializer.IsWriter)
				{
					serializer.GetFastBufferWriter().WriteBytesSafe(Bytes.Array, count, Bytes.Offset);
					return;
				}

				FastBufferReader reader = serializer.GetFastBufferReader();
				int remaining = reader.Length - reader.Position;
				if (count < 0 || count > remaining)
					throw new OverflowException($"[SyncBus] Payload claims {count} bytes with {remaining} left in the message");

				byte[] rented = ArrayPool<byte>.Shared.Rent(count);
				reader.ReadBytesSafe(ref rented, count);
				Bytes = new ArraySegment<byte>(rented, 0, count);
			}

			public void ReturnToPool() => ArrayPool<byte>.Shared.Return(Bytes.Array);
		}

		// ---- unreliable pieces -------------------------------------------------

		// payload bytes per piece: well under 1296 with room for the RPC header and a
		// DA proxy's target list (8 bytes per client)
		private const int UnreliablePieceBytes = 1024;

		// The largest DirectUnreliable payload: the most pieces a piece number can count.
		public const int MaxUnreliableBytes = byte.MaxValue * UnreliablePieceBytes;

		private static readonly Dictionary<uint, ushort> unreliableSendSequences = new();

		// one payload being reassembled per sender and endpoint: pieces may arrive in any
		// order, and a piece of a newer payload abandons an incomplete older one. Every piece
		// but the last is full-size, so each has a fixed place in a buffer kept for the next payload
		private class PartialPayload
		{
			public ushort sequence;
			public byte count;
			public int received;
			public int length;
			public byte[] buffer = Array.Empty<byte>();
			public readonly bool[] arrived = new bool[byte.MaxValue];
		}

		private static readonly Dictionary<(ulong sender, uint id), PartialPayload> partialPayloads = new();

		[Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
		private void RaiseDirectPieceRpc(uint id, ushort sequence, byte index, byte count, BusBytes data, RpcParams rpc = default)
		{
			ReceivePiece(rpc.Receive.SenderClientId, id, sequence, index, count, data.Bytes);
			data.ReturnToPool();
		}

		private static void ReceivePiece(ulong sender, uint id, ushort sequence, byte index, byte count, ArraySegment<byte> piece)
		{
			if (count == 0 || index >= count || piece.Count > UnreliablePieceBytes) return;
			if (!endpoints.TryGetValue(id, out SyncEndpoint endpoint)) return;

			if (count == 1)
			{
				endpoint.ApplyDirectBorrowed(sender, piece);
				return;
			}

			var key = (sender, id);
			if (!partialPayloads.TryGetValue(key, out PartialPayload partial))
				partialPayloads[key] = partial = new PartialPayload { sequence = (ushort)(sequence - 1) };

			if (partial.sequence != sequence)
			{
				// a straggler from a payload already given up on
				if ((short)(sequence - partial.sequence) < 0) return;

				partial.sequence = sequence;
				partial.count = count;
				partial.received = 0;
				Array.Clear(partial.arrived, 0, count);
				if (partial.buffer.Length < count * UnreliablePieceBytes)
					partial.buffer = new byte[count * UnreliablePieceBytes];
			}

			if (partial.count != count || partial.arrived[index]) return;
			partial.arrived[index] = true;

			int offset = index * UnreliablePieceBytes;
			Buffer.BlockCopy(piece.Array, piece.Offset, partial.buffer, offset, piece.Count);
			if (index == count - 1) partial.length = offset + piece.Count;

			if (++partial.received == count)
				endpoint.ApplyDirectBorrowed(sender, new ArraySegment<byte>(partial.buffer, 0, partial.length));
		}

		// ---- large messages ------------------------------------------------------

		// Reliable direct events of any size. NGO caps a reliable message at 64000 bytes,
		// so a larger payload goes as pieces, reassembled on arrival. And paced: every
		// reliable message shares one sequenced channel, so a few MB sent at once would hold
		// every synced change behind it until the last byte landed. The pieces leave at
		// ReliableBytesPerSecond, and synced state slips in between them. Each endpoint and
		// target keeps its own queue, which later messages of the same endpoint join while
		// it drains, so per-endpoint order holds as it does for small ones. Only the direct
		// routes: splitting via-authority traffic would break its ordering against synced
		// state.

		// payload bytes per piece: well under NGO's 64000 with room for the RPC header and a
		// DA proxy's target list
		public const int ReliablePieceBytes = 32 * 1024;

		// Pieces of large messages sent a second, by this peer to all others together.
		public static int ReliableBytesPerSecond = 2_000_000;

		// Whether pieces of large messages are still waiting to leave this peer.
		public static bool IsSendingLarge => outgoing.Count > 0;

		private const ulong Everyone = ulong.MaxValue;

		private class OutgoingPiece
		{
			public uint message;
			public ushort index, count;
			public byte[] data;
		}

		private static readonly Dictionary<(uint id, ulong target), Queue<OutgoingPiece>> outgoing = new();
		private static readonly List<(uint id, ulong target)> outgoingOrder = new();
		private static int nextOutgoing;
		private static uint nextMessage;
		private static float sendBudget;

		private class IncomingMessage
		{
			public byte[][] pieces;
			public int received;
		}

		private static readonly Dictionary<(ulong sender, uint id, uint message), IncomingMessage> incoming = new();

		private static void ClearLargeMessages()
		{
			outgoing.Clear();
			outgoingOrder.Clear();
			incoming.Clear();
			sendBudget = 0;
		}

		private static void Enqueue(uint id, ulong target, ArraySegment<byte> data)
		{
			int count = Mathf.Max(1, (data.Count + ReliablePieceBytes - 1) / ReliablePieceBytes);
			if (count > ushort.MaxValue)
			{
				Debug.LogError($"[SyncBus] Reliable payload of {data.Count}B for {id} needs {count} pieces (max {ushort.MaxValue})");
				return;
			}

			var key = (id, target);
			if (!outgoing.TryGetValue(key, out Queue<OutgoingPiece> queue))
			{
				outgoing[key] = queue = new Queue<OutgoingPiece>();
				outgoingOrder.Add(key);
			}

			uint message = nextMessage++;
			for (int i = 0; i < count; i++)
			{
				int offset = i * ReliablePieceBytes;
				byte[] piece = new byte[Mathf.Min(ReliablePieceBytes, data.Count - offset)];
				Buffer.BlockCopy(data.Array, data.Offset + offset, piece, 0, piece.Length);
				queue.Enqueue(new OutgoingPiece { message = message, index = (ushort)i, count = (ushort)count, data = piece });
			}
		}

		private void Update()
		{
			if (!IsSpawned || outgoing.Count == 0) return;

			sendBudget = Mathf.Min(sendBudget + ReliableBytesPerSecond * Time.unscaledDeltaTime, 2f * ReliablePieceBytes);
			IReadOnlyList<ulong> connected = NetworkManager != null ? NetworkManager.ConnectedClientsIds : null;

			// round robin over the queues, a piece at a time, within the budget
			while (sendBudget > 0 && outgoingOrder.Count > 0)
			{
				nextOutgoing %= outgoingOrder.Count;
				(uint id, ulong target) key = outgoingOrder[nextOutgoing];
				Queue<OutgoingPiece> queue = outgoing[key];

				// a peer that left takes its queue with it
				if (key.target != Everyone && connected != null && !Contains(connected, key.target))
					queue.Clear();

				if (queue.Count > 0)
				{
					OutgoingPiece piece = queue.Dequeue();
					if (key.target == Everyone)
						RaiseDirectPartRpc(key.id, piece.message, piece.index, piece.count, piece.data);
					else
						RaiseDirectToPartRpc(key.id, piece.message, piece.index, piece.count, piece.data,
							RpcTarget.Single(key.target, RpcTargetUse.Temp));
					sendBudget -= piece.data.Length;
				}

				if (queue.Count == 0)
				{
					outgoing.Remove(key);
					outgoingOrder.RemoveAt(nextOutgoing);
				}
				else nextOutgoing++;
			}
		}

		private static bool Contains(IReadOnlyList<ulong> list, ulong value)
		{
			for (int i = 0; i < list.Count; i++)
				if (list[i] == value)
					return true;
			return false;
		}

		[Rpc(SendTo.NotMe)]
		private void RaiseDirectPartRpc(uint id, uint message, ushort index, ushort count, byte[] data, RpcParams rpc = default) =>
			ReceivePart(rpc.Receive.SenderClientId, id, message, index, count, data);

		[Rpc(SendTo.SpecifiedInParams)]
		private void RaiseDirectToPartRpc(uint id, uint message, ushort index, ushort count, byte[] data, RpcParams rpc = default) =>
			ReceivePart(rpc.Receive.SenderClientId, id, message, index, count, data);

		// a message's pieces arrive in order on the sequenced channel, but those of messages to
		// different targets interleave: each is kept apart by its number
		private static void ReceivePart(ulong sender, uint id, uint message, ushort index, ushort count, byte[] data)
		{
			if (count == 0 || index >= count) return;
			if (count == 1)
			{
				if (endpoints.TryGetValue(id, out SyncEndpoint single)) single.ApplyDirect(sender, data);
				return;
			}

			var key = (sender, id, message);
			if (!incoming.TryGetValue(key, out IncomingMessage partial))
				incoming[key] = partial = new IncomingMessage { pieces = new byte[count][] };
			if (partial.pieces.Length != count || partial.pieces[index] != null) return;
			partial.pieces[index] = data;
			if (++partial.received < count) return;

			incoming.Remove(key);
			int length = 0;
			foreach (byte[] piece in partial.pieces) length += piece.Length;
			byte[] payload = new byte[length];
			int offset = 0;
			foreach (byte[] piece in partial.pieces)
			{
				Buffer.BlockCopy(piece, 0, payload, offset, piece.Length);
				offset += piece.Length;
			}

			if (endpoints.TryGetValue(id, out SyncEndpoint endpoint)) endpoint.ApplyDirect(sender, payload);
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
				Buffer.BlockCopy(snapshot, 0, combined, offset, snapshot.Length);
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
					Buffer.BlockCopy(combined, offset, snapshot, 0, length);
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