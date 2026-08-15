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

		private HapticSource controllerHapticSource;

		private void Awake()
		{
			if (Application.isEditor || !XRSettings.isDeviceActive) return;

			controllerHapticSource = gameObject.AddComponent<HapticSource>();
			controllerHapticSource.clip = controllerClip;
		}

		public void Play()
		{
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