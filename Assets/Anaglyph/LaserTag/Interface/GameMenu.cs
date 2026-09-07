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
		private NavPage playingPage;
		private NavPage editingMapPage;

		private MatchSettingsBinder matchSettings;
		private MapEditingMenuBinder mapEditing;

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

			navView = NavView.RequireIn(root);
			playingPage = navView.GetPage("playing-page");
			editingMapPage = navView.GetPage("editing-map-page");

			matchSettings = new MatchSettingsBinder(root);
			mapEditing = new MapEditingMenuBinder(editingMapPage);
			navView.Changed += OnNavPageChanged;

			matchSettings.StartButton.clicked += () => Referee?.QueueMatch(matchSettings.Settings);
			Require<Button>(root, "stop-button").clicked += () => Referee?.EndMatch();
			Require<Button>(root, "edit-map-button").clicked +=
				() => MapEditor.MapEditor.SetActive(true);
			Require<Button>(root, "finish-editing-button").clicked +=
				() => MapEditor.MapEditor.SetActive(false);
		}

		private void OnEnable()
		{
			InitializeUI();

			MatchReferee.StateChanged += OnMatchStateChanged;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			MapEditor.MapEditor.ActiveChanged += OnMapEditorStateChanged;
			MapEditor.MapEditor.TagRegistrationRequested += ShowTagsPage;

			OnMatchStateChanged(MatchReferee.State);
			OnNetcodeStateChanged(NetcodeManagement.State);
			OnMapEditorStateChanged(MapEditor.MapEditor.IsActive);
		}

		private void OnDisable()
		{
			MatchReferee.StateChanged -= OnMatchStateChanged;
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MapEditor.MapEditor.ActiveChanged -= OnMapEditorStateChanged;
			MapEditor.MapEditor.TagRegistrationRequested -= ShowTagsPage;
			navView.Changed -= OnNavPageChanged;
			mapEditing.Dispose();
			mapEditing = null;
			navView = null;
		}

		private void Update() => mapEditing?.Refresh();

		private void ShowTagsPage() => mapEditing.ShowTagsPage();

		private void OnNavPageChanged(NavPage page)
		{
			mapEditing.SetPresented(MapEditor.MapEditor.IsActive && page == editingMapPage);
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
			matchSettings.StartButton.SetEnabled(state == NetcodeState.Connected);
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
