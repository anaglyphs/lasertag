using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>
	/// UI Toolkit counterpart to <see cref="ButtonActOnPress"/>. Clicks on
	/// pointer down instead of waiting for a press and release over the same
	/// element, which in XR both feels snappier and stops presses from being
	/// swallowed when the ray drifts off the button before release.
	/// </summary>
	public class PressClickable : Clickable
	{
		public PressClickable() : base((Action)null) { }

		public PressClickable(Action handler) : base(handler) { }

		protected override void ProcessDownEvent(
			EventBase evt, Vector2 localPosition, int pointerId)
		{
			// Captures the pointer and sets the :active pseudo state. It does not
			// click — Clickable only clicks on down when it is set up to repeat.
			base.ProcessDownEvent(evt, localPosition, pointerId);

			if (target.enabledInHierarchy)
				Invoke(evt);
		}

		// The click already happened on press, so release only has to undo what
		// the press set up. Cancel does exactly that: it releases the pointer
		// capture and clears the :active pseudo state without invoking.
		protected override void ProcessUpEvent(
			EventBase evt, Vector2 localPosition, int pointerId)
			=> ProcessCancelEvent(evt, pointerId);
	}

	public static class ActOnPress
	{
		/// <summary>
		/// Switches every <see cref="Button"/> in the tree over to clicking on
		/// press. Call it before wiring any <see cref="Button.clicked"/>
		/// handlers — swapping the manipulator drops handlers already attached
		/// to the one it replaces.
		/// </summary>
		public static void MakeButtonsActOnPress(this VisualElement root)
			=> root.Query<Button>().ForEach(button => button.MakeActOnPress());

		/// <inheritdoc cref="MakeButtonsActOnPress"/>
		public static void MakeActOnPress(this Button button)
		{
			if (button.clickable is PressClickable)
				return;

			button.clickable = new PressClickable();
		}
	}
}
