using Unity.Netcode;

namespace Anaglyph.Netcode
{
	/// <summary>
	/// A clock that reads the same on every peer
	///
	/// NetworkManager.ServerTime can't do this: it is the true server clock on the host, but is
	/// deliberately lagged by half-RTT plus a buffer on clients so transform interpolation has
	/// data to work with. LocalTime leads the true clock by LocalBufferSec on every peer, so
	/// subtracting that buffer lands everyone on the same timeline.
	/// </summary>
	public static class SharedNetworkTime
	{
		public static double Time
		{
			get
			{
				NetworkManager manager = NetworkManager.Singleton;

				if (manager == null || !manager.IsListening)
					return 0;

				return manager.LocalTime.Time - manager.NetworkTimeSystem.LocalBufferSec;
			}
		}

		public static float TimeAsFloat => (float)Time;
	}
}
