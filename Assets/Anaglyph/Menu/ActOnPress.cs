using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>
	/// UI Toolkit counterpart to <see cref="ButtonActOnPress"/>. Clicks on
	/// pointer down, except destructive elements which require press and release.
	/// </summary>
	public class PressClickable : Clickable
	{
		public PressClickable() : base((Action)null) { }

		public PressClickable(Action handler) : base(handler) { }

		private bool actOnRelease;

		protected override void ProcessDownEvent(
			EventBase evt, Vector2 localPosition, int pointerId)
		{
			// Captures the pointer and sets the :active pseudo state. It does not
			// click — Clickable only clicks on down when it is set up to repeat.
			actOnRelease = target.ClassListContains("destructive");
			base.ProcessDownEvent(evt, localPosition, pointerId);

			if (!actOnRelease && target.enabledInHierarchy)
				Invoke(evt);

			if (active && !target.HasPointerCapture(pointerId))
				ProcessCancelEvent(evt, pointerId);
		}

		protected override void ProcessUpEvent(
			EventBase evt, Vector2 localPosition, int pointerId)
		{
			if (actOnRelease)
				base.ProcessUpEvent(evt, localPosition, pointerId);
			else
				ProcessCancelEvent(evt, pointerId);
		}
	}

	public static class ActOnPress
	{
		/// <summary>
		/// Makes buttons, toggles, and radio buttons act on press unless destructive.
		/// Call it before wiring any <see cref="Button.clicked"/>
		/// handlers — swapping the manipulator drops handlers already attached
		/// to the one it replaces.
		/// </summary>
		public static void MakeButtonsActOnPress(this VisualElement root)
		{
			root.Query<Button>().ForEach(button => button.MakeActOnPress());
			root.Query<Toggle>().ForEach(field => field.MakeActOnPress());
			root.Query<RadioButton>().ForEach(field => field.MakeActOnPress());
		}

		public static void MakeActOnPress(this BaseBoolField field)
			=> field.RegisterCallback<PointerDownEvent>(OnBoolFieldPressed, TrickleDown.TrickleDown);

		private static void OnBoolFieldPressed(PointerDownEvent evt)
		{
			var field = (BaseBoolField)evt.currentTarget;
			if (evt.button != 0 || !field.enabledInHierarchy || field.ClassListContains("destructive"))
				return;
			if (!field.toggleOnLabelClick && field.labelElement.worldBound.Contains(evt.position))
				return;

			evt.StopImmediatePropagation();
			field.Focus();
			using var submit = NavigationSubmitEvent.GetPooled();
			submit.target = field;
			field.SendEvent(submit);
		}

		/// <inheritdoc cref="MakeButtonsActOnPress"/>
		public static void MakeActOnPress(this Button button)
		{
			if (button.clickable is PressClickable)
				return;

			button.clickable = new PressClickable();
		}
	}
}
