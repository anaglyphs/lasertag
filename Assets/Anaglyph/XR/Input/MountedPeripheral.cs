using System;
using UnityEngine;

namespace Anaglyph.XR.Input
{
	/// <summary>
	/// Which peripheral the player currently has a controller mounted in, if any.
	/// Peripheral integrations set this; everything else only reacts to it.
	/// </summary>
	public static class MountedPeripheral
	{
		public static HandPeripheral Current { get; private set; }
		public static event Action<HandPeripheral> Changed;

		public static bool IsMountedOn(Handedness hand) =>
			Current != null && Current.MountedHand == hand;

		public static void Set(HandPeripheral peripheral)
		{
			if (Current == peripheral)
				return;

			Current = peripheral;
			Changed?.Invoke(Current);
		}

		// statics outlive play sessions when domain reloading is off
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetState()
		{
			Current = null;
			Changed = null;
		}
	}
}
