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

		private bool jumblingTheyNumers = false;


		private void Start()
		{
			MatchManager.MatchStateChanged += HandleStateChange;

			jumblingTheyNumers = false;
			gameOver.enabled = false;
			versus.enabled = false;
			redScoreText.enabled = false;
			blueScoreText.enabled = false;
		}

		private void Update()
		{
			if (jumblingTheyNumers)
			{
				redScoreText.fontSize = 60;
				blueScoreText.fontSize = 60;

				redScoreText.text = $"{Mathf.RoundToInt(Random.Range(0, 99))}";
				blueScoreText.text = $"{Mathf.RoundToInt(Random.Range(0, 99))}";
			} else
			{
				redScoreText.fontSize = MatchManager.WinningTeam == 1 ? 80 : 60;
				blueScoreText.fontSize = MatchManager.WinningTeam == 2 ? 80 : 60;

				redScoreText.text = $"{MatchManager.GetTeamScore(1)}";
				blueScoreText.text = $"{MatchManager.GetTeamScore(2)}";
			}
		}

		private void OnDestroy()
		{
			MatchManager.MatchStateChanged -= HandleStateChange;
		}

		private async void HandleStateChange(MatchState prev, MatchState state)
		{
			if (state == MatchState.NotPlaying && prev == MatchState.Playing)
			{
				jumblingTheyNumers = true;
				gameOver.enabled = true;
				versus.enabled = true;
				redScoreText.enabled = true;
				blueScoreText.enabled = true;

				await Awaitable.WaitForSecondsAsync(3);

				jumblingTheyNumers = false;

				await Awaitable.WaitForSecondsAsync(5);

				gameOver.enabled = false;
				versus.enabled = false;
				redScoreText.enabled = false;
				blueScoreText.enabled = false;

			} else
			{
				jumblingTheyNumers = false;
				gameOver.enabled = false;
				versus.enabled = false;
				redScoreText.enabled = false;
				blueScoreText.enabled = false;
			}
		}
	}
}
