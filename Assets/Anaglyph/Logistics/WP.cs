using Unity.Netcode;

namespace Anaglyph.Lasertag
{
    public static class WritePerm
    {
		public static NetworkVariableWritePermission Owner => NetworkVariableWritePermission.Owner;
	}

	public static class ReadPerm
	{
		public static NetworkVariableReadPermission All => NetworkVariableReadPermission.Everyone;
	}
}
