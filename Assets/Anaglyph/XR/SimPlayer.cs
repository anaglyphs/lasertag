#if UNITY_EDITOR
using ParrelSync;

namespace Anaglyph.XR
{
	/// <summary>
	/// Which simulated player this editor is: the main one, or a joiner, which is a ParrelSync clone
	/// of the project or one of Multiplayer Play Mode's virtual players.
	/// </summary>
	public static class SimPlayer
	{
		public static bool IsMainEditor => Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor && !ClonesManager.IsClone();
	}
}
#endif