using System;
using UnityEngine;

namespace Anaglyph.Debugging
{
	public static class AnaglyphDebugging
	{
		private static bool _debugMode = false;
		public static Action<bool> DebugModeChanged = delegate { };

		public static bool DebugMode
		{
			get => _debugMode;
			set
			{
				if (_debugMode != value)
				{
					_debugMode = value;
					DebugModeChanged.Invoke(_debugMode);
				}
			}
		}

		public static void SetDebugMode(bool on)
		{
			DebugMode = on;
		}

		// every play starts with it off, also in an editor that keeps statics between plays
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void StartOff() => _debugMode = false;
	}
}