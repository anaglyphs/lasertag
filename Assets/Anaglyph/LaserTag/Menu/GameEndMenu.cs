using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class GameEndHUD : MonoBehaviour
	{
		[SerializeField] private Image[] scoreBars;
		[SerializeField] private Text winMessage;

		public void Show()
		{
			gameObject.SetActive(true);

			int highestScore = 0;
			byte winningTeam = 0;

			for(byte i = 1; i < TeamManagement.NumTeams; i++)
			{
				int score = RoundManager.Instance.GetTeamScore(i);

				if(score > highestScore)
				{
					winningTeam = i;
					highestScore = score;
				}
			}

			for(byte i = 1; i < TeamManagement.NumTeams; i++)
			{
				scoreBars[i - i].fillAmount = (float)RoundManager.Instance.GetTeamScore(i) / (float)highestScore;
			}

			winMessage.text = $"{TeamManagement.TeamNames[winningTeam]} team won!";

			StartCoroutine(DelayHide());
		}

		private IEnumerator DelayHide()
		{
			yield return new WaitForSeconds(4);

			gameObject.SetActive(false);
		}
	}
}
