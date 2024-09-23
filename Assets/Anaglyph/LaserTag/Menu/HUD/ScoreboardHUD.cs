using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class ScoreboardHUD : MonoBehaviour
	{
		[SerializeField] private Text winMessage;

		public void OnEnable()
		{
			winMessage.text = $"{TeamManagement.TeamNames[RoundManager.WinningTeam]} team won!";

			StartCoroutine(DelayHide());
		}

		private IEnumerator DelayHide()
		{
			yield return new WaitForSeconds(6);

			gameObject.SetActive(false);
		}
	}
}
