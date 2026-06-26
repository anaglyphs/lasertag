using System;
using System.Collections.Generic;

namespace Anaglyph.Netcode
{
	// A single replicated value with no NetworkObject of its own — the drop-in for a
	// NetworkVariable<T> on a plain MonoBehaviour singleton. Routed through SyncBus
	// under a unique integer id; see SyncList<T> for the collection sibling.
	//
	// The authority is the single writer (Value setter). Other peers read, and may
	// Submit a proposed value the authority validates via CanSet. Late joiners get
	// the current value through SyncBus's snapshot handshake. Off-network it is just
	// a local value.
	//
	// Construct one as a field, then Register() once the session is up and
	// Unregister() when it drops — e.g. from a NetcodeManagement.StateChanged
	// handler, NOT in Awake/field init (the bus may not be ready, and there's no
	// deterministic teardown for a plain C# object otherwise).
	public class SyncVariable<T> : ISyncEndpoint where T : unmanaged
	{
		private readonly int id;
		private T current;

		// (oldValue, newValue) after any change — local write or remote update —
		// mirroring NetworkVariable<T>.OnValueChanged. Only fires on an actual change.
		public event Action<T, T> OnValueChanged = delegate { };

		// Authority-side gate for Submit()ed writes: (sender, proposed) => accept?
		// Null accepts everything.
		public Func<ulong, T, bool> CanSet;

		public SyncVariable(int id, T initialValue = default)
		{
			this.id = id;
			current = initialValue;
		}

		public void Register()
		{
			SyncBus.Register(id, this);
		}

		public void Unregister()
		{
			SyncBus.Unregister(id, this);
		}

		public T Value
		{
			get => current;
			set
			{
				if (!SyncBus.IsAuthority)
					throw new InvalidOperationException(
						$"SyncVariable {id}: only the authority may set Value. Use Submit() from other peers.");

				if (!Apply(value)) return;
				SyncBus.Broadcast(id, SyncBytes.Of(value));
			}
		}

		// Set from any peer. The authority writes directly; everyone else routes the
		// value through the authority, which validates (CanSet) and broadcasts.
		public void Submit(T newValue)
		{
			if (SyncBus.IsAuthority) Value = newValue;
			else SyncBus.Submit(id, SyncBytes.Of(newValue));
		}

		private bool Apply(T v)
		{
			if (EqualityComparer<T>.Default.Equals(current, v)) return false;
			T old = current;
			current = v;
			OnValueChanged.Invoke(old, v);
			return true;
		}

		// ---- ISyncEndpoint ---------------------------------------------------

		void ISyncEndpoint.ReceiveBroadcast(byte[] data)
		{
			Apply(SyncBytes.Read<T>(data, 0));
		}

		byte[] ISyncEndpoint.SerializeSnapshot()
		{
			return SyncBytes.Of(current);
		}

		void ISyncEndpoint.ReceiveSubmit(ulong sender, byte[] data)
		{
			T proposed = SyncBytes.Read<T>(data, 0);
			if (CanSet == null || CanSet(sender, proposed))
				Value = proposed; // authority write → applies + broadcasts
		}
	}
}