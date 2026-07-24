using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(9999)]
	public class GameHUD : MonoBehaviour
	{
		private const float SandHeight = 20f;

		private VisualElement timerHUD;
		private Label timerLabel;
		private VisualElement topSand;
		private VisualElement bottomSand;

		private VisualElement scoreGoalHUD;
		private Label scoreTargetLabel;

		// per-team score progress bars; replaced with SVG graphics later
		private VisualElement[] scoreLines;

		private void OnEnable()
		{
			timerHUD = HUDElement.Require<VisualElement>(this, "timer-hud");
			timerLabel = HUDElement.Require<Label>(this, "timer-label");
			topSand = HUDElement.Require<VisualElement>(this, "top-sand");
			bottomSand = HUDElement.Require<VisualElement>(this, "bottom-sand");

			scoreGoalHUD = HUDElement.Require<VisualElement>(this, "score-hud");
			scoreTargetLabel = HUDElement.Require<Label>(this, "target-score-label");

			scoreLines = new VisualElement[Teams.NumTeams];
			scoreLines[1] = HUDElement.Require<VisualElement>(this, "red-score-line");
			scoreLines[2] = HUDElement.Require<VisualElement>(this, "blue-score-line");

			MatchReferee.StateChanged += OnMatchStateChange;
			MatchReferee.TeamScored += OnTeamScored;
			MatchReferee.TimerTextChanged += UpdateTimerText;
			OnMatchStateChange(MatchReferee.State);
		}

		private void OnDisable()
		{
			MatchReferee.StateChanged -= OnMatchStateChange;
			MatchReferee.TeamScored -= OnTeamScored;
			MatchReferee.TimerTextChanged -= UpdateTimerText;
		}

		private void Update()
		{
			bool playing = MatchReferee.State == MatchState.Playing;

			if (!playing) return;

			switch (MatchReferee.Settings.winCondition)
			{
				case WinCondition.Timer:
					UpdateTimerSand();
					break;

				case WinCondition.ReachScore:
					break;
			}
		}

		private void OnMatchStateChange(MatchState state)
		{
			bool show = state != MatchState.NotPlaying;

			if (show)
			{
				MatchSettings settings = MatchReferee.Settings;

				HUDElement.SetDisplayed(timerHUD, settings.winCondition == WinCondition.Timer);
				HUDElement.SetDisplayed(scoreGoalHUD, settings.winCondition == WinCondition.ReachScore);

				switch (settings.winCondition)
				{
					case WinCondition.Timer:
						UpdateTimerSand();
						break;

					case WinCondition.ReachScore:
						scoreTargetLabel.text = settings.scoreTarget.ToString();
						UpdateScoreLine(1);
						UpdateScoreLine(2);
						break;
				}
			}
			else
			{
				HUDElement.SetDisplayed(timerHUD, false);
				HUDElement.SetDisplayed(scoreGoalHUD, false);
			}
		}

		private void OnTeamScored(byte team, int points)
		{
			if (team == 0)
				return;

			UpdateScoreLine(team);
		}

		private void UpdateScoreLine(byte team)
		{
			short target = MatchReferee.Settings.scoreTarget;
			float progress = 0f;
			if (target > 0)
				progress = MatchReferee.GetTeamScore(team) / (float)target;

			scoreLines[team].style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
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

			topSand.style.height = SandHeight * tn;
			bottomSand.style.height = SandHeight * (1 - tn);
		}
	}
}
