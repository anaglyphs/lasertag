using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using PointerType = UnityEngine.UIElements.PointerType;

namespace Anaglyph.Menu
{
	/// <summary>
	/// Touch only: dragging the target left grows an action over it from its right edge. Past half the
	/// target's width the action snaps to the full width, and letting go there runs it. Letting go sooner
	/// leaves the action open at its resting width until a press lands anywhere outside it.
	/// </summary>
	public sealed class SwipeToReveal : PointerManipulator
	{
		private const float DecideSwipeDistance = 10;
		private const float FullWidthFraction = 0.5f;
		private const float SnapSeconds = 0.15f;

		private readonly VisualElement action;
		private readonly float restingWidth;
		private readonly Action runAction;
		private readonly Action<bool> openChanged;

		private int trackedPointerId = PointerId.invalidPointerId;
		private Vector2 pressStart;
		private float widthAtPress;
		private bool swiping, closeIfNotSwiped;
		private VisualElement listeningRoot, pressedElement;
		private IVisualElementScheduledItem pendingHide;

		public bool IsOpen { get; private set; }

		public SwipeToReveal(VisualElement action, float restingWidth, Action runAction, Action<bool> openChanged = null)
		{
			this.action = action;
			this.restingWidth = restingWidth;
			this.runAction = runAction;
			this.openChanged = openChanged;
		}

		protected override void RegisterCallbacksOnTarget()
		{
			target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
			target.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
			target.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
			target.RegisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
			target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
			target.RegisterCallback<AttachToPanelEvent>(OnAttach);
			target.RegisterCallback<DetachFromPanelEvent>(OnDetach);
		}

		protected override void UnregisterCallbacksFromTarget()
		{
			target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
			target.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
			target.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
			target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel, TrickleDown.TrickleDown);
			target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
			target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
			target.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
			StopListeningForOutsidePresses();
			StopListeningOnPressedElement();
		}

		/// <summary>Shows the action at its resting width without animating, as when a rebuilt entry was open.</summary>
		public void Open()
		{
			SetActionWidth(restingWidth, animate: false);
			SetOpen(true);
		}

		public void Close()
		{
			swiping = false;
			trackedPointerId = PointerId.invalidPointerId;
			StopListeningOnPressedElement();
			bool actionShown = action.style.width.keyword != StyleKeyword.Null;
			if (actionShown) SetActionWidth(0, animate: true);
			SetOpen(false);
		}

		private void OnPointerDown(PointerDownEvent evt)
		{
			if (evt.pointerType != PointerType.touch || trackedPointerId != PointerId.invalidPointerId) return;
			if (IsOpen && action.worldBound.Contains(evt.position)) return;
			trackedPointerId = evt.pointerId;
			pressStart = evt.position;
			widthAtPress = IsOpen ? restingWidth : 0;
			swiping = false;
			closeIfNotSwiped = IsOpen;
			// a tap on an open entry only closes it
			if (IsOpen) evt.StopPropagation();
			else ListenOnPressedElement(evt.target as VisualElement);
		}

		// a pressed button keeps the finger to itself, so its moves never pass through the target
		private void ListenOnPressedElement(VisualElement element)
		{
			StopListeningOnPressedElement();
			if (element == null || element == target) return;
			pressedElement = element;
			pressedElement.RegisterCallback<PointerMoveEvent>(OnPointerMove);
			pressedElement.RegisterCallback<PointerUpEvent>(OnPointerUp);
			pressedElement.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
		}

		private void StopListeningOnPressedElement()
		{
			if (pressedElement == null) return;
			pressedElement.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
			pressedElement.UnregisterCallback<PointerUpEvent>(OnPointerUp);
			pressedElement.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
			pressedElement = null;
		}

		private void OnPointerMove(PointerMoveEvent evt)
		{
			if (evt.pointerId != trackedPointerId) return;
			Vector2 moved = (Vector2)evt.position - pressStart;
			if (!swiping)
			{
				if (Mathf.Abs(moved.y) >= DecideSwipeDistance && Mathf.Abs(moved.y) > Mathf.Abs(moved.x))
				{
					trackedPointerId = PointerId.invalidPointerId;
					StopListeningOnPressedElement();
					if (closeIfNotSwiped) Close();
					return;
				}
				if (-moved.x < DecideSwipeDistance) return;
				swiping = true;
				pendingHide?.Pause();
				// taking the pointer cancels the press on whatever the finger started on
				target.CapturePointer(trackedPointerId);
			}

			evt.StopPropagation();
			float width = Mathf.Clamp(widthAtPress - moved.x, 0, FullWidth);
			bool snapsFull = width >= FullWidth * FullWidthFraction;
			SetActionWidth(snapsFull ? FullWidth : width, animate: snapsFull);
		}

