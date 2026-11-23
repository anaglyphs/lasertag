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

		private static readonly float[] RoundMinuteChoices = { 1, 2, 3, 5, 7, 10 };
		private static readonly float[] DamageMultiplierChoices = { 0.5f, 1f, 2f };
		private static readonly List<RespawnCondition> RespawnChoices = new() { RespawnCondition.InBases, RespawnCondition.NextRound };
		private const int MinScoreTarget = 1, MaxScoreTarget = 20;
		private const int MinRounds = 1, MaxRounds = 5;

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
			endField = Require<DetentField>(root, "end-field");
			roundTimeField = Require<DetentField>(root, "round-time-field");
			scoreTargetField = Require<DetentField>(root, "score-target-field");
			roundsField = Require<DetentField>(root, "rounds-field");
			damageField = Require<DetentField>(root, "damage-field");
			respawnField = Require<DetentField>(root, "respawn-field");
			spawnZombiesToggle = Require<Toggle>(root, "spawn-zombies-toggle");
			StartButton = Require<Button>(root, "start-button");

			roundTimeField.SetSegmentWeights(MinutesAddedByEachChoice());
			roundTimeField.FormatValue = choice => $"{(int)RoundMinuteChoices[ClampChoice(choice, RoundMinuteChoices.Length)]}:00";
			scoreTargetField.TickCount = MaxScoreTarget - MinScoreTarget + 1;
			scoreTargetField.FormatValue = choice => (choice + MinScoreTarget).ToString();
			roundsField.SetSegmentWeights(EqualWeights(MaxRounds - MinRounds + 1));
			roundsField.FormatValue = choice => (choice + MinRounds).ToString();

			// Winning by both time and score shows as score.
			endField.SetValueWithoutNotify(matchSettings.CheckWinByScore() ? EndScoreChoice : EndTimedChoice);
			roundTimeField.SetValueWithoutNotify(NearestChoice(RoundMinuteChoices, matchSettings.roundTimeSeconds / 60f));
			scoreTargetField.SetValueWithoutNotify(Mathf.Clamp(matchSettings.scoreTarget, MinScoreTarget, MaxScoreTarget) - MinScoreTarget);
			roundsField.SetValueWithoutNotify(Mathf.Clamp(matchSettings.GetNumRounds(), MinRounds, MaxRounds) - MinRounds);
			damageField.SetValueWithoutNotify(NearestChoice(DamageMultiplierChoices, matchSettings.damageMultiplier));
			// A timed respawn shows as at base.
			respawnField.SetValueWithoutNotify(Mathf.Max(0, RespawnChoices.IndexOf(matchSettings.respawnCondition)));
			spawnZombiesToggle.SetValueWithoutNotify(matchSettings.spawnZombies);

			SetEnd(endField.value);
			SetRoundMinutes(roundTimeField.value);
			SetScoreTarget(scoreTargetField.value);
			SetRounds(roundsField.value);
			SetDamage(damageField.value);
			SetRespawnCondition(respawnField.value);

			bindings.Value(endField, change => SetEnd(change.newValue));
			bindings.Value(roundTimeField, change => SetRoundMinutes(change.newValue));
			bindings.Value(scoreTargetField, change => SetScoreTarget(change.newValue));
			bindings.Value(roundsField, change => SetRounds(change.newValue));
			bindings.Value(damageField, change => SetDamage(change.newValue));
			bindings.Value(respawnField, change => SetRespawnCondition(change.newValue));
			bindings.Value(spawnZombiesToggle, change => matchSettings.spawnZombies = change.newValue);
		}

		public void Dispose() => bindings.Dispose();

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

		private void SetRounds(int choice) =>
			matchSettings.numRounds = (byte)Mathf.Clamp(choice + MinRounds, MinRounds, MaxRounds);

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

		private static float[] EqualWeights(int count)
		{
			var weights = new float[count];
			Array.Fill(weights, 1f);
			return weights;
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
