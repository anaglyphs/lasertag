using StrikerLink.Shared.Client;
using StrikerLink.Shared.Devices.DeviceFeatures;
using StrikerLink.Shared.Devices.Types;
using StrikerLink.Unity.Runtime.Core;
using System.Runtime.InteropServices;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

public class StrikerInputDevice : MonoBehaviour
{
	private MavrikDevice inputDevice;

	private StrikerClient strikerClient;
	private DeviceBase strikerDevice;

	private void Awake()
	{
		inputDevice = InputSystem.AddDevice<MavrikDevice>();
		
		InputSystem.onBeforeUpdate += OnBeforeInputUpdate;
	}

	private void OnDestroy()
	{
		InputSystem.RemoveDevice(inputDevice);
		InputSystem.onBeforeUpdate -= OnBeforeInputUpdate;
	}

	public void OnBeforeInputUpdate()
	{
		float triggerAxis = 0;

		var strikerClient = StrikerController.Controller.GetClient();

		if (strikerClient != null && strikerClient.IsConnected)
		{
			if (strikerDevice == null || !strikerDevice.Connected)
			{
				strikerDevice = strikerClient.GetDevice(0);
			}
			else if(strikerDevice.Connected)
			{
				triggerAxis = strikerDevice.GetAxis(DeviceAxis.TriggerAxis);
			}
		}
		
		if(triggerAxis != inputDevice.trigger.value)
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