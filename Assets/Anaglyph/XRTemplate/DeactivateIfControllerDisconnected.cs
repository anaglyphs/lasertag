using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate
{
	public class DeactivateIfControllerDisconnected : MonoBehaviour
	{
		[SerializeField] private InputDeviceCharacteristics deviceCharacteristics;

		private void Awake()
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
			if (device.characteristics.HasFlag(deviceCharacteristics))
				gameObject.SetActive(device.isValid);
		}
	}
}
