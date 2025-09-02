using System;
using System.Threading;
using System.Threading.Tasks;
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

		private MatchReferee referee => MatchReferee.Instance;

		private CancellationTokenSource scoreCanceller = new();

		private void Start()
		{
			referee.MatchFinished += OnMatchFinished;
			referee.StateChanged += OnStateChanged;
			gameObject.SetActive(false);
		}

		private void OnStateChanged(MatchState state)
		{
			if (state != MatchState.NotPlaying)
			{
				scoreCanceller.Cancel();
				gameObject.SetActive(false);
			}
		}

		private void OnDestroy()
		{
			referee.MatchFinished -= OnMatchFinished;
			scoreCanceller.Cancel();
		}

		private void OnMatchFinished()
		{
			scoreCanceller.Cancel();
			scoreCanceller = new();
			ShowScore(scoreCanceller.Token);
		}

		private async Task ShowScore(CancellationToken ctn)
		{
			try
			{
				byte winningTeam = referee.CalculateWinningTeam();
				gameObject.SetActive(true);

				redScoreText.fontSize = winningTeam == 1 ? 80 : 60;
				blueScoreText.fontSize = winningTeam == 2 ? 80 : 60;

				redScoreText.text = $"{referee.GetTeamScore(1)}";
				blueScoreText.text = $"{referee.GetTeamScore(2)}";

				await Awaitable.WaitForSecondsAsync(5, ctn);
				ctn.ThrowIfCancellationRequested();

			} catch(OperationCanceledException) { }

			gameObject.SetActive(false);
		}
	}
}
