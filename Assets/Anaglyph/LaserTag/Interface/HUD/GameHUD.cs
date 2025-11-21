using System;
using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(9999)]
	public class GameHUD : MonoBehaviour
	{
		[Header("Timer")] [SerializeField] private GameObject timerHUD;

		[SerializeField] private Text timerLabel;

		[FormerlySerializedAs("timerSandTop")] [SerializeField]
		private RectTransform topSand;

		[FormerlySerializedAs("timerSandBottom")] [SerializeField]
		private RectTransform bottomSand;

		private float sandHeight;

		[Header("Score goal")] [SerializeField]
		private GameObject scoreGoalHUD;

		[SerializeField] private Text scoreTargetLabel;
		[SerializeField] private Line scoreLine1;
		[SerializeField] private Line scoreLine2;
		private ScoreLine[] scoreLines;

		private struct ScoreLine
		{
			public Line line;
			public Vector2 start;
			public Vector2 end;
			public byte team;

			public ScoreLine(Line line, byte team)
			{
				this.line = line;
				start = line.points[^2];
				end = line.points[^1];
				this.team = team;
			}

			public void Update()
			{
				var score = MatchReferee.GetTeamScore(team);
				var target = MatchReferee.Settings.scoreTarget;
				var progress = target > 0 ? score / (float)team : 0;
				var v = Vector2.Lerp(start, end, progress);
				line.points[^1] = v;
				line.PositionVertices();
			}
		}

		private void Awake()
		{
			MatchReferee.StateChanged += OnMatchStateChange;
			MatchReferee.TeamScored += OnTeamScored;
			OnMatchStateChange(MatchReferee.State);

			sandHeight = topSand.rect.height;

			scoreLines = new ScoreLine[Teams.NumTeams];
			scoreLines[1] = new ScoreLine(scoreLine1, 1);
			scoreLines[2] = new ScoreLine(scoreLine2, 1);
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= OnMatchStateChange;
			MatchReferee.TeamScored -= OnTeamScored;
		}

		private void Update()
		{
			var playing = MatchReferee.State == MatchState.Playing;

			if (!playing) return;

			switch (MatchReferee.Settings.winCondition)
			{
				case WinCondition.Timer:
					UpdateTimerText();
					UpdateTimerSand();
					break;

				case WinCondition.ReachScore:
					break;
			}
		}

		private void OnMatchStateChange(MatchState state)
		{
			var show = state != MatchState.NotPlaying;

			gameObject.SetActive(show);

			if (show)
			{
				var settings = MatchReferee.Settings;

				timerHUD.SetActive(settings.winCondition == WinCondition.Timer);
				scoreGoalHUD.SetActive(settings.winCondition == WinCondition.ReachScore);

				switch (settings.winCondition)
				{
					case WinCondition.Timer:
						UpdateTimerText();
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

			scoreLines[team].Update();
		}

		private void UpdateTimerText()
		{
			float seconds = 0;
			var matchRef = MatchReferee.Instance;
			if (matchRef)
			{
				if (MatchReferee.State == MatchState.Playing)
					seconds = matchRef.GetTimeLeft();
				else
					seconds = MatchReferee.Settings.timerSeconds;
			}

			var time = TimeSpan.FromSeconds(seconds);
			timerLabel.text = time.ToString(@"m\:ss");
		}

		private void UpdateTimerSand()
		{
			var timeTotal = MatchReferee.Settings.timerSeconds;
			var timeLeft = MatchReferee.Instance.GetTimeLeft();
			var timeNorm = timeLeft / timeTotal;

			var y = sandHeight * timeNorm;
			var m = topSand.anchorMax;
			topSand.anchorMax.Set(m.x, y);

			y = sandHeight * (1 - timeNorm);
			m = bottomSand.anchorMax;
			topSand.anchorMax.Set(m.x, y);
		}

		private void UpdateScoreLines()
		{
		}
	}
}