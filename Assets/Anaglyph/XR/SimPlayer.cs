#if UNITY_EDITOR
using System;

namespace Anaglyph.XR
{
	/// <summary>
	/// Which simulated player this editor is. Usually Multiplayer Play Mode's to say, but Burst
	/// forces itself off in the players that starts, and a test of two players a dozen times apart
	/// in speed says little: a test can run the second player in an editor of its own, on a copy
	/// of the project, which takes this argument.
	/// </summary>
	public static class SimPlayer
	{
		public const string ExternalArgument = "-lasertagExternalPlayer";

		public static readonly bool IsExternal = Array.IndexOf(Environment.GetCommandLineArgs(), ExternalArgument) >= 0;

		public static bool IsMainEditor => Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor && !IsExternal;
	}
}
#endif
