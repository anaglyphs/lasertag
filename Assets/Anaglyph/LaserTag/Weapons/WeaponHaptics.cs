using Anaglyph.Input;
using Oculus.Haptics;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Lasertag.Weapons
{
	public class WeaponHaptics : MonoBehaviour
	{
		[SerializeField] private HandSubject hand;
		[SerializeField] private HapticClip clip;
		[SerializeField] private bool loop;

		private HapticSource source;

		private void OnEnable()
		{
			hand.Changed += OnHandChanged;
			OnHandChanged(hand.Current);
		}

		private void OnDisable()
		{
			hand.Changed -= OnHandChanged;
		}

		private void OnHandChanged(HandInput current)
		{
			if (current == null || !XRSettings.isDeviceActive)
				return;

			if (source == null)
			{
				source = gameObject.AddComponent<HapticSource>();
				source.clip = clip;
				source.loop = loop;
			}

			source.controller = current.Handedness == Handedness.Left
				? Controller.Left
				: Controller.Right;
		}

		public void Play()
		{
			source?.Play();
		}
	}
}