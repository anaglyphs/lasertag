using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Anaglyph.Input
{
	public enum Handedness
	{
		Left,
		Right
	}

	public class HandInput : MonoBehaviour
	{
		[SerializeField] private InputActionMap actionMap;
		[SerializeField] private InputActionProperty position; // pointerPosition
		[SerializeField] private InputActionProperty rotation; // pointerRotation
		[SerializeField] private InputActionProperty trackingState;

		[SerializeField] private XRRayInteractor interactor;

		[SerializeField] private Handedness handedness;
		public Handedness Handedness => handedness;

		private static readonly Dictionary<Handedness, HandInput> hands = new();

		public InputActionMap Actions => actionMap;
		public Vector3 Position => position.action.ReadValue<Vector3>();
		public Quaternion Rotation => rotation.action.ReadValue<Quaternion>();
		public Vector3 Forward => Rotation * Vector3.forward;
		public bool Tracked => trackingState.action.ReadValue<bool>();

		// True while this hand's ray is over UI; gameplay binds routed through
		// HandSubject are suppressed while set (pose stays live). Computed live so
		// it reflects the UI module at the exact moment of the input callback and
		// can't go stale or depend on Update order. OR in more gates here later
		// (dead, menu open, …).
		public bool InputBlocked => interactor && interactor.IsOverUIGameObject();

		public static HandInput Get(Handedness h)
		{
			return hands.GetValueOrDefault(h);
		}

		public static event Action<HandInput> Registered;

		private void Awake()
		{
			hands[handedness] = this;
		}

		private void OnEnable()
		{
			position.action.Enable();
			rotation.action.Enable();
			trackingState.action.Enable();
			actionMap.Enable();
			Registered?.Invoke(this);
		}

		private void OnDisable()
		{
			position.action.Disable();
			rotation.action.Disable();
			trackingState.action.Disable();
			actionMap.Disable();
		}
	}
}