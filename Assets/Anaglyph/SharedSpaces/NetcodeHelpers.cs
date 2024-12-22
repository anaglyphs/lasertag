using Unity.Netcode;

namespace SharedSpaces
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
}
