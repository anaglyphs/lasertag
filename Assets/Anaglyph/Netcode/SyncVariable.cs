// using System;
// using Unity.Collections;
// using Unity.Netcode;
//
// namespace Anaglyph.Netcode
// {
// 	public class SyncVariable<T> where T : unmanaged
// 	{
// 		private readonly string ch; // data channel, e.g. "coloc.method"
// 		private readonly string req; // snapshot-request channel, e.g. "coloc.method.req"
// 		private readonly string set; // write-request channel, e.g. "coloc.method.set"
// 		private T value;
// 		public event Action<T, T> Changed = delegate { };
//
// 		// Optional authority-side gate for client-submitted writes (see Submit).
// 		// (senderClientId, proposedValue) => accept? Null accepts everything.
// 		public Func<ulong, T, bool> CanSet;
//
// 		private static NetworkManager Net => NetworkManager.Singleton;
// 		private static CustomMessagingManager Msg => Net.CustomMessagingManager;
//
// 		public SyncVariable(string channel, T initial = default)
// 		{
// 			ch = channel;
// 			req = channel + ".req";
// 			set = channel + ".set";
// 			value = initial;
// 		}
//
// 		public T Value
// 		{
// 			get => value;
// 			set
// 			{
// 				if (!NetSync.IsAuthority) throw new InvalidOperationException($"{ch}: authority-only write");
// 				Apply(value);
// 				Send(ch, value, null); // broadcast to all
// 			}
// 		}
//
// 		// Set from any peer. The authority writes directly; everyone else sends a
// 		// request the authority validates (CanSet) and applies — which broadcasts
// 		// it back. Last writer wins, serialized on the authority's thread.
// 		public void Submit(T newValue)
// 		{
// 			if (NetSync.IsAuthority)
// 			{
// 				Value = newValue;
// 				return;
// 			}
//
// 			using FastBufferWriter w = new(NetSync.Size<T>(), Allocator.Temp);
// 			NetSync.Write(w, newValue);
// 			Msg.SendNamedMessage(set, NetSync.AuthorityClientId, w, NetworkDelivery.Reliable);
// 		}
//
// 		public void Register()
// 		{
// 			Msg.RegisterNamedMessageHandler(ch, OnData);
// 			if (NetSync.IsAuthority)
// 			{
// 				Msg.RegisterNamedMessageHandler(req, OnRequest); // authority answers pulls
// 				Msg.RegisterNamedMessageHandler(set, OnSetRequest); // ...and write-requests
// 			}
// 			else
// 			{
// 				RequestSnapshot(); // non-authority pulls now
// 			}
// 		}
//
// 		public void Unregister()
// 		{
// 			if (Net == null || Msg == null) return;
// 			Msg.UnregisterNamedMessageHandler(ch);
// 			if (NetSync.IsAuthority)
// 			{
// 				Msg.UnregisterNamedMessageHandler(req);
// 				Msg.UnregisterNamedMessageHandler(set);
// 			}
// 		}
//
// 		private void RequestSnapshot()
// 		{
// 			using FastBufferWriter w = new(0, Allocator.Temp);
// 			Msg.SendNamedMessage(req, NetSync.AuthorityClientId, w, NetworkDelivery.Reliable);
// 		}
//
// 		private void OnRequest(ulong sender, FastBufferReader _)
// 		{
// 			Send(ch, value, sender);
// 		}
//
// 		private void OnSetRequest(ulong sender, FastBufferReader r)
// 		{
// 			T proposed = NetSync.Read<T>(r);
// 			if (CanSet == null || CanSet(sender, proposed))
// 				Value = proposed; // authority write → broadcast to all
// 		}
//
// 		private void OnData(ulong _, FastBufferReader r)
// 		{
// 			T v = NetSync.Read<T>(r);
// 			if (!NetSync.IsAuthority) Apply(v);
// 		}
//
// 		private void Apply(T v)
// 		{
// 			T oldValue = value;
// 			value = v;
// 			Changed.Invoke(oldValue, value);
// 		}
//
// 		private void Send(string channel, T v, ulong? target)
// 		{
// 			using FastBufferWriter w = new(NetSync.Size<T>(), Allocator.Temp);
// 			NetSync.Write(w, v);
// 			if (target.HasValue) Msg.SendNamedMessage(channel, target.Value, w, NetworkDelivery.ReliableSequenced);
// 			else Msg.SendNamedMessageToAll(channel, w, NetworkDelivery.ReliableSequenced);
// 		}
// 	}
// }

