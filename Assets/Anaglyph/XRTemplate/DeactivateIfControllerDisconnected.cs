using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate
{
	public class DeactivateIfControllerDisconnected : MonoBehaviour
	{
		private HandedHierarchy handedParent;
		private InputDevice inputDevice;

		private void Awake()
		{
			handedParent = GetComponentInParent<HandedHierarchy>();
		}

		private void Start()
		{
			InputDevices.deviceConnected += HandleControllerConnection;
			InputDevices.deviceDisconnected += HandleControllerConnection;
		}

		private void OnDestroy()
		{
			InputDevices.deviceConnected -= HandleControllerConnection;
			InputDevices.deviceDisconnected -= HandleControllerConnection;
		}

		private void HandleControllerConnection(InputDevice device)
		{
			var thisDevice = InputDevices.GetDeviceAtXRNode(handedParent.Node);
			if (thisDevice.isValid)
				inputDevice = thisDevice;

			if (device == inputDevice)
				gameObject.SetActive(device.isValid);
		}
	}
}
