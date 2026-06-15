// using Unity.Netcode;
//
// namespace Anaglyph.Netcode
// {
// 	// Authority + capability helpers for the custom (named-message) sync layer
// 	// used by SyncVariable<T> and SyncList<T>. These replicate state over
// 	// CustomMessagingManager instead of NetworkObjects.
// 	//
// 	// Named messages are NOT relayed peer-to-peer by NGO (unlike RPCs): at the
// 	// transport layer a non-server client may only send to the server, and a
// 	// "send to all" broadcast is only permitted from the transport-level server.
// 	// That single broadcast-capable peer is:
// 	//
// 	//   * classic client/server : the host    (IsServer)
// 	//   * DA over LAN           : the DAHost  (IsClient && IsServer)  <-- our LAN setup
// 	//   * DA via the CMB service : NOBODY      (no peer is IsServer)
// 	//
// 	// So for this layer, "authority" is the transport server, and every other
// 	// peer routes snapshot-pulls / change-requests to AuthorityClientId
// 	// (== ServerClientId). The DA *session-owner* role is deliberately NOT used:
// 	// a session owner can be a plain client, which cannot broadcast named
// 	// messages. On LAN the DAHost is the server, so IsServer is the right test.
// 	public static class NetSync
// 	{
// 		private static NetworkManager Net => NetworkManager.Singleton;
//
// 		// The peer that owns synced state and is allowed to broadcast it.
// 		// True on the host (classic) or the DAHost (DA over LAN).
// 		public static bool IsAuthority => Net != null && Net.IsServer;
//
// 		// Who non-authority peers send requests / snapshot-pulls to.
// 		public static ulong AuthorityClientId => NetworkManager.ServerClientId;
//
// 		// Generic unmanaged (de)serialization. FastBufferWriter/Reader's typed
// 		// WriteValueSafe/ReadValueSafe overloads split by constraint (ForPrimitives
// 		// / ForEnums / ForStructs / ForNetworkSerializable); a bare "T : unmanaged"
// 		// matches none, so the compiler falls through to the INetworkSerializable
// 		// overload and errors with CS0314. A raw byte copy is unambiguous and
// 		// covers primitives, enums and blittable structs alike — the same memcpy
// 		// NGO's INetworkSerializeByMemcpy path uses. No endianness handling, which
// 		// is fine here: every peer is the same architecture.
// 		public static unsafe int Size<T>() where T : unmanaged => sizeof(T);
//
// 		public static unsafe void Write<T>(FastBufferWriter w, T value) where T : unmanaged
// 		{
// 			w.WriteBytesSafe((byte*)&value, sizeof(T));
// 		}
//
// 		public static unsafe T Read<T>(FastBufferReader r) where T : unmanaged
// 		{
// 			T value = default;
// 			r.ReadBytesSafe((byte*)&value, sizeof(T));
// 			return value;
// 		}
// 	}
// }

