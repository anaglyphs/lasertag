using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class GameEndHUD : MonoBehaviour
	{
		[SerializeField] private Image[] scoreBars;
		[SerializeField] private Text winMessage;

		public void OnEnable()
		{
			winMessage.text = $"{TeamManagement.TeamNames[RoundManager.Instance.WinningTeam]} team won!";

			StartCoroutine(DelayHide());
		}

		private IEnumerator DelayHide()
		{
			yield return new WaitForSeconds(6);

			gameObject.SetActive(false);
		}
	}
}
