// using System;
// using System.Collections;
// using System.Collections.Generic;
// using Unity.Collections;
// using Unity.Netcode;
//
// namespace Anaglyph.Netcode
// {
// 	// Host-authoritative replicated list with no NetworkObject, built on
// 	// CustomMessagingManager named messages. The scalar sibling of SyncVariable<T>:
// 	// the host mutates, every client receives the delta, and late joiners pull a
// 	// full snapshot in Register(). See SyncVariable<T> for the value version.
// 	//
// 	// Snapshot replies and live deltas share the one data channel (ch) and go out
// 	// ReliableSequenced, so a late joiner's snapshot and any concurrent Add/Clear
// 	// stay correctly ordered relative to each other regardless of interleaving.
// 	public class SyncList<T> : IReadOnlyList<T> where T : unmanaged
// 	{
// 		private enum Op : byte
// 		{
// 			Snapshot,
// 			Add,
// 			RemoveAt,
// 			Clear
// 		}
//
// 		private readonly string ch; // data channel, e.g. "coloc.anchors"
// 		private readonly string req; // snapshot-request channel, e.g. "coloc.anchors.req"
// 		private readonly string addCh; // add-request channel, e.g. "coloc.anchors.add"
// 		private readonly string remCh; // remove-request channel, e.g. "coloc.anchors.rem"
// 		private readonly List<T> items = new();
//
// 		// Fired after the list mutates from any source (local host write or an
// 		// incoming message). Mirrors how NetworkList.OnListChanged was used:
// 		// re-read the whole list, don't trust the delta.
// 		public event Action Changed = delegate { };
//
// 		// Optional authority-side gates for client-submitted mutations (see
// 		// SubmitAdd / SubmitRemove). (senderClientId, item) => accept? Null
// 		// accepts everything.
// 		public Func<ulong, T, bool> CanAdd;
// 		public Func<ulong, T, bool> CanRemove;
//
// 		private static NetworkManager Net => NetworkManager.Singleton;
// 		private static CustomMessagingManager Msg => Net.CustomMessagingManager;
//
// 		public SyncList(string channel)
// 		{
// 			ch = channel;
// 			req = channel + ".req";
// 			addCh = channel + ".add";
// 			remCh = channel + ".rem";
// 		}
//
// 		// ---- read access ------------------------------------------------------
//
// 		public int Count => items.Count;
// 		public T this[int index] => items[index];
//
// 		public List<T>.Enumerator GetEnumerator()
// 		{
// 			return items.GetEnumerator();
// 		}
//
// 		IEnumerator<T> IEnumerable<T>.GetEnumerator()
// 		{
// 			return items.GetEnumerator();
// 		}
//
// 		IEnumerator IEnumerable.GetEnumerator()
// 		{
// 			return items.GetEnumerator();
// 		}
//
// 		// ---- lifecycle --------------------------------------------------------
//
// 		public void Register()
// 		{
// 			Msg.RegisterNamedMessageHandler(ch, OnData);
// 			if (NetSync.IsAuthority)
// 			{
// 				Msg.RegisterNamedMessageHandler(req, OnRequest); // authority answers pulls
// 				Msg.RegisterNamedMessageHandler(addCh, OnAddRequest); // ...and add-requests
// 				Msg.RegisterNamedMessageHandler(remCh, OnRemoveRequest); // ...and remove-requests
// 			}
// 			else RequestSnapshot(); // non-authority pulls now
// 		}
//
// 		public void Unregister()
// 		{
// 			if (Net == null) return;
// 			Msg.UnregisterNamedMessageHandler(ch);
// 			if (NetSync.IsAuthority)
// 			{
// 				Msg.UnregisterNamedMessageHandler(req);
// 				Msg.UnregisterNamedMessageHandler(addCh);
// 				Msg.UnregisterNamedMessageHandler(remCh);
// 			}
// 		}
//
// 		// ---- client submit (any peer proposes, authority applies) ------------
//
// 		// Any peer adds an item. The authority adds directly; everyone else sends
// 		// a request the authority validates (CanAdd) and applies — which then
// 		// broadcasts the Add to all. Concurrent submits are serialized on the
// 		// authority, so there's still one canonical list and order.
// 		public void SubmitAdd(T item)
// 		{
// 			if (NetSync.IsAuthority)
// 			{
// 				Add(item);
// 				return;
// 			}
//
// 			using FastBufferWriter w = new(NetSync.Size<T>(), Allocator.Temp);
// 			NetSync.Write(w, item);
// 			Msg.SendNamedMessage(addCh, NetSync.AuthorityClientId, w, NetworkDelivery.Reliable);
// 		}
//
// 		// Any peer removes an item by identity (EqualityComparer<T>.Default — give
// 		// T an IEquatable<T> with identity semantics, like AnchorPoseEntry keyed by
// 		// TrackableId). Routed through the authority so the index is resolved
// 		// against the canonical list, never a stale local one.
// 		public void SubmitRemove(T item)
// 		{
// 			if (NetSync.IsAuthority)
// 			{
// 				Remove(item);
// 				return;
// 			}
//
// 			using FastBufferWriter w = new(NetSync.Size<T>(), Allocator.Temp);
// 			NetSync.Write(w, item);
// 			Msg.SendNamedMessage(remCh, NetSync.AuthorityClientId, w, NetworkDelivery.Reliable);
// 		}
//
// 		// ---- authority-only mutation -----------------------------------------
//
// 		public void Add(T item)
// 		{
// 			RequireAuthority();
// 			items.Add(item);
// 			Changed.Invoke();
//
// 			using FastBufferWriter w = new(sizeof(byte) + NetSync.Size<T>(), Allocator.Temp);
// 			w.WriteValueSafe(Op.Add);
// 			NetSync.Write(w, item);
// 			Msg.SendNamedMessageToAll(ch, w, NetworkDelivery.ReliableSequenced);
// 		}
//
// 		public void RemoveAt(int index)
// 		{
// 			RequireAuthority();
// 			items.RemoveAt(index);
// 			Changed.Invoke();
//
// 			using FastBufferWriter w = new(sizeof(byte) + sizeof(int), Allocator.Temp);
// 			w.WriteValueSafe(Op.RemoveAt);
// 			w.WriteValueSafe(index);
// 			Msg.SendNamedMessageToAll(ch, w, NetworkDelivery.ReliableSequenced);
// 		}
//
// 		public void Clear()
// 		{
// 			RequireAuthority();
// 			items.Clear();
// 			Changed.Invoke();
//
// 			using FastBufferWriter w = new(sizeof(byte), Allocator.Temp);
// 			w.WriteValueSafe(Op.Clear);
// 			Msg.SendNamedMessageToAll(ch, w, NetworkDelivery.ReliableSequenced);
// 		}
//
// 		// Identity remove: resolve the item to an index on the canonical list, then
// 		// RemoveAt (which broadcasts the index). Safe because every client's list
// 		// mirrors the authority's, so the index means the same thing everywhere.
// 		public void Remove(T item)
// 		{
// 			RequireAuthority();
//
// 			EqualityComparer<T> cmp = EqualityComparer<T>.Default;
// 			for (int i = 0; i < items.Count; i++)
// 				if (cmp.Equals(items[i], item))
// 				{
// 					RemoveAt(i);
// 					return;
// 				}
// 		}
//
// 		// ---- snapshot handshake ----------------------------------------------
//
// 		private void RequestSnapshot()
// 		{
// 			using FastBufferWriter w = new(0, Allocator.Temp);
// 			Msg.SendNamedMessage(req, NetSync.AuthorityClientId, w, NetworkDelivery.Reliable);
// 		}
//
// 		private void OnRequest(ulong sender, FastBufferReader _)
// 		{
// 			int size = sizeof(byte) + sizeof(int) + items.Count * NetSync.Size<T>();
// 			using FastBufferWriter w = new(size, Allocator.Temp);
// 			w.WriteValueSafe(Op.Snapshot);
// 			w.WriteValueSafe(items.Count);
// 			foreach (T item in items)
// 				NetSync.Write(w, item);
// 			// Fragmented: a full snapshot can exceed a single MTU for large lists.
// 			Msg.SendNamedMessage(ch, sender, w, NetworkDelivery.ReliableFragmentedSequenced);
// 		}
//
// 		private void OnAddRequest(ulong sender, FastBufferReader r)
// 		{
// 			T item = NetSync.Read<T>(r);
// 			if (CanAdd == null || CanAdd(sender, item)) Add(item);
// 		}
//
// 		private void OnRemoveRequest(ulong sender, FastBufferReader r)
// 		{
// 			T item = NetSync.Read<T>(r);
// 			if (CanRemove == null || CanRemove(sender, item)) Remove(item);
// 		}
//
// 		// ---- incoming ---------------------------------------------------------
//
// 		private void OnData(ulong _, FastBufferReader r)
// 		{
// 			if (NetSync.IsAuthority) return; // authority is the source of truth; never apply remote ops
//
// 			r.ReadValueSafe(out Op op);
// 			switch (op)
// 			{
// 				case Op.Snapshot:
// 					r.ReadValueSafe(out int count);
// 					items.Clear();
// 					for (int i = 0; i < count; i++)
// 					{
// 						T item = NetSync.Read<T>(r);
// 						items.Add(item);
// 					}
//
// 					break;
//
// 				case Op.Add:
// 					T added = NetSync.Read<T>(r);
// 					items.Add(added);
// 					break;
//
// 				case Op.RemoveAt:
// 					r.ReadValueSafe(out int index);
// 					if (index >= 0 && index < items.Count) items.RemoveAt(index);
// 					break;
//
// 				case Op.Clear:
// 					items.Clear();
// 					break;
// 			}
//
// 			Changed.Invoke();
// 		}
//
// 		private static void RequireAuthority()
// 		{
// 			if (!NetSync.IsAuthority) throw new InvalidOperationException("SyncList: authority-only write");
// 		}
// 	}
// }

