using System;
using UnityEngine;

namespace Anaglyph
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

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void InitializeOnLoad()
		{
			DebugMode = Debug.isDebugBuild;
		}
	}
}