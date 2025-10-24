using UnityEngine;
using StrikerLink.Unity.Runtime.Core;
using StrikerLink.Unity.Runtime.HapticEngine;
using Anaglyph.XRTemplate;
using Oculus.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Anaglyph.Lasertag.ControllerIntegration
{
	public class HapticPlayer : MonoBehaviour
	{
		// oculus
		[SerializeField] private HapticClip oculusHapticClip;
		private HapticClipPlayer oculusHapticPlayer;

		// striker
		[SerializeField] private HapticEffectAsset strikerHapticEffect;
		private StrikerDevice strikerDevice;


		private HandedHierarchy hierarchyHandedness;

		private void Awake()
		{
			if (Application.isEditor)
				return;
			
			hierarchyHandedness = GetComponentInParent<HandedHierarchy>(true);
			oculusHapticPlayer = new();
			oculusHapticPlayer.clip = oculusHapticClip;
		}

		private void OnEnable()
		{
			strikerDevice = GetComponentInParent<StrikerDevice>(true);
		}

		public void Play()
		{
			if (!enabled)
				return;

			if (hierarchyHandedness.Handedness == InteractorHandedness.Left &&
				strikerDevice != null && strikerDevice.isReady)
			{
				strikerDevice.FireHaptic(strikerHapticEffect);
			}
			else
			{
				Controller oculusController = Controller.Both;

				if (hierarchyHandedness.Handedness == InteractorHandedness.Left)
					oculusController = Controller.Left;
				else if (hierarchyHandedness.Handedness == InteractorHandedness.Right)
					oculusController = Controller.Right;

				oculusHapticPlayer.Play(oculusController);
			}
		}
	}
}
