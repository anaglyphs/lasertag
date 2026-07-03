using System;
using System.Collections;
using System.Collections.Generic;

namespace Anaglyph.Netcode
{
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
		private enum Op : byte
		{
			Set = 0,
			Remove = 1,
			Clear = 2
		}

		private readonly Dictionary<TKey, TValue> entries = new();

		public event Action Changed = delegate { };

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
		public bool ContainsKey(TKey key) => entries.ContainsKey(key);
		public bool TryGetValue(TKey key, out TValue value) => entries.TryGetValue(key, out value);
		public IEnumerable<TKey> Keys => entries.Keys;
		public IEnumerable<TValue> Values => entries.Values;

		public Dictionary<TKey, TValue>.Enumerator GetEnumerator() => entries.GetEnumerator();

		IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
			entries.GetEnumerator();

		IEnumerator IEnumerable.GetEnumerator() => entries.GetEnumerator();

		// ---- authority-only mutation -------------------------------------------

		public void Set(TKey key, TValue value)
		{
			RequireAuthority(Name);
			entries[key] = value;
			Changed.Invoke();
			SyncBus.SendBroadcast(Id, EncodeEntry(Op.Set, key, value));
		}

		public void Remove(TKey key)
		{
			RequireAuthority(Name);
			if (!entries.Remove(key)) return;
			Changed.Invoke();
			SyncBus.SendBroadcast(Id, EncodeKey(Op.Remove, key));
		}

		public void Clear()
		{
			RequireAuthority(Name);
			entries.Clear();
			Changed.Invoke();
			SyncBus.SendBroadcast(Id, new[] { (byte)Op.Clear });
		}

		// ---- any-peer requests ---------------------------------------------------

		public void RequestSet(TKey key, TValue value)
		{
			if (SyncBus.IsAuthority) RequestSetChecked(SyncBus.LocalClientId, key, value);
			else SyncBus.SendRequest(Id, EncodeEntry(Op.Set, key, value));
		}

		public void RequestRemove(TKey key)
		{
			if (SyncBus.IsAuthority) RequestRemoveChecked(SyncBus.LocalClientId, key);
			else SyncBus.SendRequest(Id, EncodeKey(Op.Remove, key));
		}

		public void RequestClear()
		{
			if (SyncBus.IsAuthority) RequestClearChecked(SyncBus.LocalClientId);
			else SyncBus.SendRequest(Id, new[] { (byte)Op.Clear });
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
			switch ((Op)data[0])
			{
				case Op.Set:
					entries[SyncBytes.Read<TKey>(data, 1)] =
						SyncBytes.Read<TValue>(data, 1 + SyncBytes.Size<TKey>());
					break;

				case Op.Remove:
					entries.Remove(SyncBytes.Read<TKey>(data, 1));
					break;

				case Op.Clear:
					entries.Clear();
					break;
			}

			Changed.Invoke();
		}

		internal override void ApplyRequest(ulong sender, byte[] data)
		{
			switch ((Op)data[0])
			{
				case Op.Set:
					RequestSetChecked(sender, SyncBytes.Read<TKey>(data, 1),
						SyncBytes.Read<TValue>(data, 1 + SyncBytes.Size<TKey>()));
					break;

				case Op.Remove:
					RequestRemoveChecked(sender, SyncBytes.Read<TKey>(data, 1));
					break;

				case Op.Clear:
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
			Changed.Invoke();
		}

		internal override void ResetState()
		{
			entries.Clear();
			Changed.Invoke();
		}

		// ---- payload encoding ----------------------------------------------------

		private static byte[] EncodeEntry(Op op, TKey key, TValue value)
		{
			byte[] data = new byte[1 + SyncBytes.Size<TKey>() + SyncBytes.Size<TValue>()];
			data[0] = (byte)op;
			SyncBytes.Write(data, 1, key);
			SyncBytes.Write(data, 1 + SyncBytes.Size<TKey>(), value);
			return data;
		}

		private static byte[] EncodeKey(Op op, TKey key)
		{
			byte[] data = new byte[1 + SyncBytes.Size<TKey>()];
			data[0] = (byte)op;
			SyncBytes.Write(data, 1, key);
			return data;
		}
	}
}