		private void OnPointerUp(PointerUpEvent evt)
		{
			if (evt.pointerId != trackedPointerId) return;
			trackedPointerId = PointerId.invalidPointerId;
			StopListeningOnPressedElement();
			if (target.HasPointerCapture(evt.pointerId)) target.ReleasePointer(evt.pointerId);
			if (!swiping)
			{
				if (closeIfNotSwiped) Close();
				return;
			}

			swiping = false;
			evt.StopPropagation();
			float width = Mathf.Clamp(widthAtPress - (evt.position.x - pressStart.x), 0, FullWidth);
			if (width >= FullWidth * FullWidthFraction)
			{
				runAction?.Invoke();
				Close();
				return;
			}

			if (width >= restingWidth / 2)
			{
				SetActionWidth(restingWidth, animate: true);
				SetOpen(true);
			}
			else Close();
		}

		private void OnPointerCancel(PointerCancelEvent evt)
		{
			if (evt.pointerId == trackedPointerId) SettleAfterLostPointer();
		}

		private void OnCaptureOut(PointerCaptureOutEvent evt)
		{
			if (evt.pointerId == trackedPointerId && swiping) SettleAfterLostPointer();
		}

		private void SettleAfterLostPointer()
		{
			trackedPointerId = PointerId.invalidPointerId;
			StopListeningOnPressedElement();
			swiping = false;
			if (IsOpen) SetActionWidth(restingWidth, animate: true);
			else Close();
		}

		// the room inside the target's border, less the action's own margins
		private float FullWidth
		{
			get
			{
				IResolvedStyle row = target.resolvedStyle, own = action.resolvedStyle;
				float inside = row.width - row.borderLeftWidth - row.borderRightWidth - own.marginLeft - own.marginRight;
				return Mathf.Max(inside, restingWidth);
			}
		}

		private void SetOpen(bool open)
		{
			if (open) StartListeningForOutsidePresses();
			else StopListeningForOutsidePresses();
			if (IsOpen == open) return;
			IsOpen = open;
			openChanged?.Invoke(open);
		}

		private void SetActionWidth(float width, bool animate)
		{
			pendingHide?.Pause();
			IStyle style = action.style;
			style.display = DisplayStyle.Flex;
			style.position = Position.Absolute;
			style.right = 0;
			style.top = 0;
			style.bottom = 0;
			style.minWidth = 0;
			style.width = width;
			style.transitionProperty = new List<StylePropertyName> { "width" };
			style.transitionDuration = new List<TimeValue> { new(animate ? SnapSeconds : 0, TimeUnit.Second) };
			if (width > 0) return;
			pendingHide = action.schedule.Execute(RestoreActionStyle).StartingIn((long)(SnapSeconds * 1000));
		}

		private void RestoreActionStyle()
		{
			IStyle style = action.style;
			style.display = StyleKeyword.Null;
			style.position = StyleKeyword.Null;
			style.right = StyleKeyword.Null;
			style.top = StyleKeyword.Null;
			style.bottom = StyleKeyword.Null;
			style.minWidth = StyleKeyword.Null;
			style.width = StyleKeyword.Null;
			style.transitionProperty = StyleKeyword.Null;
			style.transitionDuration = StyleKeyword.Null;
		}

		private void OnAttach(AttachToPanelEvent _)
		{
			if (IsOpen) StartListeningForOutsidePresses();
		}

		private void OnDetach(DetachFromPanelEvent _) => StopListeningForOutsidePresses();

		private void StartListeningForOutsidePresses()
		{
			VisualElement root = target?.panel?.visualTree;
			if (root == listeningRoot) return;
			StopListeningForOutsidePresses();
			listeningRoot = root;
			listeningRoot?.RegisterCallback<PointerDownEvent>(OnAnyPointerDown, TrickleDown.TrickleDown);
		}

		private void StopListeningForOutsidePresses()
		{
			listeningRoot?.UnregisterCallback<PointerDownEvent>(OnAnyPointerDown, TrickleDown.TrickleDown);
			listeningRoot = null;
		}

		private void OnAnyPointerDown(PointerDownEvent evt)
		{
			if (target.worldBound.Contains(evt.position)) return;
			Close();
		}
	}
}
