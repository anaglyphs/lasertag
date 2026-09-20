using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Shaders;
using Anaglyph.VariableObjects;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	public class LasertagSettings : MonoBehaviour
	{
		[SerializeField] private BoolObject damagedRedVision;
		[SerializeField] private BoolObject lightEffects;
		[SerializeField] private BoolObject relay;

		private void Start()
		{
			damagedRedVision.AddChangeListenerAndCheck(b =>
			{
				if (MainPlayer.Instance != null)
					MainPlayer.Instance.redDamagedVision = b;
			});

			lightEffects.AddChangeListenerAndCheck(b => { DepthLightingRendererFeature.SetGloballyEnabled(b); });
		}
	}
}
