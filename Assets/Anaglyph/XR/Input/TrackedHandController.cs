using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
using UnityEngine.Scripting;

namespace Anaglyph.XR.Input
{
	[StructLayout(LayoutKind.Explicit, Size = 68)]
	public struct TrackedHandControllerState : IInputStateTypeInfo
	{
		public static FourCC Format => new('T', 'H', 'N', 'D');
		public FourCC format => Format;

		[InputControl(usage = "TrackingState", layout = "Integer", offset = 0)]
		[FieldOffset(0)] public int trackingState;

		[InputControl(usage = "IsTracked", layout = "Button", offset = 4)]
		[FieldOffset(4)] public bool isTracked;

		[InputControl(usage = "TriggerButton", layout = "Button", offset = 5)]
		[FieldOffset(5)] public bool triggerPressed;

		[InputControl(usages = new[] { "MenuButton", "Menu" }, layout = "Button", offset = 6)]
		[FieldOffset(6)] public bool menu;

		[InputControl(usage = "Trigger", layout = "Axis", offset = 8)]
		[FieldOffset(8)] public float trigger;

		[InputControl(usage = "DevicePosition", offset = 12, noisy = true, dontReset = true)]
		[FieldOffset(12)] public Vector3 devicePosition;

		[InputControl(usage = "DeviceRotation", offset = 24, noisy = true, dontReset = true)]
		[FieldOffset(24)] public Quaternion deviceRotation;

		[InputControl(usage = "PointerPosition", offset = 40, noisy = true, dontReset = true)]
		[FieldOffset(40)] public Vector3 pointerPosition;

		[InputControl(usage = "PointerRotation", offset = 52, noisy = true, dontReset = true)]
		[FieldOffset(52)] public Quaternion pointerRotation;
	}

	/// <summary>
	/// A tracked hand presented as an XR controller: pinch is the trigger, the aim ray is the
	/// pointer pose, and the palm is the device pose. Bindings to XRController usages pick it up.
	/// </summary>
#if UNITY_EDITOR
	[UnityEditor.InitializeOnLoad]
#endif
	[Preserve, InputControlLayout(stateType = typeof(TrackedHandControllerState),
		 displayName = "Tracked Hand Controller", commonUsages = new[] { "LeftHand", "RightHand" })]
	public class TrackedHandController : XRController
	{
		public ButtonControl triggerPressed { get; private set; }
		public AxisControl trigger { get; private set; }
		public ButtonControl menu { get; private set; }
		public Vector3Control pointerPosition { get; private set; }
		public QuaternionControl pointerRotation { get; private set; }

		protected override void FinishSetup()
		{
			base.FinishSetup();
			triggerPressed = GetChildControl<ButtonControl>("triggerPressed");
			trigger = GetChildControl<AxisControl>("trigger");
			menu = GetChildControl<ButtonControl>("menu");
			pointerPosition = GetChildControl<Vector3Control>("pointerPosition");
			pointerRotation = GetChildControl<QuaternionControl>("pointerRotation");
		}

		static TrackedHandController() => RegisterLayout();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void RegisterLayout() => InputSystem.RegisterLayout<TrackedHandController>();
	}
}
