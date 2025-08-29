using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class GameEndHUD : MonoBehaviour
	{
		[SerializeField] private Graphic gameOver;
		[SerializeField] private Graphic versus;
		[SerializeField] private Text redScoreText;
		[SerializeField] private Text blueScoreText;

		// private bool jumblingTheyNumers = false;

		private MatchReferee matchManager => MatchReferee.Instance;

		private void Start()
		{
			matchManager.MatchFinished += OnMatchFinished;
			gameObject.SetActive(false);
		}

		private void OnDestroy()
		{
			matchManager.MatchFinished -= OnMatchFinished;
		}

		private async void OnMatchFinished()
		{
			byte winningTeam = matchManager.CalculateWinningTeam();
			gameObject.SetActive(true);

			redScoreText.fontSize = winningTeam == 1 ? 80 : 60;
			blueScoreText.fontSize = winningTeam == 2 ? 80 : 60;

			redScoreText.text = $"{matchManager.GetTeamScore(1)}";
			blueScoreText.text = $"{matchManager.GetTeamScore(2)}";

			await Awaitable.WaitForSecondsAsync(5);

			gameObject.SetActive(false);
		}

		//private void Update()
		//{
		//	if (jumblingTheyNumers)
		//	{
		//		redScoreText.fontSize = 60;
		//		blueScoreText.fontSize = 60;

		//		redScoreText.text = $"{Mathf.RoundToInt(Random.Range(0, 99))}";
		//		blueScoreText.text = $"{Mathf.RoundToInt(Random.Range(0, 99))}";
		//	} else
		//	{
		//		redScoreText.fontSize = matchManager.WinningTeam == 1 ? 80 : 60;
		//		blueScoreText.fontSize = matchManager.WinningTeam == 2 ? 80 : 60;

		//		redScoreText.text = $"{matchManager.GetTeamScore(1)}";
		//		blueScoreText.text = $"{matchManager.GetTeamScore(2)}";
		//	}
		//}
	}
}
