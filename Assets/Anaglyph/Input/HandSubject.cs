using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Input
{
	// A swappable handle to whichever physical hand currently fills a role
	// (e.g. "the tool hand" or "the palette hand"). Components reference the
	// subject rather than a specific HandInput, so they keep working when the
	// hands are reassigned. Pose is read straight through; button callbacks are
	// automatically re-pointed at the new hand on Assign.
	public class HandSubject : MonoBehaviour
	{
		[SerializeField] private HandInput current;
		public HandInput Current => current;

		// Raised whenever the assigned hand changes (null when cleared).
		public event Action<HandInput> Changed;

		// Pose is reported in the rig's tracking space (see HandInput) — apply it
		// as LOCAL position/rotation under the XR Origin, not world space.
		public Vector3 Position => current ? current.Position : Vector3.zero;
		public Quaternion Rotation => current ? current.Rotation : Quaternion.identity;
		public Vector3 Forward => current ? current.Forward : Vector3.forward;
		public Vector3 PointPosition => current ? current.PointPosition : Vector3.zero;
		public Quaternion PointRotation => current ? current.PointRotation : Quaternion.identity;
		public Vector3 PointForward => current ? current.PointForward : Vector3.forward;

		// Each Bind keeps the original callback (for Unbind matching) alongside the
		// wrapper actually subscribed to the action.
		private readonly List<Binding> bindings = new();

		private class Binding
		{
			public string name;
			public Action<InputAction.CallbackContext> callback;
			public Action<InputAction.CallbackContext> dispatch;
		}

		private void Start()
		{
			if (current != null) Subscribe(current);
		}

		public void Assign(HandInput hand)
		{
			if (hand == current) return;

			Unsubscribe(current);
			current = hand;
			Subscribe(current);

			Changed?.Invoke(current);
		}

		// Subscribe a callback to a named action on whichever hand is assigned,
		// now and across future swaps. Callbacks receive all phases
		// (started/performed/canceled) — filter with context.phase as needed.
		// Mirror every Bind with an Unbind (e.g. in OnEnable/OnDisable).
		public void Bind(string actionName, Action<InputAction.CallbackContext> callback)
		{
			Binding binding = new()
			{
				name = actionName,
				callback = callback,
				dispatch = context =>
				{
					// swallow presses while the hand is driving UI, but always let
					// 'canceled' through so any in-progress hold releases cleanly
					if ((!enabled || (current && current.InputBlocked)) && context.phase != InputActionPhase.Canceled)
						return;

					callback(context);
				}
			};

			bindings.Add(binding);
			AddCallback(current, binding);

			// todo: move this somewhere else
			// if (TryGetComponent(out HapticSource hapticSource))
			// {
			// 	hapticSource.enabled = XRSettings.enabled;
			// 	if (XRSettings.enabled)
			// 		hapticSource.controller =
			// 			current.Handedness == Handedness.Left ? Controller.Left : Controller.Right;
			// }
		}

		public void Unbind(string actionName, Action<InputAction.CallbackContext> callback)
		{
			int index = bindings.FindIndex(b => b.name == actionName && b.callback == callback);
			if (index < 0) return;

			RemoveCallback(current, bindings[index]);
			bindings.RemoveAt(index);

			// if (TryGetComponent(out HapticSource hapticSource))
			// 	hapticSource.enabled = false;
		}

		private void Subscribe(HandInput hand)
		{
			if (hand == null) return;
			foreach (Binding binding in bindings)
				AddCallback(hand, binding);
		}

		private void Unsubscribe(HandInput hand)
		{
			if (hand == null) return;
			foreach (Binding binding in bindings)
				RemoveCallback(hand, binding);
		}

		private static void AddCallback(HandInput hand, Binding binding)
		{
			InputAction action = hand ? hand.Actions.FindAction(binding.name) : null;
			if (action == null) return;

			action.started += binding.dispatch;
			action.performed += binding.dispatch;
			action.canceled += binding.dispatch;
		}

		private static void RemoveCallback(HandInput hand, Binding binding)
		{
			InputAction action = hand ? hand.Actions.FindAction(binding.name) : null;
			if (action == null) return;

			action.started -= binding.dispatch;
			action.performed -= binding.dispatch;
			action.canceled -= binding.dispatch;
		}

		private void OnEnable()
		{
			if (didStart)
				Subscribe(current);
		}

		private void OnDisable()
		{
			Unsubscribe(current);
		}

		private void OnDestroy()
		{
			if (current)
				Unsubscribe(current);
		}
	}
}