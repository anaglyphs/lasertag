using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Interface.HUD
{
	public class ScoreHUDHandSelector : MonoBehaviour
	{
		[SerializeField] private HandHUDPositioner positioner;

		private HandInput follow;

		private void Update()
		{
			if (follow != null && follow.IsTracking)
				return;

			follow = null;
			foreach (HandInput hand in HandInput.Hands.Values)
			{
				if (!hand.IsTracking)
					continue;

				follow = hand;
				break;
			}

			positioner.SetHand(follow);
		}
	}
}
