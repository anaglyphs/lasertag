using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(9999)]
	public class GameHUD : MonoBehaviour
	{
		[Header("Timer")] [SerializeField] private GameObject timerHUD;

		[SerializeField] private Text timerLabel;

		[SerializeField] private RectTransform topSand;
		[SerializeField] private RectTransform bottomSand;
		private float sandHeight;

		[Header("Score goal")] [SerializeField]
		private GameObject scoreGoalHUD;

		[SerializeField] private Text scoreTargetLabel;
		[SerializeField] private LineGraphic scoreLine1;
		[SerializeField] private LineGraphic scoreLine2;
		private ScoreLine[] scoreLines;

		private struct ScoreLine
		{
			public LineGraphic line;
			public Vector2 start;
			public Vector2 end;

			public ScoreLine(LineGraphic line)
			{
				this.line = line;
				start = line.points[^2];
				end = line.points[^1];
			}

			public void Update(int score)
			{
				short target = MatchReferee.Settings.scoreTarget;
				float progress = 0f;
				if (target > 0)
					progress = score / (float)target;
				Vector2 v = Vector2.Lerp(start, end, progress);
				line.points[^1] = v;
				line.SetVerticesDirty();
			}
		}

		private void Awake()
		{
			MatchReferee.StateChanged += OnMatchStateChange;
			MatchReferee.TeamScored += OnTeamScored;
			MatchReferee.TimerTextChanged += UpdateTimerText;
			OnMatchStateChange(MatchReferee.State);

			sandHeight = topSand.sizeDelta.y;

			scoreLines = new ScoreLine[Teams.NumTeams];
			scoreLines[1] = new ScoreLine(scoreLine1);
			scoreLines[2] = new ScoreLine(scoreLine2);
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= OnMatchStateChange;
			MatchReferee.TeamScored -= OnTeamScored;
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

			gameObject.SetActive(show);

			if (show)
			{
				MatchSettings settings = MatchReferee.Settings;

				timerHUD.SetActive(settings.winCondition == WinCondition.Timer);
				scoreGoalHUD.SetActive(settings.winCondition == WinCondition.ReachScore);

				switch (settings.winCondition)
				{
					case WinCondition.Timer:
						UpdateTimerSand();
						break;

					case WinCondition.ReachScore:
						scoreTargetLabel.text = settings.scoreTarget.ToString();
						break;
				}
			}
			else
			{
				timerHUD.SetActive(false);
				scoreGoalHUD.SetActive(false);
			}
		}

		private void OnTeamScored(byte team, int points)
		{
			if (team == 0)
				return;

			int score = MatchReferee.GetTeamScore(team);
			scoreLines[team].Update(score);
		}

		private void UpdateTimerText(string timerString)
		{
			timerLabel.text = timerString;
		}

		private void UpdateTimerSand()
		{
			int timeTotal = MatchReferee.Settings.timerSeconds;
			float timeLeft = MatchReferee.Instance.GetTimeLeft();

			float sh = sandHeight;
			float tn = timeLeft / timeTotal;

			SetRectHeight(topSand, sh * tn);
			SetRectHeight(bottomSand, sh * (1 - tn));
		}

		private static void SetRectHeight(RectTransform rt, float height)
		{
			Vector2 v = rt.sizeDelta;
			v.y = height;
			rt.sizeDelta = v;
		}
	}
}