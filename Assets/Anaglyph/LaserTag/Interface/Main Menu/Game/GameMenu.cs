using System;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.MapEditor.Tools;
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
		private MapNameBinder mapName;
		private SpaceDetailsBinder spaceDetails;
		private NavPage spaceDetailsPage;
		private bool editingSpace;

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
			spaceDetailsPage = navView.GetPage("space-details");

			mapManager = GetComponent<MapManagerUI>();
			if (mapManager == null)
				throw new InvalidOperationException("GameMenu requires MapManagerUI on the same object.");
			mapManager.Bind(Require<VisualElement>(root, "map-catalog-section"), spaceDetailsPage,
				() => MapEditor.MapEditor.SetActive(true));
			mapProbe = new MapProbeBinder(Require<Button>(root, "probe-maps-button"));
			bindings.Click(Require<Button>(root, "manage-map-button"), mapProbe.Probe);
			matchSettings = new MatchSettingsBinder(root);
			mapName = new MapNameBinder(Require<VisualElement>(editingMapPage, "map-name-section"));
			spaceDetails = new SpaceDetailsBinder(spaceDetailsPage);
			spaceDetails.Alignment.Changing += OnAlignmentChanging;
			spaceDetails.Alignment.Changed += OnAlignmentChanged;
			navView.Changed += OnNavPageChanged;

			bindings.Click(matchSettings.StartButton, () => Referee?.QueueMatch(matchSettings.Settings));
			bindings.Click(Require<Button>(root, "stop-button"), () => Referee?.EndMatch());
			bindings.Click(Require<Button>(root, "finish-editing-button"),
				() => MapEditor.MapEditor.SetActive(false));
		}

		private void OnEnable()
		{
			InitializeUI();

			MatchReferee.StateChanged += OnMatchStateChanged;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			MapEditor.MapEditor.ActiveChanged += OnMapEditorStateChanged;
			MapEditor.MapEditor.TagRegistrationRequested += ShowSpaceAlignment;

			OnMatchStateChanged(MatchReferee.State);
			OnNetcodeStateChanged(NetcodeManagement.State);
			if (MapEditor.MapEditor.IsActive && MapEditorTool.CurrentMode is MapEditorTool.Mode.Tags or MapEditorTool.Mode.MeasureTagSize)
				ShowSpaceAlignment();
			else OnMapEditorStateChanged(MapEditor.MapEditor.IsActive);
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
			MapEditor.MapEditor.TagRegistrationRequested -= ShowSpaceAlignment;
			if (navView != null) navView.Changed -= OnNavPageChanged;
			if (spaceDetails != null)
			{
				spaceDetails.Alignment.Changing -= OnAlignmentChanging;
				spaceDetails.Alignment.Changed -= OnAlignmentChanged;
				spaceDetails.Dispose();
			}
			spaceDetails = null;
			mapName?.Dispose();
			mapName = null;
			navView = null;
		}

		private void Update()
		{
			if (navView?.CurrentPage == spaceDetailsPage)
			{
				spaceDetails?.Refresh();
				UpdateSpaceTools();
			}
			if (navView?.CurrentPage == editingMapPage) mapName?.Refresh();
		}

		private void ShowSpaceAlignment()
		{
			editingSpace = false;
			navView.DismissModal(editingMapPage);
			navView.GoToPage("map-manager-page");
			navView.GoToPage(spaceDetailsPage);
			StartSpaceTools();
		}

		private void OnAlignmentChanging()
		{
			if (editingSpace && MapEditorTool.CurrentMode == MapEditorTool.Mode.MeasureTagSize)
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
		}

		private void OnAlignmentChanged()
		{
			if (navView.CurrentPage == spaceDetailsPage) UpdateSpaceTools();
		}

		private void UpdateSpaceTools()
		{
			if (spaceDetails.Alignment.UsesTags) StartSpaceTools();
			else StopSpaceTools();
		}

		private void StartSpaceTools()
		{
			editingSpace = true;
			MapEditor.MapEditor.SetActive(true);
			if (MapEditorTool.CurrentMode is not (MapEditorTool.Mode.Tags or MapEditorTool.Mode.MeasureTagSize))
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
		}

		private void StopSpaceTools()
		{
			if (!editingSpace) return;
			editingSpace = false;
			MapEditor.MapEditor.SetActive(false);
		}

		private void OnNavPageChanged(NavPage page)
		{
			if (page == spaceDetailsPage)
			{
				spaceDetails.Refresh();
				UpdateSpaceTools();
			}
			else if (page?.name != "error-modal") StopSpaceTools();
			if (page == editingMapPage) mapName.Refresh();
		}

		private void OnMapEditorStateChanged(bool active)
		{
			if (!active && editingSpace)
			{
				editingSpace = false;
				navView.GoToPage("map-manager-page");
			}
			navView.SetModalPresented(editingMapPage, active && !editingSpace, 10);
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
