using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

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

		[SerializeField] private InputActionProperty pointPosition;
		[SerializeField] private InputActionProperty pointRotation;
		
		[SerializeField] private InputActionProperty trackingState;

		[SerializeField] private XRRayInteractor interactor;

		[SerializeField] private Handedness handedness;
		public Handedness Handedness => handedness;

		private static readonly Dictionary<Handedness, HandInput> hands = new();

		public InputActionMap Actions => actionMap;
		public Vector3 Position => position.action.ReadValue<Vector3>();
		public Quaternion Rotation => rotation.action.ReadValue<Quaternion>();
		public Vector3 Forward => Rotation * Vector3.forward;
		public Vector3 PointPosition => PointsFromPeripheral
			? Position + Rotation * MountedPeripheral.Current.BarrelFromController.position
			: pointPosition.action.ReadValue<Vector3>();

		public Quaternion PointRotation => PointsFromPeripheral
			? Rotation * MountedPeripheral.Current.BarrelFromController.rotation
			: pointRotation.action.ReadValue<Quaternion>();

		public Vector3 PointForward => PointRotation * Vector3.forward;

		// The runtime's aim pose describes a controller held in a hand. Mounted in a cradle it
		// describes nothing, so the peripheral's barrel replaces it rather than offsetting it.
		private bool PointsFromPeripheral => MountedPeripheral.IsMountedOn(handedness);
		public bool IsTracking => trackingState.action.ReadValue<int>() != 0;

		// Polled rather than latched off interactor.uiHoverEntered/Exited: the exit event is only
		// raised while the interactor keeps ticking, so a UIDocument that disappears out from under
		// the ray (menu closing, interactor disabled the same frame) can leave the latch stuck on.
		// This is the same test XRI runs internally (XRUIToolkitHandler.HasUIDocument): the world
		// space panel collider lives on the UIDocument GameObject.
		public bool InputBlocked => IsOverWorldSpaceUIToolkit();

		private bool IsOverWorldSpaceUIToolkit()
		{
			if (interactor == null || !interactor.isActiveAndEnabled)
				return false;

			return interactor.TryGetCurrent3DRaycastHit(out var hit) &&
			       hit.collider != null &&
			       hit.collider.TryGetComponent(out UIDocument document) &&
			       document.isActiveAndEnabled;
		}

		public static HandInput Get(Handedness h)
		{
			return hands.GetValueOrDefault(h);
		}

		public static event Action<HandInput> Registered;
		public event Action<bool> IsTrackingChanged;

		private void Awake()
		{
			hands[handedness] = this;
			
			trackingState.action.performed += OnTrackingStateChanged;
		}
		
		private void OnTrackingStateChanged(InputAction.CallbackContext tracked)
		{
			IsTrackingChanged?.Invoke(tracked.ReadValue<int>() != 0);
		}

		private void OnDestroy()
		{
			trackingState.action.performed -= OnTrackingStateChanged;
		}

		private void OnEnable()
		{
			position.action.Enable();
			rotation.action.Enable();
			pointPosition.action.Enable();
			pointRotation.action.Enable();
			trackingState.action.Enable();
			actionMap.Enable();
			Registered?.Invoke(this);
		}

		private void OnDisable()
		{
			position.action.Disable();
			rotation.action.Disable();
			pointPosition.action.Disable();
			pointRotation.action.Disable();
			trackingState.action.Disable();
			actionMap.Disable();
		}
	}
}
