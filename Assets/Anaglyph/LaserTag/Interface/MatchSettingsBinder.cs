using System;
using Anaglyph.LaserTag.Matches;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// Binds the controls of GameMatchPage to a <see cref="MatchSettings"/> value.
	/// Shared by the headset menu and the desktop operator panel.
	/// </summary>
	public sealed class MatchSettingsBinder
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

			// the UXML carries the menu's default slider values
			matchSettings.roundTimeSeconds = MinutesToSeconds(roundTimeSlider.value);
			matchSettings.scoreTarget = (short)Mathf.Clamp(scoreTargetSlider.value, 1, short.MaxValue);
			matchSettings.damageMultiplier = Mathf.Max(0f, damageMultiplierSlider.value);
			matchSettings.respawnSeconds = Mathf.Max(0, respawnTimeSlider.value);
			matchSettings.spawnZombies = spawnZombiesToggle.value;

			roundTimeSlider.RegisterValueChangedCallback(change =>
				matchSettings.roundTimeSeconds = MinutesToSeconds(change.newValue));

			scoreTargetSlider.RegisterValueChangedCallback(change =>
				matchSettings.scoreTarget = (short)Mathf.Clamp(change.newValue, 1, short.MaxValue));

			damageMultiplierSlider.RegisterValueChangedCallback(change =>
				matchSettings.damageMultiplier = Mathf.Max(0f, change.newValue));

			roundsSlider.RegisterValueChangedCallback(change => SetNumRounds());
			infiniteRoundsToggle.RegisterValueChangedCallback(change => SetNumRounds());
			infiniteRoundsToggle.SetValueWithoutNotify(matchSettings.HasInfiniteRounds());
			SetNumRounds();

			respawnTimeSlider.RegisterValueChangedCallback(change =>
				matchSettings.respawnSeconds = Mathf.Max(0, change.newValue));

			spawnZombiesToggle.RegisterValueChangedCallback(change =>
				matchSettings.spawnZombies = change.newValue);

			winByRadio.RegisterValueChangedCallback(change => SetWinBy(change.newValue));
			bool byScore = matchSettings.CheckWinByScore();
			int winChoice = matchSettings.CheckWinByTimer() && byScore ? 2 : byScore ? 1 : 0;
			winByRadio.SetValueWithoutNotify(winChoice);
			SetWinBy(winChoice);

			respawnConditionRadio.RegisterValueChangedCallback(change => SetRespawnCondition(change.newValue));
			respawnConditionRadio.SetValueWithoutNotify((int)matchSettings.respawnCondition);
			SetRespawnCondition((int)matchSettings.respawnCondition);
		}

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

			SetDisplayed(roundTimeSlider, matchSettings.CheckWinByTimer());
			SetDisplayed(scoreTargetSlider, matchSettings.CheckWinByScore());
		}

		// respawn-radio choices follow the RespawnCondition enum order
		private void SetRespawnCondition(int choice)
		{
			matchSettings.respawnCondition =
				(RespawnCondition)Mathf.Clamp(choice, 0, (int)RespawnCondition.NextRound);

			SetDisplayed(respawnTimeSlider,
				matchSettings.respawnCondition == RespawnCondition.Timer);
		}

		private static void SetDisplayed(VisualElement element, bool displayed)
		{
			element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private static T Require<T>(VisualElement root, string name)
			where T : VisualElement
		{
			T element = root.Q<T>(name);
			if (element == null)
				throw new InvalidOperationException(
					$"Required UI Toolkit element '{name}' ({typeof(T).Name}) was not found.");

			return element;
		}
	}
}
