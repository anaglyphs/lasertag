using System;
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
		private readonly RadioButtonGroup winByRadio;
		private readonly Slider roundTimeSlider;
		private readonly SliderInt scoreTargetSlider;
		private readonly Slider damageMultiplierSlider;
		private readonly SliderInt roundsSlider;
		private readonly Toggle infiniteRoundsToggle;
		private readonly RadioButtonGroup respawnConditionRadio;
		private readonly SliderInt respawnTimeSlider;
		private readonly Toggle spawnZombiesToggle;

		private readonly UIEventBindings bindings = new();

		private MatchSettings matchSettings = MatchSettings.DemoGame();

		public MatchSettings Settings => matchSettings;
		public Button StartButton { get; }

		public MatchSettingsBinder(VisualElement root)
		{
			winByRadio = Require<RadioButtonGroup>(root, "win-by-radio");
			roundTimeSlider = Require<Slider>(root, "round-time-slider");
			scoreTargetSlider = Require<SliderInt>(root, "score-target-slider");
			damageMultiplierSlider = Require<Slider>(root, "damage-multiplier-slider");
			roundsSlider = Require<SliderInt>(root, "rounds-slider");
			infiniteRoundsToggle = Require<Toggle>(root, "infinite-rounds-toggle");
			respawnConditionRadio = Require<RadioButtonGroup>(root, "respawn-condition-radio");
			respawnTimeSlider = Require<SliderInt>(root, "respawn-time-slider");
			spawnZombiesToggle = Require<Toggle>(root, "spawn-zombies-toggle");
			StartButton = Require<Button>(root, "start-button");

			Require<TextField>(roundTimeSlider, null).keyboardType = TouchScreenKeyboardType.DecimalPad;
			Require<TextField>(damageMultiplierSlider, null).keyboardType = TouchScreenKeyboardType.DecimalPad;
			Require<TextField>(scoreTargetSlider, null).keyboardType = TouchScreenKeyboardType.NumberPad;
			Require<TextField>(roundsSlider, null).keyboardType = TouchScreenKeyboardType.NumberPad;
			Require<TextField>(respawnTimeSlider, null).keyboardType = TouchScreenKeyboardType.NumberPad;

			// the UXML carries the menu's default slider values
			matchSettings.roundTimeSeconds = MinutesToSeconds(roundTimeSlider.value);
			matchSettings.scoreTarget = (short)Mathf.Clamp(scoreTargetSlider.value, 1, short.MaxValue);
			matchSettings.damageMultiplier = Mathf.Max(0f, damageMultiplierSlider.value);
			matchSettings.respawnSeconds = Mathf.Max(0, respawnTimeSlider.value);
			matchSettings.spawnZombies = spawnZombiesToggle.value;

			bindings.Value(roundTimeSlider, change =>
				matchSettings.roundTimeSeconds = MinutesToSeconds(change.newValue));

			bindings.Value(scoreTargetSlider, change =>
				matchSettings.scoreTarget = (short)Mathf.Clamp(change.newValue, 1, short.MaxValue));

			bindings.Value(damageMultiplierSlider, change =>
				matchSettings.damageMultiplier = Mathf.Max(0f, change.newValue));

			bindings.Value(roundsSlider, change => SetNumRounds());
			bindings.Value(infiniteRoundsToggle, change => SetNumRounds());
			infiniteRoundsToggle.SetValueWithoutNotify(matchSettings.HasInfiniteRounds());
			SetNumRounds();

			bindings.Value(respawnTimeSlider, change =>
				matchSettings.respawnSeconds = Mathf.Max(0, change.newValue));

			bindings.Value(spawnZombiesToggle, change =>
				matchSettings.spawnZombies = change.newValue);

			bindings.Value(winByRadio, change => SetWinBy(change.newValue));
			bool byScore = matchSettings.CheckWinByScore();
			int winChoice = matchSettings.CheckWinByTimer() && byScore ? 2 : byScore ? 1 : 0;
			winByRadio.SetValueWithoutNotify(winChoice);
			SetWinBy(winChoice);

			bindings.Value(respawnConditionRadio, change => SetRespawnCondition(change.newValue));
			respawnConditionRadio.SetValueWithoutNotify((int)matchSettings.respawnCondition);
			SetRespawnCondition((int)matchSettings.respawnCondition);
		}

		public void Dispose() => bindings.Dispose();

		// The referee reads zero rounds as "until somebody ends the match", so the
		// toggle owns the setting and the slider only feeds it when finite.
		private void SetNumRounds()
		{
			bool infinite = infiniteRoundsToggle.value;

			matchSettings.numRounds = infinite
				? (byte)0
				: (byte)Mathf.Clamp(roundsSlider.value, 1, byte.MaxValue);

			roundsSlider.SetEnabled(!infinite);
		}

		private static int MinutesToSeconds(float minutes)
		{
			return Mathf.RoundToInt(minutes * 60f);
		}

		// win-by-radio choices: 0 = Time, 1 = Points, 2 = Either
		private void SetWinBy(int choice)
		{
			matchSettings.winCondition = choice switch
			{
				0 => WinCondition.Timer,
				1 => WinCondition.ReachScore,
				_ => WinCondition.Timer | WinCondition.ReachScore,
			};

			roundTimeSlider.SetEnabled(matchSettings.CheckWinByTimer());
			scoreTargetSlider.SetEnabled(matchSettings.CheckWinByScore());
		}

		// respawn-radio choices follow the RespawnCondition enum order
		private void SetRespawnCondition(int choice)
		{
			matchSettings.respawnCondition =
				(RespawnCondition)Mathf.Clamp(choice, 0, (int)RespawnCondition.NextRound);
		}
	}
}
