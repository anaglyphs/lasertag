using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Shaders;
using Anaglyph.VariableObjects;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	public class LasertagSettings : MonoBehaviour
	{
		[SerializeField] private BoolObject boundary;
		[SerializeField] private BoolObject damagedRedVision;
		[SerializeField] private BoolObject lightEffects;
		[SerializeField] private BoolObject relay;

		private void Start()
		{
			// boundary.AddChangeListenerAndCheck(b =>
			// {
			// });

			damagedRedVision.AddChangeListenerAndCheck(b =>
			{
				if (MainPlayer.Instance != null)
					MainPlayer.Instance.redDamagedVision = b;
			});

			lightEffects.AddChangeListenerAndCheck(b => { DepthLight.SetGloballyEnabled(b); });

			// relay.AddChangeListenerAndCheck(b =>
			// {
			// });
		}
	}
}
