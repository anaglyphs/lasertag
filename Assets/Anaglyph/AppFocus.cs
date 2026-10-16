using UnityEngine;

namespace Anaglyph
{
	/// <summary>
	/// The one place that decides what application focus means. On a headset, losing
	/// focus means the player took it off or opened the system menu: tracking, depth
	/// and colocation all stop, and anything that trusted them must reset. In the
	/// editor, focus only says which window is frontmost, and tabbing between editor
	/// instances must not count as any of that. Every focus check routes through here
	/// so that difference lives in exactly one spot.
	/// </summary>
	public static class AppFocus
	{
		/// <summary>Whether focus changes carry meaning at all on this platform.</summary>
		public static bool Matters => !Application.isEditor;

		/// <summary>Focused for all practical purposes: always true in the editor.</summary>
		public static bool IsFocused => !Matters || Application.isFocused;

		/// <summary>
		/// What an <c>OnApplicationFocus(bool)</c> argument means here: the value itself
		/// on a headset, always focused in the editor.
		/// </summary>
		public static bool Resolve(bool hasFocus) => !Matters || hasFocus;

		/// <summary>True when an <c>OnApplicationFocus(bool)</c> callback is a real loss of focus.</summary>
		public static bool IsLoss(bool hasFocus) => !Resolve(hasFocus);
	}
}
