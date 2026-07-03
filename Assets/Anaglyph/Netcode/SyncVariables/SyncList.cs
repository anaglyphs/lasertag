using System;
using System.Collections;
using System.Collections.Generic;

namespace Anaglyph.Netcode
{
	// A replicated list with no NetworkObject of its own — the drop-in for a
	// NetworkList<T> on a plain MonoBehaviour singleton, and the collection sibling
	// of SyncVariable<T>. Routed through SyncBus under the hash of its string name.
	//
	// The authority mutates (Add / RemoveAt / Set / Clear) and every peer mirrors the
	// change in the same order; other peers use RequestAdd / RequestRemove /
	// RequestClear, which the authority validates. Index-based deltas are safe
	// because every peer applies the authority's mutations in identical order, so
	// indices always refer to the same base state.
	//
	// Changed fires after any mutation from any source; re-read the list rather than
	// trusting a delta, the way NetworkList<T>.OnListChanged is typically used.
	public class SyncList<T> : SyncEndpoint, IReadOnlyList<T> where T : unmanaged
	{
		private enum Op : byte
		{
			Add = 0,
			RemoveAt = 1,
			Set = 2,
			Clear = 3,
			RemoveItem = 4 // request-only: remove by value, resolved on the authority
		}

		private readonly T[] initial;
		private readonly List<T> items = new();

		public event Action Changed = delegate { };

		// Authority-side gates for requests: (sender, item) => accept? Null accepts
		// everything. Also consulted for the authority's own Request calls.
		public Func<ulong, T, bool> ValidateAdd;
		public Func<ulong, T, bool> ValidateRemove;
		public Func<ulong, bool> ValidateClear;

		public SyncList(string name, IEnumerable<T> initialItems = null) : base(name)
		{
			initial = initialItems != null ? new List<T>(initialItems).ToArray() : Array.Empty<T>();
			items.AddRange(initial);
		}

		// ---- read access -------------------------------------------------------

		public int Count => items.Count;
		public T this[int index] => items[index];

		public int IndexOf(T item)
		{
			EqualityComparer<T> comparer = EqualityComparer<T>.Default;
			for (int i = 0; i < items.Count; i++)
				if (comparer.Equals(items[i], item))
					return i;
			return -1;
		}

		public bool Contains(T item) => IndexOf(item) >= 0;

		public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();
		IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();

		// ---- authority-only mutation -------------------------------------------

		public void Add(T item)
		{
			RequireAuthority(Name);
			items.Add(item);
			Changed.Invoke();
			SyncBus.SendBroadcast(Id, EncodeItem(Op.Add, item));
		}

		public void RemoveAt(int index)
		{
			RequireAuthority(Name);
			items.RemoveAt(index);
			Changed.Invoke();
			SyncBus.SendBroadcast(Id, EncodeIndex(Op.RemoveAt, index));
		}

		public void Set(int index, T item)
		{
			RequireAuthority(Name);
			items[index] = item;
			Changed.Invoke();
			SyncBus.SendBroadcast(Id, EncodeIndexedItem(index, item));
		}

		public void Clear()
		{
			RequireAuthority(Name);
			items.Clear();
			Changed.Invoke();
			SyncBus.SendBroadcast(Id, new[] { (byte)Op.Clear });
		}

		// Identity remove resolved against the canonical (authority) list.
		public void Remove(T item)
		{
			RequireAuthority(Name);
			int index = IndexOf(item);
			if (index >= 0) RemoveAt(index);
		}

		// ---- any-peer requests ---------------------------------------------------

		public void RequestAdd(T item)
		{
			if (SyncBus.IsAuthority) RequestAddChecked(SyncBus.LocalClientId, item);
			else SyncBus.SendRequest(Id, EncodeItem(Op.Add, item));
		}

		public void RequestRemove(T item)
		{
			if (SyncBus.IsAuthority) RequestRemoveChecked(SyncBus.LocalClientId, item);
			else SyncBus.SendRequest(Id, EncodeItem(Op.RemoveItem, item));
		}

