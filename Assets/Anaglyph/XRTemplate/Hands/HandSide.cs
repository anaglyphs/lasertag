using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.WSA;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(-1000)]
	public class HandSide : MonoBehaviour
	{
		public bool isRight;
		public XRNode node { get; private set; }
		public XRController controller { get; private set; }
		public XRRayInteractor rayInteractor { get; private set; }

		private void Awake()
		{
			node = isRight ? XRNode.RightHand : XRNode.LeftHand;

			var controllers = FindObjectsByType<XRController>(FindObjectsSortMode.None);
			foreach (var cont in controllers)
			{
				if (cont.controllerNode == node)
				{
					controller = cont;
					rayInteractor = controller.GetComponentInChildren<XRRayInteractor>(true);
					break;
				}
			}

			InputDevices.deviceConnected += HandleDeviceConnected;
			InputDevices.deviceDisconnected += HandleDeviceConnected;
		}

		private void Start()
		{
			HandleDeviceConnected(controller.inputDevice);
		}

		private void HandleDeviceConnected(InputDevice device)
		{
			gameObject.SetActive(controller.inputDevice.isValid);
		}

		private void OnDestroy()
		{
			InputDevices.deviceConnected -= HandleDeviceConnected;
			InputDevices.deviceDisconnected -= HandleDeviceConnected;
		}

	}
}
