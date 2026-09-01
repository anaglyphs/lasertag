using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player.Teams;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface.HUD
{
	/// <summary>
	/// Hand-mounted HUD showing how the current round is going. One section is
	/// shown at a time, picked by the win condition.
	/// </summary>
	[DefaultExecutionOrder(9999)]
	public class HandHUD : MonoBehaviour
	{
		private VisualElement timerHUD;
		private Label timerLabel;
		private Label[] timerScores;

		private VisualElement scoreGoalHUD;
		private Label scoreTargetLabel;
		private Label[] goalScores;

		private Label roundLabel;

		private readonly int[] shownTimerScores = new int[Teams.NumTeams];
		private readonly int[] shownGoalScores = new int[Teams.NumTeams];
		private int shownScoreTarget;
		private int shownRound;

		private void OnEnable()
		{
			timerHUD = HUDElement.Require<VisualElement>(this, "timer-hud");
			timerLabel = HUDElement.Require<Label>(this, "timer-label");

			timerScores = new Label[Teams.NumTeams];
			timerScores[1] = HUDElement.Require<Label>(this, "timer-red-score");
			timerScores[2] = HUDElement.Require<Label>(this, "timer-blue-score");

			scoreGoalHUD = HUDElement.Require<VisualElement>(this, "score-hud");
			scoreTargetLabel = HUDElement.Require<Label>(this, "target-score-label");

			goalScores = new Label[Teams.NumTeams];
			goalScores[1] = HUDElement.Require<Label>(this, "goal-red-score");
			goalScores[2] = HUDElement.Require<Label>(this, "goal-blue-score");

			roundLabel = HUDElement.Require<Label>(this, "round-label");

			InvalidateShownValues();

			MatchReferee.TimerTextChanged += UpdateTimerText;
		}

		private void OnDisable()
		{
			MatchReferee.TimerTextChanged -= UpdateTimerText;

			HUDElement.SetDisplayed(timerHUD, false);
			HUDElement.SetDisplayed(scoreGoalHUD, false);
			HUDElement.SetDisplayed(roundLabel, false);
		}

		private void Update()
		{
			MatchSettings settings = MatchReferee.Settings;
			bool playing = MatchReferee.State != MatchState.NotPlaying;

			// both sections fill the whole panel, so only one may be up; the
			// timer wins when a mode is won by time and score together
			bool showTimer = playing && settings.CheckWinByTimer();
			bool showScoreGoal = playing && !showTimer && settings.CheckWinByScore();

			HUDElement.SetDisplayed(timerHUD, showTimer);
			HUDElement.SetDisplayed(scoreGoalHUD, showScoreGoal);

			if (showTimer)
			{
				UpdateTimerSand();

				for (byte team = 1; team < Teams.NumTeams; team++)
					SetInt(timerScores[team], ref shownTimerScores[team],
						MatchReferee.GetTeamScore(team));
			}

			if (showScoreGoal)
			{
				SetInt(scoreTargetLabel, ref shownScoreTarget, settings.scoreTarget);

				for (byte team = 1; team < Teams.NumTeams; team++)
				{
					int score = MatchReferee.GetTeamScore(team);
					SetInt(goalScores[team], ref shownGoalScores[team], score);
					UpdateScoreLine(team, score, settings.scoreTarget);
				}
			}

			UpdateRoundLabel(playing, settings.GetNumRounds());
		}

		private void UpdateRoundLabel(bool playing, int numRounds)
		{
			bool show = playing && numRounds > 1;
			HUDElement.SetDisplayed(roundLabel, show);

			if (!show) return;

			int round = MatchReferee.CurrentRound;
			if (round == shownRound) return;

			shownRound = round;
			roundLabel.text = $"ROUND {round} / {numRounds}";
		}

		private void UpdateScoreLine(byte team, int score, short target)
		{
			float progress = target > 0 ? score / (float)target : 0f;

			// scoreLines[team].style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
		}

		private void UpdateTimerText(string timerString)
		{
			timerLabel.text = timerString;
		}

		private void UpdateTimerSand()
		{
			int timeTotal = MatchReferee.Settings.roundTimeSeconds;
			float timeLeft = MatchReferee.Instance.GetTimeLeft();

			float tn = timeTotal > 0 ? Mathf.Clamp01(timeLeft / timeTotal) : 0f;
		}

		private void InvalidateShownValues()
		{
			for (int i = 0; i < Teams.NumTeams; i++)
			{
				shownTimerScores[i] = int.MinValue;
				shownGoalScores[i] = int.MinValue;
			}

			shownScoreTarget = int.MinValue;
			shownRound = 0;
		}

		private static void SetInt(Label label, ref int shown, int value)
		{
			if (shown == value) return;

			shown = value;
			label.text = value.ToString();
		}
	}
}
