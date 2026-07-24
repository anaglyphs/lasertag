using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
{
	public class GameEndHUD : MonoBehaviour
	{
		private ScoreLabel[] scoreLabels;
		private VisualElement gameEndPopup;
		private CancellationTokenSource scoreCanceller = new();

		private void Start()
		{
			scoreLabels = GetComponentsInChildren<ScoreLabel>(true);

			MatchReferee.MatchFinished += OnMatchFinished;
			MatchReferee.RoundFinished += OnRoundFinished;
			MatchReferee.StateChanged += OnStateChanged;

			gameObject.SetActive(false);
		}

		private void OnEnable()
		{
			gameEndPopup = HUDElement.Require<VisualElement>(this, "game-end-hud");
			HUDElement.SetDisplayed(gameEndPopup, true);
		}

		private void OnDisable()
		{
			if (gameEndPopup != null)
				HUDElement.SetDisplayed(gameEndPopup, false);
		}

		private void OnStateChanged(MatchState state)
		{
			// stay up through the between-round muster, but hide once the next
			// round is starting, or when a fresh match begins mustering
			bool freshMuster = state == MatchState.Mustering && MatchReferee.RoundsPlayed == 0;

			if (freshMuster || state == MatchState.Countdown || state == MatchState.Playing)
			{
				scoreCanceller.Cancel();
				gameObject.SetActive(false);
			}
		}

		private void OnDestroy()
		{
			MatchReferee.MatchFinished -= OnMatchFinished;
			MatchReferee.RoundFinished -= OnRoundFinished;
			MatchReferee.StateChanged -= OnStateChanged;
			scoreCanceller.Cancel();
		}

		private void OnMatchFinished()
		{
			Show();
		}

		private void OnRoundFinished()
		{
			Show();
		}

		private void Show()
		{
			// single-round matches present team scores; multi-round matches
			// present rounds won
			ScoreLabel.ScoreSource source = MatchReferee.Settings.GetNumRounds() > 1
				? ScoreLabel.ScoreSource.RoundWins
				: ScoreLabel.ScoreSource.TeamScore;

			foreach (ScoreLabel scoreLabel in scoreLabels)
				scoreLabel.SetSource(source);

			scoreCanceller.Cancel();
			scoreCanceller = new();
			_ = ShowScore(scoreCanceller.Token);
		}

		private async Task ShowScore(CancellationToken ctn)
		{
			try
			{
				gameObject.SetActive(true);

				await Awaitable.WaitForSecondsAsync(5, ctn);
				ctn.ThrowIfCancellationRequested();
			}
			catch (OperationCanceledException) { }

			gameObject.SetActive(false);
		}
	}
}
