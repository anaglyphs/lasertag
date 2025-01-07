using UnityEngine;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.XR.Interaction.Toolkit;

namespace Anaglyph.XRTemplate
{
    public static class InputHelper
    {
		public static bool IsSameHand(UnityEngine.InputSystem.InputDevice inputSystemDevice, XRController xrController)
        {
			return inputSystemDevice.usages.Contains(UnityEngine.InputSystem.CommonUsages.LeftHand) && 
                xrController.inputDevice.characteristics.HasFlag(UnityEngine.XR.InputDeviceCharacteristics.Left);
        }
    }
}
