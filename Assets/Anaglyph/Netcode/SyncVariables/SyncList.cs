using System;
using System.Collections;
using System.Collections.Generic;

namespace Anaglyph.Netcode
{
	public enum SyncListOp : byte
	{
		Add = 0,
		RemoveAt = 1,
		Set = 2,
		Clear = 3,
		RemoveItem = 4, // request-only: remove by value, resolved on the authority
		Snapshot = 5
	}

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
		private readonly T[] initial;
		private readonly List<T> items = new();

		public struct EventData
		{
			public SyncListOp op;
			public int eventIndex;
			public T eventItem;

			public EventData(SyncListOp op)
			{
				this.op = op;
				eventIndex = -1;
				eventItem = default;
			}

			public EventData(SyncListOp op, int eventIndex, T eventItem)
			{
				this.op = op;
				this.eventIndex = eventIndex;
				this.eventItem = eventItem;
			}
		}

		public event Action<EventData> Changed = delegate { };

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

		public bool Contains(T item)
		{
			return IndexOf(item) >= 0;
		}

		public List<T>.Enumerator GetEnumerator()
		{
			return items.GetEnumerator();
		}

		IEnumerator<T> IEnumerable<T>.GetEnumerator()
		{
			return items.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return items.GetEnumerator();
		}

		// ---- authority-only mutation -------------------------------------------

		public void Add(T item)
		{
			RequireAuthority(Name);
			AddLocally(item);
			SyncBus.SendBroadcast(Id, EncodeItem(SyncListOp.Add, item));
		}

		public void RemoveAt(int index)
		{
			RequireAuthority(Name);
			RemoveAtLocally(index);
			SyncBus.SendBroadcast(Id, EncodeIndex(SyncListOp.RemoveAt, index));
		}

		public void Set(int index, T item)
		{
			RequireAuthority(Name);
			SetLocally(index, item);
			SyncBus.SendBroadcast(Id, EncodeIndexedItem(index, item));
		}

		public void Clear()
		{
			RequireAuthority(Name);
			ClearLocally();
			SyncBus.SendBroadcast(Id, new[] { (byte)SyncListOp.Clear });
		}

		// Identity remove resolved against the canonical (authority) list.
		public void Remove(T item)
		{
			RequireAuthority(Name);
			int index = IndexOf(item);
			if (index >= 0) RemoveAt(index);
		}

		// ---- ungated local mutation -------------------------------------

		private void AddLocally(T item)
		{
			items.Add(item);
			Changed.Invoke(new EventData(SyncListOp.Add, items.Count - 1, item));
		}

		private void RemoveAtLocally(int index)
		{
			T item = items[index];
			items.RemoveAt(index);
			Changed.Invoke(new EventData(SyncListOp.RemoveAt, index, item));
		}

		private void SetLocally(int index, T item)
		{
			items[index] = item;
			Changed.Invoke(new EventData(SyncListOp.Set, index, item));
		}

		private void ClearLocally()
		{
			items.Clear();
			Changed.Invoke(new EventData(SyncListOp.Clear));
		}

		// ---- any-peer requests ---------------------------------------------------

		public void RequestAdd(T item)
		{
			if (SyncBus.IsAuthority) RequestAddChecked(SyncBus.LocalClientId, item);
			else SyncBus.SendRequest(Id, EncodeItem(SyncListOp.Add, item));
		}

		public void RequestRemove(T item)
		{
			if (SyncBus.IsAuthority) RequestRemoveChecked(SyncBus.LocalClientId, item);
			else SyncBus.SendRequest(Id, EncodeItem(SyncListOp.RemoveItem, item));
		}

		public void RequestClear()
		{
			if (SyncBus.IsAuthority) RequestClearChecked(SyncBus.LocalClientId);
			else SyncBus.SendRequest(Id, new[] { (byte)SyncListOp.Clear });
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
			int index;
			T item;

			switch ((SyncListOp)data[0])
			{
				case SyncListOp.Add:
					item = SyncBytes.Read<T>(data, 1);
					AddLocally(item);
					break;

				case SyncListOp.RemoveAt:
					index = SyncBytes.Read<int>(data, 1);
					if (index >= 0 && index < items.Count)
						RemoveAtLocally(index);
					break;

				case SyncListOp.Set:
					index = SyncBytes.Read<int>(data, 1);
					item = SyncBytes.Read<T>(data, 1 + sizeof(int));

					if (index >= 0 && index < items.Count)
						SetLocally(index, item);
					break;

				case SyncListOp.Clear:
					ClearLocally();
					break;
			}
		}

		internal override void ApplyRequest(ulong sender, byte[] data)
		{
			switch ((SyncListOp)data[0])
			{
				case SyncListOp.Add:
					RequestAddChecked(sender, SyncBytes.Read<T>(data, 1));
					break;

				case SyncListOp.RemoveItem:
					RequestRemoveChecked(sender, SyncBytes.Read<T>(data, 1));
					break;

				case SyncListOp.Clear:
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
			Changed.Invoke(new EventData(SyncListOp.Snapshot));
		}

		internal override void ResetState()
		{
			items.Clear();
			items.AddRange(initial);
			Changed.Invoke(new EventData(SyncListOp.Snapshot));
		}

		// ---- payload encoding ----------------------------------------------------

		private static byte[] EncodeItem(SyncListOp op, T item)
		{
			byte[] data = new byte[1 + SyncBytes.Size<T>()];
			data[0] = (byte)op;
			SyncBytes.Write(data, 1, item);
			return data;
		}

		private static byte[] EncodeIndex(SyncListOp op, int index)
		{
			byte[] data = new byte[1 + sizeof(int)];
			data[0] = (byte)op;
			SyncBytes.Write(data, 1, index);
			return data;
		}

		private static byte[] EncodeIndexedItem(int index, T item)
		{
			byte[] data = new byte[1 + sizeof(int) + SyncBytes.Size<T>()];
			data[0] = (byte)SyncListOp.Set;
			SyncBytes.Write(data, 1, index);
			SyncBytes.Write(data, 1 + sizeof(int), item);
			return data;
		}
	}
}