using System.Collections.Generic;
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

		private void Start()
		{
			var devices = new List<InputDevice>();
			var c = deviceCharacteristics;
			InputDevices.GetDevicesWithCharacteristics(c, devices);

			foreach (InputDevice device in devices)
			{
				if (device.isValid)
					return;
			}

			gameObject.SetActive(false);
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
