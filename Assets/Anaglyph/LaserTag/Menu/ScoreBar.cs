using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class ScoreBar : MonoBehaviour
    {
		[SerializeField] private Image image;
		public byte team = 0;

		private void OnValidate()
		{
			this.SetComponent(ref image);
		}

		private void Update()
		{
			int divideBy = 0;

			if (RoundManager.Instance.ActiveSettings.CheckWinByTimer())
			{
				for (byte i = 1; i < TeamManagement.NumTeams; i++)
				{
					int score = RoundManager.Instance.GetTeamScore(i);

					if (score > divideBy)
						divideBy = score;
				}
			} else if(RoundManager.Instance.ActiveSettings.CheckWinByPoints())
			{
				divideBy = RoundManager.Instance.ActiveSettings.scoreTarget;
			}

			image.fillAmount = divideBy > 0 ? (float)RoundManager.Instance.GetTeamScore(team) / (float)divideBy : 0;
		}
	}
}
