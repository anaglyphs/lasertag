using Unity.Netcode;

namespace Anaglyph.Netcode
{
    public static class NetcodeHelpers
    {
        public static bool ThisClientConnected(ConnectionEventData data) 
        {
            return data.EventType == ConnectionEvent.ClientConnected && 
                data.ClientId == NetworkManager.Singleton.LocalClientId;
        }

		public static bool ThisClientDisconnected(ConnectionEventData data)
		{
			return data.EventType == ConnectionEvent.ClientDisconnected &&
				data.ClientId == NetworkManager.Singleton.LocalClientId;
		}
	}

	public static class WritePerm
	{
		public static NetworkVariableWritePermission Owner => NetworkVariableWritePermission.Owner;
	}

	public static class ReadPerm
	{
		public static NetworkVariableReadPermission Everyone => NetworkVariableReadPermission.Everyone;
	}
}
