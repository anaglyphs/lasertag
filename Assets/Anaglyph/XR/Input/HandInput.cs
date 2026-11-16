using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Anaglyph.XR.Input
{
	public enum Handedness
	{
		Left,
		Right
	}

	public class HandInput : MonoBehaviour
	{
		[SerializeField] private InputActionMap actionMap;
		[SerializeField] private InputActionProperty position;
		[SerializeField] private InputActionProperty rotation;

		[SerializeField] private InputActionProperty pointPosition;
		[SerializeField] private InputActionProperty pointRotation;
		
		[SerializeField] private InputActionProperty trackingState;

		[SerializeField] private XRRayInteractor interactor;

		[SerializeField] private Handedness handedness;
		public Handedness Handedness => handedness;

		private static readonly Dictionary<Handedness, HandInput> hands = new();
		public static readonly ReadOnlyDictionary<Handedness, HandInput> Hands = new(hands);

		public InputActionMap Actions => actionMap;
		/// <summary>
		/// Set on a phone, where the hand is the screen held out in front: every hand follows this
		/// camera (its local pose, as controller poses are), counts as tracked, and only taps on the
		/// screen's UI block its input. Null for controllers and tracked hands.
		/// </summary>
		public static Transform FollowedCamera { get; private set; }

		public static void FollowCamera(Transform camera)
		{
			FollowedCamera = camera;
			foreach (HandInput hand in hands.Values)
				if (hand != null) hand.IsTrackingChanged?.Invoke(hand.IsTracking);
		}

		private static bool FollowsCamera => FollowedCamera != null;

		public Vector3 Position => FollowsCamera ? FollowedCamera.localPosition : position.action.ReadValue<Vector3>();
		public Quaternion Rotation => FollowsCamera ? FollowedCamera.localRotation : rotation.action.ReadValue<Quaternion>();
		public Vector3 Forward => Rotation * Vector3.forward;
		public Vector3 PointPosition => FollowsCamera ? Position : PointsFromPeripheral
			? Position + Rotation * MountedPeripheral.Current.BarrelFromController.position
			: pointPosition.action.ReadValue<Vector3>();

		public Quaternion PointRotation => FollowsCamera ? Rotation : PointsFromPeripheral
			? Rotation * MountedPeripheral.Current.BarrelFromController.rotation
			: pointRotation.action.ReadValue<Quaternion>();

		public Vector3 PointForward => PointRotation * Vector3.forward;

		// The runtime's aim pose describes a controller held in a hand. Mounted in a cradle it
		// describes nothing, so the peripheral's barrel replaces it rather than offsetting it.
		private bool PointsFromPeripheral => MountedPeripheral.IsMountedOn(handedness);
		/// <summary>Which parts of the pose the runtime currently vouches for.</summary>
		public InputTrackingState TrackingState => FollowsCamera
			? InputTrackingState.Position | InputTrackingState.Rotation
			: (InputTrackingState)trackingState.action.ReadValue<int>();

		public bool IsTracking => TrackingState != InputTrackingState.None;

		// Polled rather than latched off interactor.uiHoverEntered/Exited: the exit event is only
		// raised while the interactor keeps ticking, so a UIDocument that disappears out from under
		// the ray (menu closing, interactor disabled the same frame) can leave the latch stuck on.
		// This is the same test XRI runs internally (XRUIToolkitHandler.HasUIDocument): the world
		// space panel collider lives on the UIDocument GameObject.
		public bool InputBlocked => FollowsCamera ? ScreenSpaceUIHit.IsPrimaryTouchOverUI() : RayHoveredDocument != null;

		/// <summary>The world-space UI Toolkit document under this hand's ray; null when none.</summary>
		public UIDocument RayHoveredDocument
		{
			get
			{
				if (interactor == null || !interactor.isActiveAndEnabled ||
				    !interactor.TryGetCurrent3DRaycastHit(out var hit) || hit.collider == null ||
				    !hit.collider.TryGetComponent(out UIDocument document) || !document.isActiveAndEnabled)
					return null;

				return document;
			}
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
			if (!FollowsCamera) IsTrackingChanged?.Invoke(tracked.ReadValue<int>() != 0);
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
