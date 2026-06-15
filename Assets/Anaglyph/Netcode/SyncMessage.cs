// using System;
// using Unity.Collections;
// using Unity.Netcode;
//
// namespace Anaglyph.Netcode
// {
// 	// Fire-and-forget broadcast event over CustomMessagingManager — the RPC
// 	// (SendTo.Everyone) equivalent for the NetworkObject-free sync layer. Any peer
// 	// may Invoke: the authority broadcasts directly; a non-authority relays through
// 	// the authority, which re-broadcasts to all (including the original sender), so
// 	// every peer raises Received exactly once. CanInvoke gates client-submitted
// 	// invokes on the authority. See SyncVariable<T> for the value sibling.
// 	//
// 	// This is the payload-less variant; SyncMessage<T> carries data.
// 	public class SyncMessage
// 	{
// 		private readonly string ch; // broadcast channel
// 		private readonly string inv; // client→authority invoke-request channel
// 		public event Action Received = delegate { };
//
// 		// Optional authority-side gate for client-submitted invokes. senderClientId
// 		// => accept? Null accepts everything.
// 		public Func<ulong, bool> CanInvoke;
//
// 		private static NetworkManager Net => NetworkManager.Singleton;
// 		private static CustomMessagingManager Msg => Net.CustomMessagingManager;
//
// 		public SyncMessage(string channel)
// 		{
// 			ch = channel;
// 			inv = channel + ".inv";
// 		}
//
// 		public void Register()
// 		{
// 			Msg.RegisterNamedMessageHandler(ch, OnBroadcast);
// 			if (NetSync.IsAuthority) Msg.RegisterNamedMessageHandler(inv, OnInvokeRequest);
// 		}
//
// 		public void Unregister()
// 		{
// 			if (Net == null) return;
// 			Msg.UnregisterNamedMessageHandler(ch);
// 			if (NetSync.IsAuthority) Msg.UnregisterNamedMessageHandler(inv);
// 		}
//
// 		// Raise Received on every peer. Authority broadcasts; others relay.
// 		public void Invoke()
// 		{
// 			if (NetSync.IsAuthority)
// 			{
// 				Broadcast();
// 				return;
// 			}
//
// 			using FastBufferWriter w = new(0, Allocator.Temp);
// 			Msg.SendNamedMessage(inv, NetSync.AuthorityClientId, w, NetworkDelivery.Reliable);
// 		}
//
// 		private void Broadcast()
// 		{
// 			using FastBufferWriter w = new(0, Allocator.Temp);
// 			// SendNamedMessageToAll invokes the authority's own handler locally too,
// 			// so Received fires once on every peer including the sender.
// 			Msg.SendNamedMessageToAll(ch, w, NetworkDelivery.ReliableSequenced);
// 		}
//
// 		private void OnInvokeRequest(ulong sender, FastBufferReader _)
// 		{
// 			if (CanInvoke == null || CanInvoke(sender)) Broadcast();
// 		}
//
// 		private void OnBroadcast(ulong _, FastBufferReader __)
// 		{
// 			Received.Invoke();
// 		}
// 	}
//
// 	// Broadcast event carrying an unmanaged payload. Same relay semantics as the
// 	// payload-less SyncMessage.
// 	public class SyncMessage<T> where T : unmanaged
// 	{
// 		private readonly string ch;
// 		private readonly string inv;
// 		public event Action<T> Received = delegate { };
//
// 		public Func<ulong, T, bool> CanInvoke;
//
// 		private static NetworkManager Net => NetworkManager.Singleton;
// 		private static CustomMessagingManager Msg => Net.CustomMessagingManager;
//
// 		public SyncMessage(string channel)
// 		{
// 			ch = channel;
// 			inv = channel + ".inv";
// 		}
//
// 		public void Register()
// 		{
// 			Msg.RegisterNamedMessageHandler(ch, OnBroadcast);
// 			if (NetSync.IsAuthority) Msg.RegisterNamedMessageHandler(inv, OnInvokeRequest);
// 		}
//
// 		public void Unregister()
// 		{
// 			if (Net == null) return;
// 			Msg.UnregisterNamedMessageHandler(ch);
// 			if (NetSync.IsAuthority) Msg.UnregisterNamedMessageHandler(inv);
// 		}
//
// 		public void Invoke(T payload)
// 		{
// 			if (NetSync.IsAuthority)
// 			{
// 				Broadcast(payload);
// 				return;
// 			}
//
// 			using FastBufferWriter w = new(NetSync.Size<T>(), Allocator.Temp);
// 			NetSync.Write(w, payload);
// 			Msg.SendNamedMessage(inv, NetSync.AuthorityClientId, w, NetworkDelivery.Reliable);
// 		}
//
// 		private void Broadcast(T payload)
// 		{
// 			using FastBufferWriter w = new(NetSync.Size<T>(), Allocator.Temp);
// 			NetSync.Write(w, payload);
// 			Msg.SendNamedMessageToAll(ch, w, NetworkDelivery.ReliableSequenced);
// 		}
//
// 		private void OnInvokeRequest(ulong sender, FastBufferReader r)
// 		{
// 			T payload = NetSync.Read<T>(r);
// 			if (CanInvoke == null || CanInvoke(sender, payload)) Broadcast(payload);
// 		}
//
// 		private void OnBroadcast(ulong _, FastBufferReader r)
// 		{
// 			Received.Invoke(NetSync.Read<T>(r));
// 		}
// 	}
// }

