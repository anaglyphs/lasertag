using Anaglyph.Menu.UIToolkit;
using Anaglyph.Netcode;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(100)]
	public class GameMenu : MonoBehaviour
	{
		private UIToolkitNavPages navView;
		private UIToolkitNavPage homePage;
		private UIToolkitNavPage matchPage;
		private UIToolkitNavPage playingPage;
		private UIToolkitNavPage mapManagerPage;
		private UIToolkitNavPage editingMapPage;

		private RadioButtonGroup winByRadio;
		private FloatField roundTimeField;
		private IntegerField scoreTargetField;
		private FloatField damageMultiplierField;
		private IntegerField roundsField;
		private RadioButtonGroup respawnConditionRadio;
		private FloatField respawnTimeField;
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

			navView = new UIToolkitNavPages(Require<VisualElement>(root, "pages"));
			homePage = navView.AddPage("home-page", false);
			matchPage = navView.AddPage("match-page");
			mapManagerPage = navView.AddPage("map-manager-page");
			playingPage = navView.AddPage("playing-page", false);
			editingMapPage = navView.AddPage("editing-map-page", false);

			Require<Button>(root, "play-match-button").clicked += matchPage.NavigateHere;
			Require<Button>(root, "manage-map-button").clicked += mapManagerPage.NavigateHere;

			winByRadio = Require<RadioButtonGroup>(root, "win-by-radio");
			roundTimeField = Require<FloatField>(root, "round-time-field");
			scoreTargetField = Require<IntegerField>(root, "score-target-field");
			damageMultiplierField = Require<FloatField>(root, "damage-multiplier-field");
			roundsField = Require<IntegerField>(root, "rounds-field");
			respawnConditionRadio = Require<RadioButtonGroup>(root, "respawn-condition-radio");
			respawnTimeField = Require<FloatField>(root, "respawn-time-field");
			startButton = Require<Button>(root, "start-button");

			// the UXML carries the menu's default field values
			matchSettings.roundTimeSeconds = MinutesToSeconds(roundTimeField.value);
			matchSettings.scoreTarget = (short)Mathf.Clamp(scoreTargetField.value, 1, short.MaxValue);
			matchSettings.damageMultiplier = Mathf.Max(0f, damageMultiplierField.value);
			matchSettings.numRounds = (byte)Mathf.Clamp(roundsField.value, 1, byte.MaxValue);
			matchSettings.respawnSeconds = Mathf.Max(0f, respawnTimeField.value);

			roundTimeField.RegisterValueChangedCallback(change =>
				matchSettings.roundTimeSeconds = MinutesToSeconds(change.newValue));

			scoreTargetField.RegisterValueChangedCallback(change =>
				matchSettings.scoreTarget = (short)Mathf.Clamp(change.newValue, 1, short.MaxValue));

			damageMultiplierField.RegisterValueChangedCallback(change =>
				matchSettings.damageMultiplier = Mathf.Max(0f, change.newValue));

			roundsField.RegisterValueChangedCallback(change =>
				matchSettings.numRounds = (byte)Mathf.Clamp(change.newValue, 1, byte.MaxValue));

			respawnTimeField.RegisterValueChangedCallback(change =>
				matchSettings.respawnSeconds = Mathf.Max(0f, change.newValue));

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
				() => MapEditor.SetActive(true);
			Require<Button>(root, "finish-editing-button").clicked +=
				() => MapEditor.SetActive(false);

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
			MapEditor.ActiveChanged += OnMapEditorStateChanged;

			OnMatchStateChanged(MatchReferee.State);
			OnNetcodeStateChanged(NetcodeManagement.State);
			OnMapEditorStateChanged(MapEditor.IsActive);
		}

		private void OnDisable()
		{
			MatchReferee.StateChanged -= OnMatchStateChanged;
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MapEditor.ActiveChanged -= OnMapEditorStateChanged;
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

			SetDisplayed(roundTimeField, matchSettings.CheckWinByTimer());
			SetDisplayed(scoreTargetField, matchSettings.CheckWinByScore());
		}

		// respawn-radio choices follow the RespawnCondition enum order
		private void SetRespawnCondition(int choice)
		{
			matchSettings.respawnCondition =
				(RespawnCondition)Mathf.Clamp(choice, 0, (int)RespawnCondition.NextRound);

			SetDisplayed(respawnTimeField,
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
				MapEditor.SetActive(false);
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
