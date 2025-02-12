using StrikerLink.Unity.Runtime.Core;
using System;
using UnityEngine;

namespace Anaglyph.Lasertag.ControllerIntegration
{
	public class ConformBlasterToController : MonoBehaviour
	{
		[Serializable]
		public struct BlasterConfiguration
		{
			public Transform offset;
			public Transform bulletEmitterPose;
			public Transform sightPose;
			public bool visualsAreVisible;
		}

		[SerializeField] private Transform offset;
		[SerializeField] private Transform bulletEmitter;
		[SerializeField] private Transform sight;
		[SerializeField] private GameObject visuals;

		[SerializeField] private BlasterConfiguration oculusTouch;
		[SerializeField] private BlasterConfiguration strikerMavrik;

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

		private void MatchTransform(Transform a, Transform b)
			=> a.SetPositionAndRotation(b.position, b.rotation);

		private void Configure(BlasterConfiguration configuration)
		{
			MatchTransform(offset, configuration.offset);
			MatchTransform(bulletEmitter, configuration.bulletEmitterPose);
			MatchTransform(sight, configuration.sightPose);
			visuals.SetActive(configuration.visualsAreVisible);
		}

		private void OnStrikerDeviceEvent(StrikerDevice device) => UpdateConfiguration();

		private void UpdateConfiguration()
		{
			if(device != null && device.isConnected)
				Configure(strikerMavrik);
			else
				Configure(oculusTouch);
		}
	}
}