using System.Collections.Generic;
using Anaglyph.XR;
using Anaglyph.XR.Haptics;
using Anaglyph.XR.Input;
using Oculus.Haptics;
using StrikerLink.Unity.Runtime.HapticEngine;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace Anaglyph.LaserTag.Weapons
{
	public class WeaponHaptics : MonoBehaviour
	{
		// matches the device StrikerInputDevice reads input from
		private const int StrikerDeviceIndex = 0;

		[SerializeField] private HandSubject hand;
		[FormerlySerializedAs("clip")] [SerializeField] private HapticClip controllerClip;
		[SerializeField] private HapticEffectAsset strikerClip;

		// one per haptic asset: weapons are instantiated again on every switch
		private static readonly Dictionary<HapticClip, PhoneHapticClip> phoneClips = new();

		private HapticSource controllerHapticSource;
		private PhoneHapticClip phoneClip;

		private void Awake()
		{
			if (HandheldAR.IsActive)
			{
				if (!phoneClips.TryGetValue(controllerClip, out phoneClip))
					phoneClips[controllerClip] = phoneClip = new PhoneHapticClip(controllerClip.json);
				return;
			}

			if (Application.isEditor || !XRSettings.isDeviceActive) return;

			controllerHapticSource = gameObject.AddComponent<HapticSource>();
			controllerHapticSource.clip = controllerClip;
		}

		public void Play()
		{
			if (phoneClip != null)
			{
				phoneClip.Play();
				return;
			}

			if (hand.Current == null) return;

			Handedness handedness = hand.Current.Handedness;

			if (MountedPeripheral.IsMountedOn(handedness))
			{
				strikerClip.Fire(StrikerDeviceIndex);
				return;
			}

			if (controllerHapticSource == null) return;

			controllerHapticSource.controller = handedness == Handedness.Left
				? Controller.Left
				: Controller.Right;

			controllerHapticSource.Play();
		}
	}
}