using System.Net;
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

		public static string GetLocalIPv4()
		{
			var addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
			foreach (var address in addresses)
			{
				if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
				{
					return address.ToString();
				}
			}

			return null;
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
