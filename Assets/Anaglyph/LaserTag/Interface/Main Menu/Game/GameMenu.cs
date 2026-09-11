using System;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	[DefaultExecutionOrder(100)]
	public class GameMenu : MonoBehaviour
	{
		private MenuErrorPresenter errors;
		private NavView navView;
		private NavPage playingPage;
		private NavPage editingMapPage;

		private readonly UIEventBindings bindings = new();
		private MapManagerUI mapManager;
		private MapProbeBinder mapProbe;
		private MatchSettingsBinder matchSettings;
		private MapEditingMenuBinder mapEditing;

		private MatchReferee Referee => MatchReferee.Instance;

		private void Awake() => errors = new MenuErrorPresenter(UserErrorArea.Game);
		private void OnDestroy() => errors?.Dispose();

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

			mapManager = GetComponent<MapManagerUI>();
			if (mapManager == null)
				throw new InvalidOperationException("GameMenu requires MapManagerUI on the same object.");
			mapManager.Bind(Require<VisualElement>(root, "map-catalog-section"));
			mapProbe = new MapProbeBinder(Require<Button>(root, "probe-maps-button"));
			bindings.Click(Require<Button>(root, "manage-map-button"), mapProbe.Probe);
			matchSettings = new MatchSettingsBinder(root);
			mapEditing = new MapEditingMenuBinder(editingMapPage);
			navView.Changed += OnNavPageChanged;

			bindings.Click(matchSettings.StartButton, () => Referee?.QueueMatch(matchSettings.Settings));
			bindings.Click(Require<Button>(root, "stop-button"), () => Referee?.EndMatch());
			bindings.Click(Require<Button>(root, "edit-map-button"),
				() => MapEditor.MapEditor.SetActive(true));
			bindings.Click(Require<Button>(root, "finish-editing-button"),
				() => MapEditor.MapEditor.SetActive(false));
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
			errors.Bind(navView);
			// Rebinding a surviving tree may leave the same page selected, with no Changed event.
			OnNavPageChanged(navView.CurrentPage);
		}

		private void OnDisable()
		{
			errors?.Unbind();
			bindings.Dispose();
			mapManager?.Unbind();
			mapProbe?.Dispose();
			mapProbe = null;
			matchSettings?.Dispose();
			matchSettings = null;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MapEditor.MapEditor.ActiveChanged -= OnMapEditorStateChanged;
			MapEditor.MapEditor.TagRegistrationRequested -= ShowTagsPage;
			if (navView != null) navView.Changed -= OnNavPageChanged;
			mapEditing?.Dispose();
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
			if (active) mapEditing.ResetForEditingSession();
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
	}
}
