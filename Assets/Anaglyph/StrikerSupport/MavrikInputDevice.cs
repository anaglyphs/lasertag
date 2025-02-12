using StrikerLink.Shared.Devices.DeviceFeatures;
using StrikerLink.Shared.Devices.Types;
using StrikerLink.Unity.Runtime.Core;
using System.Runtime.InteropServices;
using Unity.XR.Oculus.Input;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;

public class StrikerInputDevice : MonoBehaviour
{
	private MavrikDevice inputDevice;

	private DeviceBase strikerDevice;
	private OculusTouchController leftController;

	private void Awake()
	{
		inputDevice = InputSystem.AddDevice<MavrikDevice>();

		StrikerController.Controller.OnClientConnected.AddListener(OnClientConnected);

		InputSystem.onDeviceChange += OnDeviceChange;

		InputSystem.onBeforeUpdate += OnBeforeInputUpdate;
	}

	private void OnDestroy()
	{
		InputSystem.RemoveDevice(inputDevice);

		StrikerController.Controller.OnClientConnected.RemoveListener(OnClientConnected);

		InputSystem.onDeviceChange -= OnDeviceChange;

		InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;
	}

	private void OnDeviceChange(InputDevice device, InputDeviceChange change)
	{
		leftController = (OculusTouchController)XRController.leftHand;
	}

	private void OnClientConnected()
	{
		strikerDevice = StrikerController.Controller.GetClient().GetDevice(0);
	}

	public void OnBeforeInputUpdate()
	{
		if (strikerDevice == null || leftController == null)
			return;

		float triggerAxis = strikerDevice.GetAxis(DeviceAxis.TriggerAxis);
		inputDevice.trigger.QueueValueChange(triggerAxis);
	}
}



[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct MavrikState : IInputStateTypeInfo
{
	public FourCC format => new FourCC('M', 'V', 'R', 'K');

	[FieldOffset(5)] 
	[InputControl(layout = "Button")]
	public ushort trigger;
}

#if UNITY_EDITOR
[InitializeOnLoad]
#endif
[InputControlLayout(displayName = "StrikerVR Mavrik", stateType = typeof(MavrikState))]
public class MavrikDevice : InputDevice
{
	static MavrikDevice()
	{
		InputSystem.RegisterLayout<MavrikDevice>();
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void InitializeInPlayer() { }

	public AxisControl trigger { get; private set; }


	protected override void FinishSetup()
	{
		base.FinishSetup();
		trigger = GetChildControl<AxisControl>(nameof(MavrikState.trigger));
	}
}