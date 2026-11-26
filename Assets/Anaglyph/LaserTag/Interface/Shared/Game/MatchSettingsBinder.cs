using System;
using System.Collections.Generic;
using Anaglyph.Menu;
using Anaglyph.LaserTag.Matches;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// Binds the controls of GameMatchSettingsPage to a <see cref="MatchSettings"/> value.
	/// Shared by the headset menu and the desktop operator panel.
	/// </summary>
	public sealed class MatchSettingsBinder : IDisposable
	{
		private const int EndTimedChoice = 0;
		private const int EndScoreChoice = 1;
		private const int PointsForKillsChoice = 0;
		private const int PointsForFlagCapturesChoice = 1;

		private static readonly float[] RoundMinuteChoices = { 1, 2, 3, 5, 7, 10 };
		private static readonly float[] DamageMultiplierChoices = { 0.5f, 1f, 2f };
		private static readonly List<RespawnCondition> RespawnChoices = new() { RespawnCondition.NextRound, RespawnCondition.InBases };
		private const int MinScoreTarget = 1, MaxScoreTarget = 20;
		private const int MinRounds = 1, MaxRounds = 5;
		private const int InfiniteRoundsChoice = MaxRounds - MinRounds + 1;

		private readonly DetentField pointsField;
		private readonly DetentField endField;
		private readonly DetentField roundTimeField;
		private readonly DetentField scoreTargetField;
		private readonly DetentField roundsField;
		private readonly DetentField damageField;
		private readonly DetentField respawnField;
		private readonly Toggle spawnZombiesToggle;

		private readonly UIEventBindings bindings = new();

		private MatchSettings matchSettings = MatchSettings.DemoGame();

		public MatchSettings Settings => matchSettings;
		public Button StartButton { get; }

		public MatchSettingsBinder(VisualElement root)
		{
			pointsField = Require<DetentField>(root, "points-field");
			endField = Require<DetentField>(root, "end-field");
			roundTimeField = Require<DetentField>(root, "round-time-field");
			scoreTargetField = Require<DetentField>(root, "score-target-field");
			roundsField = Require<DetentField>(root, "rounds-field");
			damageField = Require<DetentField>(root, "damage-field");
			respawnField = Require<DetentField>(root, "respawn-field");
			spawnZombiesToggle = Require<Toggle>(root, "spawn-zombies-toggle");
			StartButton = Require<Button>(root, "start-button");

			roundTimeField.SetSegments(MinutesAddedByEachChoice());
			roundTimeField.FormatValue = choice => $"{(int)RoundMinuteChoices[ClampChoice(choice, RoundMinuteChoices.Length)]}:00";
			scoreTargetField.SetTicks(MaxScoreTarget - MinScoreTarget + 1);
			scoreTargetField.FormatValue = choice => (choice + MinScoreTarget).ToString();
			roundsField.SetSegments(InfiniteRoundsChoice + 1);
			roundsField.AddMarkClass(InfiniteRoundsChoice, "match-segment--arrow");
			FormatRounds();
			MenuCopy.Changed += FormatRounds;

			pointsField.SetValueWithoutNotify(matchSettings.pointsPerFlagCapture > 0 ? PointsForFlagCapturesChoice : PointsForKillsChoice);
			// Winning by both time and score shows as score.
			endField.SetValueWithoutNotify(matchSettings.CheckWinByScore() ? EndScoreChoice : EndTimedChoice);
			roundTimeField.SetValueWithoutNotify(NearestChoice(RoundMinuteChoices, matchSettings.roundTimeSeconds / 60f));
			scoreTargetField.SetValueWithoutNotify(Mathf.Clamp(matchSettings.scoreTarget, MinScoreTarget, MaxScoreTarget) - MinScoreTarget);
			roundsField.SetValueWithoutNotify(matchSettings.HasInfiniteRounds()
				? InfiniteRoundsChoice
				: Mathf.Clamp(matchSettings.GetNumRounds(), MinRounds, MaxRounds) - MinRounds);
			damageField.SetValueWithoutNotify(NearestChoice(DamageMultiplierChoices, matchSettings.damageMultiplier));
			// A timed respawn shows as at base.
			RespawnCondition shownRespawn = matchSettings.respawnCondition == RespawnCondition.Timer ? RespawnCondition.InBases : matchSettings.respawnCondition;
			respawnField.SetValueWithoutNotify(RespawnChoices.IndexOf(shownRespawn));
			spawnZombiesToggle.SetValueWithoutNotify(matchSettings.spawnZombies);

			SetPoints(pointsField.value);
			SetEnd(endField.value);
			SetRoundMinutes(roundTimeField.value);
			SetScoreTarget(scoreTargetField.value);
			SetRounds(roundsField.value);
			SetDamage(damageField.value);
			SetRespawnCondition(respawnField.value);

			bindings.Value(pointsField, change => SetPoints(change.newValue));
			bindings.Value(endField, change =>
			{
				SetEnd(change.newValue);
				PressClickable.Flash(change.newValue == EndTimedChoice ? roundTimeField : scoreTargetField);
			});
			bindings.Value(roundTimeField, change => SetRoundMinutes(change.newValue));
			bindings.Value(scoreTargetField, change => SetScoreTarget(change.newValue));
			bindings.Value(roundsField, change => SetRounds(change.newValue));
			bindings.Value(damageField, change => SetDamage(change.newValue));
			bindings.Value(respawnField, change => SetRespawnCondition(change.newValue));
			bindings.Value(spawnZombiesToggle, change => matchSettings.spawnZombies = change.newValue);
		}

		public void Dispose()
		{
			bindings.Dispose();
			MenuCopy.Changed -= FormatRounds;
		}

		// "Endless" follows the language, so it is looked up again when the language changes
		private void FormatRounds()
		{
			string endless = MenuCopy.Get("GameMenu", "GameMatchSettingsPage.rounds-field.endless");
			roundsField.FormatValue = choice => choice >= InfiniteRoundsChoice ? endless : (choice + MinRounds).ToString();
		}

		private void SetPoints(int choice)
		{
			bool flagCapturesEarnPoints = choice == PointsForFlagCapturesChoice;
			matchSettings.pointsPerKill = (byte)(flagCapturesEarnPoints ? 0 : 1);
			matchSettings.pointsPerFlagCapture = (byte)(flagCapturesEarnPoints ? 1 : 0);
		}

		private void SetEnd(int choice)
		{
			bool timed = choice == EndTimedChoice;
			matchSettings.winCondition = timed ? WinCondition.Timer : WinCondition.ReachScore;
			SetShown(roundTimeField, timed);
			SetShown(scoreTargetField, !timed);
		}

		private void SetRoundMinutes(int choice) =>
			matchSettings.roundTimeSeconds = Mathf.RoundToInt(RoundMinuteChoices[ClampChoice(choice, RoundMinuteChoices.Length)] * 60);

		private void SetScoreTarget(int choice) =>
			matchSettings.scoreTarget = (short)Mathf.Clamp(choice + MinScoreTarget, MinScoreTarget, MaxScoreTarget);

		// Zero rounds means the match runs until somebody ends it.
		private void SetRounds(int choice) =>
			matchSettings.numRounds = choice >= InfiniteRoundsChoice
				? (byte)0
				: (byte)Mathf.Clamp(choice + MinRounds, MinRounds, MaxRounds);

		private void SetDamage(int choice) =>
			matchSettings.damageMultiplier = DamageMultiplierChoices[ClampChoice(choice, DamageMultiplierChoices.Length)];

		private void SetRespawnCondition(int choice) =>
			matchSettings.respawnCondition = RespawnChoices[ClampChoice(choice, RespawnChoices.Count)];

		private static void SetShown(VisualElement element, bool shown) =>
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

		private static float[] MinutesAddedByEachChoice()
		{
			var added = new float[RoundMinuteChoices.Length];
			for (int i = 0; i < added.Length; i++)
				added[i] = RoundMinuteChoices[i] - (i == 0 ? 0 : RoundMinuteChoices[i - 1]);
			return added;
		}

		private static int ClampChoice(int choice, int count) => Mathf.Clamp(choice, 0, count - 1);

		private static int NearestChoice(float[] choices, float wanted)
		{
			int nearest = 0;
			for (int i = 1; i < choices.Length; i++)
				if (Mathf.Abs(choices[i] - wanted) < Mathf.Abs(choices[nearest] - wanted))
					nearest = i;
			return nearest;
		}
	}
}
