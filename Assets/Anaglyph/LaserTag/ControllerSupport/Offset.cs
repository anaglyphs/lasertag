using StrikerLink.Unity.Runtime.Core;
using UnityEngine;

namespace Anaglyph.Lasertag.ControllerSupport
{
    public class Offset : MonoBehaviour
    {
		public Transform mavrikPoseTransform;

		private StrikerDevice device;

		private void Awake()
		{
			device = GetComponentInParent<StrikerDevice>();
			if (device != null)
			{
				device.DeviceEvents.OnDeviceConnected.AddListener(OnStrikerDeviceEvent);
				device.DeviceEvents.OnDeviceDisconnected.AddListener(OnStrikerDeviceEvent);
			}

			UpdateConfiguration();
		}

		private void OnStrikerDeviceEvent(StrikerDevice device) => UpdateConfiguration();

		private void UpdateConfiguration()
		{
			if (device != null && device.isConnected)
			{
				transform.localPosition = mavrikPoseTransform.localPosition;
				transform.localRotation = mavrikPoseTransform.localRotation;
				//transform.SetLocalPositionAndRotation(mavrikPoseTransform.localPosition, mavrikPoseTransform.localRotation);
			}
			else
			{
				transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
			}
		}
	}
}
