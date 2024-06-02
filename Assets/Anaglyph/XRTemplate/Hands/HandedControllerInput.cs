using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(-1000)]
	public class HandedControllerInput : SuperAwakeBehavior
	{
		private HandSide handSide;
		private InputDevice inputDevice;
		private InputDeviceRole inputDeviceRole;

		public bool TriggerWasDown { get; private set; }
		public bool TriggerIsDown { get; private set; }

		public UnityEvent OnTriggerPress;

		public Vector2 JoystickVector { get; private set; }

		protected override void SuperAwake()
		{
			handSide = GetComponentInParent<HandSide>(true);
			inputDeviceRole = handSide.isRight ? InputDeviceRole.RightHanded : InputDeviceRole.LeftHanded;
		}

		private void Start()
		{
			OnInputDeviceConnect(InputDevices.GetDeviceAtXRNode(handSide.node));
			InputDevices.deviceConnected += OnInputDeviceConnect;
		}

		private void OnDestroy()
		{
			InputDevices.deviceConnected -= OnInputDeviceConnect;
		}

		private void OnInputDeviceConnect(InputDevice device)
		{
			if (device.role == inputDeviceRole)
			{
				inputDevice = device;
			}
		}


		private void Update()
		{
			TriggerWasDown = TriggerIsDown;

			if (handSide.rayInteractor != null && handSide.rayInteractor.IsOverUIGameObject())
			{
				TriggerIsDown = false;
				return;
			}

			inputDevice.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerIsDown);
			TriggerIsDown = triggerIsDown;

			if (Input.GetMouseButtonDown(0)) {
				TriggerIsDown = true;
			}

			inputDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 joystickVector);
			JoystickVector = joystickVector;

			if(TriggerIsDown && !TriggerWasDown)
			{
				OnTriggerPress.Invoke();
			}
		}

		private void OnDisable()
		{
			TriggerWasDown = false;
			TriggerIsDown = false;
			JoystickVector = Vector2.zero;
		}
	}
}