		public void RequestClear()
		{
			if (SyncBus.IsAuthority) RequestClearChecked(SyncBus.LocalClientId);
			else SyncBus.SendRequest(Id, new[] { (byte)Op.Clear });
		}

		private void RequestAddChecked(ulong sender, T item)
		{
			if (ValidateAdd == null || ValidateAdd(sender, item)) Add(item);
		}

		private void RequestRemoveChecked(ulong sender, T item)
		{
			if (ValidateRemove == null || ValidateRemove(sender, item)) Remove(item);
		}

		private void RequestClearChecked(ulong sender)
		{
			if (ValidateClear == null || ValidateClear(sender)) Clear();
		}

		// ---- bus plumbing ------------------------------------------------------

		internal override void ApplyBroadcast(byte[] data)
		{
			switch ((Op)data[0])
			{
				case Op.Add:
					items.Add(SyncBytes.Read<T>(data, 1));
					break;

				case Op.RemoveAt:
					int removeIndex = SyncBytes.Read<int>(data, 1);
					if (removeIndex >= 0 && removeIndex < items.Count) items.RemoveAt(removeIndex);
					break;

				case Op.Set:
					int setIndex = SyncBytes.Read<int>(data, 1);
					if (setIndex >= 0 && setIndex < items.Count)
						items[setIndex] = SyncBytes.Read<T>(data, 1 + sizeof(int));
					break;

				case Op.Clear:
					items.Clear();
					break;
			}

			Changed.Invoke();
		}

		internal override void ApplyRequest(ulong sender, byte[] data)
		{
			switch ((Op)data[0])
			{
				case Op.Add:
					RequestAddChecked(sender, SyncBytes.Read<T>(data, 1));
					break;

				case Op.RemoveItem:
					RequestRemoveChecked(sender, SyncBytes.Read<T>(data, 1));
					break;

				case Op.Clear:
					RequestClearChecked(sender);
					break;
			}
		}

		internal override byte[] SerializeSnapshot()
		{
			int size = SyncBytes.Size<T>();
			byte[] data = new byte[sizeof(int) + items.Count * size];
			SyncBytes.Write(data, 0, items.Count);
			for (int i = 0; i < items.Count; i++)
				SyncBytes.Write(data, sizeof(int) + i * size, items[i]);
			return data;
		}

		private bool snapshotApplied;

		internal override void ApplySnapshot(byte[] data)
		{
			int size = SyncBytes.Size<T>();
			int count = SyncBytes.Read<int>(data, 0);

			items.Clear();
			for (int i = 0; i < count; i++)
				items.Add(SyncBytes.Read<T>(data, sizeof(int) + i * size));

			snapshotApplied = true;
		}

		internal override void FlushSnapshotEvents()
		{
			if (!snapshotApplied) return;
			snapshotApplied = false;
			Changed.Invoke();
		}

		internal override void ResetState()
		{
			items.Clear();
			items.AddRange(initial);
			Changed.Invoke();
		}

		// ---- payload encoding ----------------------------------------------------

		private static byte[] EncodeItem(Op op, T item)
		{
			byte[] data = new byte[1 + SyncBytes.Size<T>()];
			data[0] = (byte)op;
			SyncBytes.Write(data, 1, item);
			return data;
		}

		private static byte[] EncodeIndex(Op op, int index)
		{
			byte[] data = new byte[1 + sizeof(int)];
			data[0] = (byte)op;
			SyncBytes.Write(data, 1, index);
			return data;
		}

		private static byte[] EncodeIndexedItem(int index, T item)
		{
			byte[] data = new byte[1 + sizeof(int) + SyncBytes.Size<T>()];
			data[0] = (byte)Op.Set;
			SyncBytes.Write(data, 1, index);
			SyncBytes.Write(data, 1 + sizeof(int), item);
			return data;
		}
	}
}
