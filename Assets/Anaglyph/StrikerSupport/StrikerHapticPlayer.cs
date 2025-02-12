using UnityEngine;
using StrikerLink.Unity.Runtime.Core;
using StrikerLink.Unity.Runtime.HapticEngine;

namespace Anaglyph.StrikerSupport
{
    public class StrikerHapticPlayer : MonoBehaviour
    {
		[SerializeField] private HapticEffectAsset hapticEffect;

		StrikerDevice device;

		private void OnEnable()
		{
			device = GetComponentInParent<StrikerDevice>(true);
		}

		public void Play()
		{
			if (!enabled || device == null)
				return;

			device.FireHaptic(hapticEffect);
		}
	}
}
