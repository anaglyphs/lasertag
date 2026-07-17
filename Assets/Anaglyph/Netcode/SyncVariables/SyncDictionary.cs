using System;
using System.Collections;
using System.Collections.Generic;

namespace Anaglyph.Netcode
{
	public enum SyncDictionaryOp : byte
	{
		Set = 0,
		Remove = 1,
		Clear = 2,
		Snapshot = 3
	}

	// A replicated dictionary with no NetworkObject of its own — the NetworkDictionary
	// NGO never shipped, for plain MonoBehaviour singletons. Routed through SyncBus
	// under the hash of its string name.
	//
	// The authority mutates (Set / Remove / Clear) and every peer mirrors the change
	// in the same order; other peers use RequestSet / RequestRemove / RequestClear,
	// which the authority validates. Changed fires after any mutation from any
	// source; re-read the dictionary rather than trusting a delta.
	public class SyncDictionary<TKey, TValue> : SyncEndpoint, IReadOnlyDictionary<TKey, TValue>
		where TKey : unmanaged
		where TValue : unmanaged
	{
		private readonly Dictionary<TKey, TValue> entries = new();

		public struct EventData
		{
			public SyncDictionaryOp op;
			public TKey eventKey;
			public TValue eventValue;

			public EventData(SyncDictionaryOp op)
			{
				this.op = op;
				eventKey = default;
				eventValue = default;
			}

			public EventData(SyncDictionaryOp op, TKey eventKey, TValue eventValue)
			{
				this.op = op;
				this.eventKey = eventKey;
				this.eventValue = eventValue;
			}
		}

		public event Action<EventData> Changed = delegate { };

		// Authority-side gates for requests: (sender, key, value) => accept? Null
		// accepts everything. Also consulted for the authority's own Request calls.
		public Func<ulong, TKey, TValue, bool> ValidateSet;
		public Func<ulong, TKey, bool> ValidateRemove;
		public Func<ulong, bool> ValidateClear;

		public SyncDictionary(string name) : base(name)
		{
		}

		// ---- read access -------------------------------------------------------

		public int Count => entries.Count;
		public TValue this[TKey key] => entries[key];

		public bool ContainsKey(TKey key)
		{
			return entries.ContainsKey(key);
		}

		public bool TryGetValue(TKey key, out TValue value)
		{
			return entries.TryGetValue(key, out value);
		}

		public IEnumerable<TKey> Keys => entries.Keys;
		public IEnumerable<TValue> Values => entries.Values;

		public Dictionary<TKey, TValue>.Enumerator GetEnumerator()
		{
			return entries.GetEnumerator();
		}

		IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
		{
			return entries.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return entries.GetEnumerator();
		}

		// ---- authority-only mutation -------------------------------------------

		public void Set(TKey key, TValue value)
		{
			RequireAuthority(Name);

			SetLocally(key, value);

			SyncBus.SendBroadcast(Id, EncodeEntry(SyncDictionaryOp.Set, key, value));
		}

		public void Remove(TKey key)
		{
			RequireAuthority(Name);

			if (!RemoveLocally(key)) return;

			SyncBus.SendBroadcast(Id, EncodeKey(SyncDictionaryOp.Remove, key));
		}

		public void Clear()
		{
			RequireAuthority(Name);
			ClearLocally();

			SyncBus.SendBroadcast(Id, new[] { (byte)SyncDictionaryOp.Clear });
		}

		// ---- ungated local mutation -------------------------------------

		private void SetLocally(TKey key, TValue value)
		{
			// bool isAdded = !entries.ContainsKey(key);
			entries[key] = value;

			Changed.Invoke(new EventData(SyncDictionaryOp.Set, key, value));
		}

		private bool RemoveLocally(TKey key)
		{
			if (!entries.Remove(key, out TValue removedValue)) return false;

			Changed.Invoke(new EventData(SyncDictionaryOp.Remove, key, removedValue));

			return true;
		}

		// Anchor values are still present in Changed event!
		private void ClearLocally()
		{
			Changed.Invoke(new EventData(SyncDictionaryOp.Clear));
			entries.Clear();
		}

		// ---- any-peer requests ---------------------------------------------------

		public void RequestSet(TKey key, TValue value)
		{
			if (SyncBus.IsAuthority) RequestSetChecked(SyncBus.LocalClientId, key, value);
			else SyncBus.SendRequest(Id, EncodeEntry(SyncDictionaryOp.Set, key, value));
		}

		public void RequestRemove(TKey key)
		{
			if (SyncBus.IsAuthority) RequestRemoveChecked(SyncBus.LocalClientId, key);
			else SyncBus.SendRequest(Id, EncodeKey(SyncDictionaryOp.Remove, key));
		}

		public void RequestClear()
		{
			if (SyncBus.IsAuthority) RequestClearChecked(SyncBus.LocalClientId);
			else SyncBus.SendRequest(Id, new[] { (byte)SyncDictionaryOp.Clear });
		}

		private void RequestSetChecked(ulong sender, TKey key, TValue value)
		{
			if (ValidateSet == null || ValidateSet(sender, key, value)) Set(key, value);
		}

		private void RequestRemoveChecked(ulong sender, TKey key)
		{
			if (ValidateRemove == null || ValidateRemove(sender, key)) Remove(key);
		}

		private void RequestClearChecked(ulong sender)
		{
			if (ValidateClear == null || ValidateClear(sender)) Clear();
		}

		// ---- bus plumbing ------------------------------------------------------

		internal override void ApplyBroadcast(byte[] data)
		{
			TKey key;
			TValue value;

			switch ((SyncDictionaryOp)data[0])
			{
				case SyncDictionaryOp.Set:
					key = SyncBytes.Read<TKey>(data, 1);
					value = SyncBytes.Read<TValue>(data, 1 + SyncBytes.Size<TKey>());
					SetLocally(key, value);
					break;

				case SyncDictionaryOp.Remove:
					key = SyncBytes.Read<TKey>(data, 1);
					RemoveLocally(key);
					break;

				case SyncDictionaryOp.Clear:
					ClearLocally();
					break;
			}
		}

		internal override void ApplyRequest(ulong sender, byte[] data)
		{
			switch ((SyncDictionaryOp)data[0])
			{
				case SyncDictionaryOp.Set:
					RequestSetChecked(sender, SyncBytes.Read<TKey>(data, 1),
						SyncBytes.Read<TValue>(data, 1 + SyncBytes.Size<TKey>()));
					break;

				case SyncDictionaryOp.Remove:
					RequestRemoveChecked(sender, SyncBytes.Read<TKey>(data, 1));
					break;

				case SyncDictionaryOp.Clear:
					RequestClearChecked(sender);
					break;
			}
		}

		internal override byte[] SerializeSnapshot()
		{
			int keySize = SyncBytes.Size<TKey>();
			int valueSize = SyncBytes.Size<TValue>();
			byte[] data = new byte[sizeof(int) + entries.Count * (keySize + valueSize)];

			SyncBytes.Write(data, 0, entries.Count);
			int offset = sizeof(int);

			foreach (KeyValuePair<TKey, TValue> entry in entries)
			{
				SyncBytes.Write(data, offset, entry.Key);
				offset += keySize;
				SyncBytes.Write(data, offset, entry.Value);
				offset += valueSize;
			}

			return data;
		}

		private bool snapshotApplied;

		internal override void ApplySnapshot(byte[] data)
		{
			int keySize = SyncBytes.Size<TKey>();
			int valueSize = SyncBytes.Size<TValue>();
			int count = SyncBytes.Read<int>(data, 0);
			int offset = sizeof(int);

			entries.Clear();
			for (int i = 0; i < count; i++)
			{
				TKey key = SyncBytes.Read<TKey>(data, offset);
				offset += keySize;
				entries[key] = SyncBytes.Read<TValue>(data, offset);
				offset += valueSize;
			}

			snapshotApplied = true;
		}

		internal override void FlushSnapshotEvents()
		{
			if (!snapshotApplied) return;
			snapshotApplied = false;
			Changed.Invoke(new EventData(SyncDictionaryOp.Snapshot));
		}

		internal override void ResetState()
		{
			ClearLocally();
		}

		// ---- payload encoding ----------------------------------------------------

		private static byte[] EncodeEntry(SyncDictionaryOp op, TKey key, TValue value)
		{
			byte[] data = new byte[1 + SyncBytes.Size<TKey>() + SyncBytes.Size<TValue>()];
			data[0] = (byte)op;
			SyncBytes.Write(data, 1, key);
			SyncBytes.Write(data, 1 + SyncBytes.Size<TKey>(), value);
			return data;
		}

		private static byte[] EncodeKey(SyncDictionaryOp op, TKey key)
		{
			byte[] data = new byte[1 + SyncBytes.Size<TKey>()];
			data[0] = (byte)op;
			SyncBytes.Write(data, 1, key);
			return data;
		}
	}
}