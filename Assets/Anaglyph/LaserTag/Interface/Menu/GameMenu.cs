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

		private Toggle winByScoreToggle;
		private VisualElement timeFieldRow;
		private TextField timeField;
		private VisualElement scoreFieldRow;
		private TextField scoreField;
		private TextField damageMultiplierField;
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

			navView = new UIToolkitNavPages(Require<VisualElement>(root, "pages"));
			homePage = navView.AddPage("home-page", false);
			matchPage = navView.AddPage("match-page");
			mapManagerPage = navView.AddPage("map-manager-page");
			playingPage = navView.AddPage("playing-page", false);
			editingMapPage = navView.AddPage("editing-map-page", false);

			Require<Button>(root, "play-match-button").clicked += matchPage.NavigateHere;
			Require<Button>(root, "manage-map-button").clicked += mapManagerPage.NavigateHere;

			winByScoreToggle = Require<Toggle>(root, "win-by-score-toggle");
			timeFieldRow = Require<VisualElement>(root, "time-field-row");
			timeField = Require<TextField>(root, "time-field");
			scoreFieldRow = Require<VisualElement>(root, "score-field-row");
			scoreField = Require<TextField>(root, "score-field");
			damageMultiplierField = Require<TextField>(root, "damage-multiplier-field");
			startButton = Require<Button>(root, "start-button");

			winByScoreToggle.RegisterValueChangedCallback(
				change => SetWinByScore(change.newValue));
			winByScoreToggle.SetValueWithoutNotify(matchSettings.CheckWinByScore());
			SetWinByScore(winByScoreToggle.value);

			timeField.SetValueWithoutNotify((matchSettings.timerSeconds / 60f).ToString());
			timeField.RegisterValueChangedCallback(change =>
			{
				if (float.TryParse(change.newValue, out float minutes))
					matchSettings.timerSeconds = (int)(minutes * 60f);
			});

			scoreField.SetValueWithoutNotify(matchSettings.scoreTarget.ToString());
			scoreField.RegisterValueChangedCallback(change =>
			{
				if (short.TryParse(change.newValue, out short target))
					matchSettings.scoreTarget = target;
			});

			damageMultiplierField.SetValueWithoutNotify(
				matchSettings.damageMultiplier.ToString());
			damageMultiplierField.RegisterValueChangedCallback(change =>
			{
				if (float.TryParse(change.newValue, out float multiplier))
					matchSettings.damageMultiplier = multiplier;
			});

			startButton.clicked += () => Referee?.QueueMatch(matchSettings);
			Require<Button>(root, "stop-button").clicked += () => Referee?.EndMatch();
			Require<Button>(root, "edit-map-button").clicked +=
				() => MapEditor.SetActive(true);
			Require<Button>(root, "finish-editing-button").clicked +=
				() => MapEditor.SetActive(false);

			navView.Start(homePage);
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

		private void SetWinByScore(bool winByScore)
		{
			timeFieldRow.style.display =
				winByScore ? DisplayStyle.None : DisplayStyle.Flex;
			scoreFieldRow.style.display =
				winByScore ? DisplayStyle.Flex : DisplayStyle.None;
			matchSettings.winCondition =
				winByScore ? WinCondition.ReachScore : WinCondition.Timer;
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
