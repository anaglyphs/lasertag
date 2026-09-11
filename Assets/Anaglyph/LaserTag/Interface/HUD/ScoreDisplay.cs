using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player.Teams;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface.HUD
{
	/// <summary>
	/// Shows how the current round is going, in whichever mode the win condition
	/// calls for. Contents are laid out against a fixed design box and scaled to
	/// fit whatever box the element is given, so the same element reads correctly
	/// on a hand panel and in a menu page.
	/// </summary>
	[UxmlElement]
	public sealed partial class ScoreDisplay : VisualElement
	{
		/// <summary>
		/// Both modes and the round label are positioned against this box in
		/// ScoreDisplay.uss. The two must be changed together.
		/// </summary>
		private const float DesignWidth = 200f;
		private const float DesignHeight = 130f;

		public static readonly string ussClassName = "score-display";
		public static readonly string contentUssClassName = "score-display__content";
		public static readonly string modeUssClassName = "score-display__mode";
		public static readonly string scoreModeUssClassName = "score-display__mode--score";
		public static readonly string timerModeUssClassName = "score-display__mode--timer";
		public static readonly string goalScoreUssClassName = "score-display__goal-score";
		public static readonly string goalTargetUssClassName = "score-display__goal-target";
		public static readonly string timerScoreUssClassName = "score-display__timer-score";
		public static readonly string timerLabelUssClassName = "score-display__timer-label";
		public static readonly string roundUssClassName = "score-display__round";
		public static readonly string redModifierSuffix = "--red";
		public static readonly string blueModifierSuffix = "--blue";

		private readonly VisualElement content = new();
		private readonly VisualElement scoreMode = new();
		private readonly VisualElement timerMode = new();

		private readonly Label goalTargetLabel = new();
		private readonly Label timerLabel = new();
		private readonly Label roundLabel = new();

		private readonly Label[] goalScores = new Label[Teams.NumTeams];
		private readonly Label[] timerScores = new Label[Teams.NumTeams];

		private readonly int[] shownGoalScores = new int[Teams.NumTeams];
		private readonly int[] shownTimerScores = new int[Teams.NumTeams];
		private int shownScoreTarget;
		private int shownRound;
		private int shownNumRounds;

		private readonly IVisualElementScheduledItem refreshTick;

		public ScoreDisplay()
		{
			AddToClassList(ussClassName);
			pickingMode = PickingMode.Ignore;

			content.AddToClassList(contentUssClassName);
			content.pickingMode = PickingMode.Ignore;

			BuildScoreMode();
			BuildTimerMode();

			roundLabel.AddToClassList(roundUssClassName);
			roundLabel.pickingMode = PickingMode.Ignore;

			content.Add(scoreMode);
			content.Add(timerMode);
			content.Add(roundLabel);
			hierarchy.Add(content);

			InvalidateShownValues();

			refreshTick = schedule.Execute(Refresh).Every(0);
			refreshTick.Pause();

			RegisterCallback<GeometryChangedEvent>(FitContentToBox);
			RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
			RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
		}

		private void BuildScoreMode()
		{
			scoreMode.AddToClassList(modeUssClassName);
			scoreMode.AddToClassList(scoreModeUssClassName);
			scoreMode.pickingMode = PickingMode.Ignore;

			goalScores[Teams.Red] = AddTeamLabel(scoreMode, goalScoreUssClassName, Teams.Red);
			goalScores[Teams.Blue] = AddTeamLabel(scoreMode, goalScoreUssClassName, Teams.Blue);

			AddLabel(scoreMode, goalTargetLabel, goalTargetUssClassName);
		}

		private void BuildTimerMode()
		{
			timerMode.AddToClassList(modeUssClassName);
			timerMode.AddToClassList(timerModeUssClassName);
			timerMode.pickingMode = PickingMode.Ignore;

			timerScores[Teams.Red] = AddTeamLabel(timerMode, timerScoreUssClassName, Teams.Red);
			timerScores[Teams.Blue] = AddTeamLabel(timerMode, timerScoreUssClassName, Teams.Blue);

			AddLabel(timerMode, timerLabel, timerLabelUssClassName);
		}

		// Team colour comes from Teams so the display never drifts from the
		// colours the rest of the game paints players and bases with.
		private static Label AddTeamLabel(VisualElement mode, string ussClassName, byte team)
		{
			string modifier = team == Teams.Red ? redModifierSuffix : blueModifierSuffix;

			Label label = AddLabel(mode, new Label(), ussClassName);
			label.AddToClassList(ussClassName + modifier);
			label.style.color = Teams.Colors[team];

			return label;
		}

		private static Label AddLabel(VisualElement parent, Label label, string ussClassName)
		{
			label.AddToClassList(ussClassName);
			label.pickingMode = PickingMode.Ignore;
			parent.Add(label);

			return label;
		}

		// Scale is a transform, so writing it here never feeds back into layout.
		private void FitContentToBox(GeometryChangedEvent evt)
		{
			// the event bubbles, so a child resizing would otherwise fit to the
			// child's rect
			if (evt.target != this) return;

			float width = evt.newRect.width;
			float height = evt.newRect.height;
			if (width <= 0f || height <= 0f) return;

			float fit = Mathf.Min(width / DesignWidth, height / DesignHeight);
			content.style.scale = new Scale(Vector2.one * fit);
		}

		private void OnAttachToPanel(AttachToPanelEvent evt)
		{
			InvalidateShownValues();
			MatchReferee.TimerTextChanged += SetTimerText;
			MenuCopy.Changed += InvalidateShownValues;
			refreshTick.Resume();
		}

		private void OnDetachFromPanel(DetachFromPanelEvent evt)
		{
			MatchReferee.TimerTextChanged -= SetTimerText;
			MenuCopy.Changed -= InvalidateShownValues;
			refreshTick.Pause();
		}

		private void Refresh()
		{
			MatchSettings settings = MatchReferee.Settings;
			bool playing = MatchReferee.State != MatchState.NotPlaying;

			// both modes fill the design box, so only one may be up; the timer
			// wins when a mode is won by time and score together
			bool showTimer = playing && settings.CheckWinByTimer();
			bool showScoreGoal = playing && !showTimer && settings.CheckWinByScore();

			HUDElement.SetDisplayed(timerMode, showTimer);
			HUDElement.SetDisplayed(scoreMode, showScoreGoal);

			if (showTimer)
			{
				for (byte team = Teams.Red; team < Teams.NumTeams; team++)
					SetInt(timerScores[team], ref shownTimerScores[team],
						MatchReferee.GetTeamScore(team));
			}

			if (showScoreGoal)
			{
				SetInt(goalTargetLabel, ref shownScoreTarget, settings.scoreTarget);

				for (byte team = Teams.Red; team < Teams.NumTeams; team++)
					SetInt(goalScores[team], ref shownGoalScores[team],
						MatchReferee.GetTeamScore(team));
			}

			RefreshRoundLabel(playing, settings.GetNumRounds());
		}

		private void RefreshRoundLabel(bool playing, int numRounds)
		{
			bool show = playing && numRounds > 1;
			HUDElement.SetDisplayed(roundLabel, show);

			if (!show) return;

			int round = MatchReferee.CurrentRound;
			if (round == shownRound && numRounds == shownNumRounds) return;

			shownRound = round;
			shownNumRounds = numRounds;
			roundLabel.text = MenuCopy.Format("HUD", "match.round", round, numRounds);
		}

		private void SetTimerText(string timerString)
		{
			timerLabel.text = timerString;
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
			shownNumRounds = 0;
		}

		private static void SetInt(Label label, ref int shown, int value)
		{
			if (shown == value) return;

			shown = value;
			label.text = value.ToString();
		}
	}
}
