using System;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	[DefaultExecutionOrder(100)]
	public class GameMenu : MonoBehaviour
	{
		private NavView navView;
		private NavPage homePage;
		private NavPage matchPage;
		private NavPage playingPage;
		private NavPage mapManagerPage;
		private NavPage editingMapPage;

		private RadioButtonGroup winByRadio;
		private Slider roundTimeSlider;
		private SliderInt scoreTargetSlider;
		private Slider damageMultiplierSlider;
		private SliderInt roundsSlider;
		private RadioButtonGroup respawnConditionRadio;
		private SliderInt respawnTimeSlider;
		private Toggle spawnZombiesToggle;
		private Button startButton;

		private MatchSettings matchSettings = MatchSettings.DemoGame();

		private MatchReferee Referee => MatchReferee.Instance;

		private void InitializeUI()
		{
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"GameMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			navView = new NavView(Require<VisualElement>(root, "pages"));
			homePage = navView.AddPage("home-page", false);
			matchPage = navView.AddPage("match-page");
			mapManagerPage = navView.AddPage("map-manager-page");
			playingPage = navView.AddPage("playing-page", false);
			editingMapPage = navView.AddPage("editing-map-page", false);

			Require<Button>(root, "play-match-button").clicked += matchPage.NavigateHere;
			Require<Button>(root, "manage-map-button").clicked += mapManagerPage.NavigateHere;

			winByRadio = Require<RadioButtonGroup>(root, "win-by-radio");
			roundTimeSlider = Require<Slider>(root, "round-time-slider");
			scoreTargetSlider = Require<SliderInt>(root, "score-target-slider");
			damageMultiplierSlider = Require<Slider>(root, "damage-multiplier-slider");
			roundsSlider = Require<SliderInt>(root, "rounds-slider");
			respawnConditionRadio = Require<RadioButtonGroup>(root, "respawn-condition-radio");
			respawnTimeSlider = Require<SliderInt>(root, "respawn-time-slider");
			spawnZombiesToggle = Require<Toggle>(root, "spawn-zombies-toggle");
			startButton = Require<Button>(root, "start-button");

			// the UXML carries the menu's default slider values
			matchSettings.roundTimeSeconds = MinutesToSeconds(roundTimeSlider.value);
			matchSettings.scoreTarget = (short)Mathf.Clamp(scoreTargetSlider.value, 1, short.MaxValue);
			matchSettings.damageMultiplier = Mathf.Max(0f, damageMultiplierSlider.value);
			matchSettings.numRounds = (byte)Mathf.Clamp(roundsSlider.value, 1, byte.MaxValue);
			matchSettings.respawnSeconds = Mathf.Max(0, respawnTimeSlider.value);
			matchSettings.spawnZombies = spawnZombiesToggle.value;

			roundTimeSlider.RegisterValueChangedCallback(change =>
				matchSettings.roundTimeSeconds = MinutesToSeconds(change.newValue));

			scoreTargetSlider.RegisterValueChangedCallback(change =>
				matchSettings.scoreTarget = (short)Mathf.Clamp(change.newValue, 1, short.MaxValue));

			damageMultiplierSlider.RegisterValueChangedCallback(change =>
				matchSettings.damageMultiplier = Mathf.Max(0f, change.newValue));

			roundsSlider.RegisterValueChangedCallback(change =>
				matchSettings.numRounds = (byte)Mathf.Clamp(change.newValue, 1, byte.MaxValue));

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

			startButton.clicked += () => Referee?.QueueMatch(matchSettings);
			Require<Button>(root, "stop-button").clicked += () => Referee?.EndMatch();
			Require<Button>(root, "edit-map-button").clicked +=
				() => MapEditor.MapEditor.SetActive(true);
			Require<Button>(root, "finish-editing-button").clicked +=
				() => MapEditor.MapEditor.SetActive(false);

			navView.Start(homePage);
		}

		private static int MinutesToSeconds(float minutes)
		{
			return Mathf.RoundToInt(minutes * 60f);
		}

		private void OnEnable()
		{
			InitializeUI();

			MatchReferee.StateChanged += OnMatchStateChanged;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			MapEditor.MapEditor.ActiveChanged += OnMapEditorStateChanged;

			OnMatchStateChanged(MatchReferee.State);
			OnNetcodeStateChanged(NetcodeManagement.State);
			OnMapEditorStateChanged(MapEditor.MapEditor.IsActive);
		}

		private void OnDisable()
		{
			MatchReferee.StateChanged -= OnMatchStateChanged;
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MapEditor.MapEditor.ActiveChanged -= OnMapEditorStateChanged;
			navView?.Dispose();
			navView = null;
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

		private void OnMapEditorStateChanged(bool active)
		{
			navView.SetModalPresented(editingMapPage, active, 10);
		}

		private void OnMatchStateChanged(MatchState state)
		{
			bool playing = state != MatchState.NotPlaying;
			navView.SetModalPresented(playingPage, playing, 20);

			if (playing)
				MapEditor.MapEditor.SetActive(false);
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			startButton.SetEnabled(state == NetcodeState.Connected);
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
