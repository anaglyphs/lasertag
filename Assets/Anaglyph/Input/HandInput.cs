using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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

		[SerializeField] private InputActionProperty pointPosition;
		[SerializeField] private InputActionProperty pointRotation;

		[SerializeField] private XRRayInteractor interactor;

		[SerializeField] private Handedness handedness;
		public Handedness Handedness => handedness;

		private static readonly Dictionary<Handedness, HandInput> hands = new();

		public InputActionMap Actions => actionMap;
		public Vector3 Position => position.action.ReadValue<Vector3>();
		public Quaternion Rotation => rotation.action.ReadValue<Quaternion>();
		public Vector3 Forward => Rotation * Vector3.forward;
		public bool Tracked => trackingState.action.ReadValue<bool>();
		public Vector3 PointPosition => pointPosition.action.ReadValue<Vector3>();
		public Quaternion PointRotation => pointRotation.action.ReadValue<Quaternion>();
		public Vector3 PointForward => PointRotation * Vector3.forward;

		// interactor.IsOverUIGameObject() is randomly returning true and I don't know why...
		// I'm not using uGUI canvases anymore so I don't need it /shrug
		public bool InputBlocked => isOverWorldSpaceUIToolkit; // || interactor.IsOverUIGameObject();

		private bool isOverWorldSpaceUIToolkit;

		private void OnUIHoverEntered(UIHoverEventArgs args)
		{
			if (args.uiSystem == UIHoverEventArgs.UISystem.UIToolkit)
				isOverWorldSpaceUIToolkit = true;
		}

		private void OnUIHoverExited(UIHoverEventArgs args)
		{
			if (args.uiSystem == UIHoverEventArgs.UISystem.UIToolkit)
				isOverWorldSpaceUIToolkit = false;
		}

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
			if (interactor)
			{
				interactor.uiHoverEntered.AddListener(OnUIHoverEntered);
				interactor.uiHoverExited.AddListener(OnUIHoverExited);
			}

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
			if (interactor)
			{
				interactor.uiHoverEntered.RemoveListener(OnUIHoverEntered);
				interactor.uiHoverExited.RemoveListener(OnUIHoverExited);
			}
			isOverWorldSpaceUIToolkit = false;

			position.action.Disable();
			rotation.action.Disable();
			pointPosition.action.Disable();
			pointRotation.action.Disable();
			trackingState.action.Disable();
			actionMap.Disable();
		}
	}
}
