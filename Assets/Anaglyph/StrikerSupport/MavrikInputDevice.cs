using StrikerLink.Shared.Devices.DeviceFeatures;
using StrikerLink.Shared.Devices.Types;
using StrikerLink.Unity.Runtime.Core;
using System.Runtime.InteropServices;
using Unity.XR.Oculus.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;

[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct MyDeviceState : IInputStateTypeInfo
{
	public FourCC format => new FourCC('M', 'Y', 'D', 'V');

	[FieldOffset(0)]
	[InputControl(name = "button", layout = "Button", bit = 3)]
	public ushort buttons;
}

[InputControlLayout(displayName = "My Device", stateType = typeof(MyDeviceState))]
public class StrikerInputDevice : InputDevice, IInputUpdateCallbackReceiver
{
	private DeviceBase strikerDevice;
	private OculusTouchController leftController;
	private float triggerAxis;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void InitializeInPlayer()
	{
		InputSystem.RegisterLayout<StrikerInputDevice>();
		InputSystem.AddDevice<StrikerInputDevice>();
	}

	public void OnUpdate()
	{
		// todo, move this all somewhere else
		if (StrikerController.IsConnected)
		{
			if (strikerDevice == null)
			{
				strikerDevice = StrikerController.Controller.GetClient().GetDevice(0);
				return;
			}
		}
		else
		{
			return;
		}

		if (leftController == null)
		{
			leftController = (OculusTouchController)XRController.leftHand;
			return;
		}

		using (StateEvent.From(leftController, out var eventPtr))
		{
			float triggerAxis = strikerDevice.GetAxis(DeviceAxis.TriggerAxis);

			leftController.triggerPressed.WriteValueIntoEvent(triggerAxis, eventPtr);
			leftController.trigger.WriteValueIntoEvent(triggerAxis, eventPtr);

			InputSystem.QueueEvent(eventPtr);
		}
	}
}