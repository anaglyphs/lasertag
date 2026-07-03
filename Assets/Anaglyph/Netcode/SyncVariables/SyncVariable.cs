using System;
using System.Collections.Generic;

namespace Anaglyph.Netcode
{
	// A single replicated value with no NetworkObject of its own — the drop-in for a
	// NetworkVariable<T> on a plain MonoBehaviour singleton. Routed through SyncBus
	// under the hash of its string name; see SyncEndpoint for the lifecycle contract.
	//
	// The authority is the single writer (Value setter). Other peers read, and may
	// Request a value that the authority validates (Validate) and applies. Late
	// joiners get the current value through the bus's snapshot handshake. Off-network
	// it is just a local value.
	public class SyncVariable<T> : SyncEndpoint where T : unmanaged
	{
		private readonly T initial;
		private T current;

		// (oldValue, newValue) after any applied change — local write, remote update,
		// snapshot, or reset. Only fires on an actual change.
		public event Action<T, T> Changed = delegate { };

		// Authority-side gate for requested writes: (sender, proposed) => accept?
		// Null accepts everything. Also consulted for the authority's own Request.
		public Func<ulong, T, bool> Validate;

		public SyncVariable(string name, T initialValue = default) : base(name)
		{
			initial = initialValue;
			current = initialValue;
		}

		public T Value
		{
			get => current;
			set
			{
				RequireAuthority(Name);
				if (!Apply(value)) return;
				SyncBus.SendBroadcast(Id, SyncBytes.Of(value));
			}
		}

		// Set from any peer: the authority applies directly, everyone else routes the
		// value through the authority. Never applied locally before the authority
		// confirms, so there is nothing to roll back.
		public void Request(T newValue)
		{
			if (SyncBus.IsAuthority) ApplyRequestChecked(SyncBus.LocalClientId, newValue);
			else SyncBus.SendRequest(Id, SyncBytes.Of(newValue));
		}

		private bool Apply(T value)
		{
			if (EqualityComparer<T>.Default.Equals(current, value)) return false;
			T old = current;
			current = value;
			Changed.Invoke(old, value);
			return true;
		}

		private void ApplyRequestChecked(ulong sender, T proposed)
		{
			if (Validate == null || Validate(sender, proposed))
				Value = proposed; // authority write: applies + broadcasts
		}

		// ---- bus plumbing ------------------------------------------------------

		private bool snapshotChanged;
		private T snapshotOld;

		internal override void ApplyBroadcast(byte[] data)
		{
			Apply(SyncBytes.Read<T>(data, 0));
		}

		internal override void ApplyRequest(ulong sender, byte[] data)
		{
			ApplyRequestChecked(sender, SyncBytes.Read<T>(data, 0));
		}

		internal override byte[] SerializeSnapshot()
		{
			return SyncBytes.Of(current);
		}

		internal override void ApplySnapshot(byte[] data)
		{
			T incoming = SyncBytes.Read<T>(data, 0);
			if (EqualityComparer<T>.Default.Equals(current, incoming)) return;

			snapshotOld = current;
			snapshotChanged = true;
			current = incoming;
		}

		internal override void FlushSnapshotEvents()
		{
			if (!snapshotChanged) return;
			snapshotChanged = false;
			Changed.Invoke(snapshotOld, current);
		}

		internal override void ResetState()
		{
			Apply(initial);
		}
	}
}
