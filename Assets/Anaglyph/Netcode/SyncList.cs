using System;
using System.Collections;
using System.Collections.Generic;

namespace Anaglyph.Netcode
{
	// A replicated list with no NetworkObject of its own — the drop-in for a
	// NetworkList<T> on a plain MonoBehaviour singleton, and the collection sibling
	// of SyncVariable<T>. Routed through SyncBus under a unique integer id.
	//
	// The authority mutates (Add / RemoveAt / Clear / Remove) and every peer mirrors
	// the change; non-authority peers may SubmitAdd / SubmitRemove, which the
	// authority validates via CanAdd / CanRemove. Late joiners get the whole list
	// through SyncBus's snapshot handshake. Off-network it is just a local list.
	//
	// OnChanged fires after any mutation from any source; re-read the list rather
	// than trusting a delta, the way NetworkList<T>.OnListChanged is typically used.
	// Register()/Unregister() like SyncVariable<T> (see there for timing).
	public class SyncList<T> : ISyncEndpoint, IReadOnlyList<T> where T : unmanaged
	{
		// Wire ops. Snapshot/Add/RemoveAt/Clear travel authority -> everyone;
		// Add/Remove (by value) travel non-authority -> authority as proposals.
		private enum Op : byte
		{
			Snapshot = 0,
			Add = 1,
			RemoveAt = 2,
			Clear = 3,
			Remove = 4
		}

		private readonly int id;
		private readonly List<T> items = new();

		public event Action OnChanged = delegate { };

		// Authority-side gates for SubmitAdd / SubmitRemove: (sender, item) => accept?
		// Null accepts everything.
		public Func<ulong, T, bool> CanAdd;
		public Func<ulong, T, bool> CanRemove;

		public SyncList(int id) => this.id = id;

		public void Register() => SyncBus.Register(id, this);
		public void Unregister() => SyncBus.Unregister(id, this);

		// ---- read access -----------------------------------------------------

		public int Count => items.Count;
		public T this[int index] => items[index];

		public int IndexOf(T item)
		{
			EqualityComparer<T> cmp = EqualityComparer<T>.Default;
			for (int i = 0; i < items.Count; i++)
				if (cmp.Equals(items[i], item))
					return i;
			return -1;
		}

		public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();
		IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

		// ---- authority-only mutation -----------------------------------------

		public void Add(T item)
		{
			RequireAuthority();
			items.Add(item);
			OnChanged.Invoke();
			SyncBus.Broadcast(id, EncodeItem(Op.Add, item));
		}

		public void RemoveAt(int index)
		{
			RequireAuthority();
			items.RemoveAt(index);
			OnChanged.Invoke();
			SyncBus.Broadcast(id, EncodeRemoveAt(index));
		}

		public void Clear()
		{
			RequireAuthority();
			items.Clear();
			OnChanged.Invoke();
			SyncBus.Broadcast(id, new[] { (byte)Op.Clear });
		}

		// Identity remove resolved against the canonical (authority) list; every peer
		// mirrors that list, so the broadcast index means the same thing everywhere.
		public void Remove(T item)
		{
			RequireAuthority();
			int i = IndexOf(item);
			if (i >= 0) RemoveAt(i);
		}

		// ---- any-peer submit -------------------------------------------------

		// Authority adds directly; others send the item for the authority to validate
		// (CanAdd) and add — which then broadcasts it to all.
		public void SubmitAdd(T item)
		{
			if (SyncBus.IsAuthority) Add(item);
			else SyncBus.Submit(id, EncodeItem(Op.Add, item));
		}

		// Remove by identity. Routed through the authority so the index is resolved
		// against the canonical list, never a stale local one.
		public void SubmitRemove(T item)
		{
			if (SyncBus.IsAuthority) Remove(item);
			else SyncBus.Submit(id, EncodeItem(Op.Remove, item));
		}

		// ---- ISyncEndpoint ---------------------------------------------------

		byte[] ISyncEndpoint.SerializeSnapshot()
		{
			int size = SyncBytes.Size<T>();
			byte[] data = new byte[1 + sizeof(int) + items.Count * size];
			data[0] = (byte)Op.Snapshot;
			SyncBytes.Write(data, 1, items.Count);
			for (int i = 0; i < items.Count; i++)
				SyncBytes.Write(data, 1 + sizeof(int) + i * size, items[i]);
			return data;
		}

		void ISyncEndpoint.ReceiveBroadcast(byte[] data)
		{
			int size = SyncBytes.Size<T>();
			switch ((Op)data[0])
			{
				case Op.Snapshot:
					int count = SyncBytes.Read<int>(data, 1);
					items.Clear();
					for (int i = 0; i < count; i++)
						items.Add(SyncBytes.Read<T>(data, 1 + sizeof(int) + i * size));
					break;

				case Op.Add:
					items.Add(SyncBytes.Read<T>(data, 1));
					break;

				case Op.RemoveAt:
					int index = SyncBytes.Read<int>(data, 1);
					if (index >= 0 && index < items.Count) items.RemoveAt(index);
					break;

				case Op.Clear:
					items.Clear();
					break;
			}

			OnChanged.Invoke();
		}

		void ISyncEndpoint.ReceiveSubmit(ulong sender, byte[] data)
		{
			T item = SyncBytes.Read<T>(data, 1);
			switch ((Op)data[0])
			{
				case Op.Add:
					if (CanAdd == null || CanAdd(sender, item)) Add(item);
					break;

				case Op.Remove:
					if (CanRemove == null || CanRemove(sender, item)) Remove(item);
					break;
			}
		}

		// ---- payload encoding ------------------------------------------------

		private static byte[] EncodeItem(Op op, T item)
		{
			byte[] data = new byte[1 + SyncBytes.Size<T>()];
			data[0] = (byte)op;
			SyncBytes.Write(data, 1, item);
			return data;
		}

		private static byte[] EncodeRemoveAt(int index)
		{
			byte[] data = new byte[1 + sizeof(int)];
			data[0] = (byte)Op.RemoveAt;
			SyncBytes.Write(data, 1, index);
			return data;
		}

		private static void RequireAuthority()
		{
			if (!SyncBus.IsAuthority)
				throw new InvalidOperationException(
					"SyncList: only the authority may mutate directly. Use SubmitAdd/SubmitRemove from other peers.");
		}
	}
}
