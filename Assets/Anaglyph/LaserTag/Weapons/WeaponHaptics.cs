using Anaglyph.Input;
using Oculus.Haptics;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Lasertag.Weapons
{
	// HapticSource.Awake() builds a HapticClipPlayer the moment it's instantiated,
	// which dereferences Haptics.Instance — null on unsupported platforms (e.g. the
	// macOS editor with no device) — and throws. Awake runs even on a disabled
	// component, so toggling 'enabled' can't stop it. Instead we never put a
	// HapticSource on the prefab: we add one at runtime, and only when an XR device
	// is actually present, pointed at the controller of whichever hand we're on.
	public class WeaponHaptics : MonoBehaviour
	{
		[SerializeField] private HandSubject hand;
		[SerializeField] private HapticClip clip;

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
			}

			source.controller = current.Handedness == Handedness.Left
				? Controller.Left
				: Controller.Right;
		}

		// Route the weapon's onFire UnityEvent here instead of HapticSource.Play —
		// it's a safe no-op off-device.
		public void Play()
		{
			source?.Play();
		}
	}
}