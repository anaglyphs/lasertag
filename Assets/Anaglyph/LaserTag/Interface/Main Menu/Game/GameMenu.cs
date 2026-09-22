using System;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Maps;
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
		private NavView mapsNavigation;
		private NavView matchNavigation;
		private Button mapsTab;
		private Button matchTab;
		private bool mapsSelected = true;
		private bool updatingEditor;
		private NavPage playingPage;
		private NavPage missingBasesPage;
		private bool? hadRequiredBases;
		private NavPage editingMapPage;

		private readonly UIEventBindings bindings = new();
		private MapManagerUI mapManager;
		private MapProbeBinder mapProbe;
		private MatchSettingsBinder matchSettings;
		private MapNameBinder mapName;
		private SpaceDetailsBinder spaceDetails;
		private NavPage spaceDetailsPage;

		private MatchReferee Referee => MatchReferee.Instance;

		private void Awake() => errors = new MenuErrorPresenter(MenuErrorArea.Game);
		private void OnDestroy() => errors?.Dispose();

		private void InitializeUI(VisualElement root)
		{
			if (root == null)
				throw new InvalidOperationException(
					"GameMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			navView = Require<NavView>(root, "game-nav");
			mapsNavigation = Require<NavView>(root, "maps-nav");
			matchNavigation = Require<NavView>(root, "match-nav");
			mapsTab = Require<Button>(root, "maps-tab");
			matchTab = Require<Button>(root, "match-tab");
			playingPage = matchNavigation.GetPage("playing-page");
			missingBasesPage = matchNavigation.GetPage("missing-bases-modal");
			hadRequiredBases = null;
			editingMapPage = mapsNavigation.GetPage("editing-map-page");
			spaceDetailsPage = mapsNavigation.GetPage("space-details");
			bindings.Click(mapsTab, () => SelectTab(true));
			bindings.Click(matchTab, () => SelectTab(false));

			mapManager = GetComponent<MapManagerUI>();
			if (mapManager == null)
				throw new InvalidOperationException("GameMenu requires MapManagerUI on the same object.");
			mapManager.Bind(Require<VisualElement>(root, "map-catalog-section"), spaceDetailsPage,
				ShowMapEditing);
			mapProbe = new MapProbeBinder(Require<Button>(root, "probe-maps-button"));
			matchSettings = new MatchSettingsBinder(root);
			mapName = new MapNameBinder(Require<VisualElement>(editingMapPage, "map-name-section"));
			spaceDetails = new SpaceDetailsBinder(spaceDetailsPage);
			spaceDetails.Alignment.Changing += OnAlignmentChanging;
			spaceDetails.Alignment.Changed += OnAlignmentChanged;
			mapsNavigation.Changed += OnNavPageChanged;

			bindings.Click(matchSettings.StartButton, () =>
			{
				if (MapBaseRequirements.HasBothTeamBases) Referee?.QueueMatch(matchSettings.Settings);
			});
			bindings.Click(Require<Button>(missingBasesPage, "place-bases-button"), ShowMapEditing);
			bindings.Click(Require<Button>(root, "stop-button"), () => Referee?.EndMatch());
			bindings.Click(Require<Button>(root, "finish-editing-button"),
				() => MapEditor.MapEditor.SetActive(false));
			RefreshBaseRequirements();
		}

		private void OnEnable()
		{
			InitializeUI(GetComponent<UIDocument>()?.rootVisualElement);

			MatchReferee.StateChanged += OnMatchStateChanged;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			MapEditor.MapEditor.ActiveChanged += OnMapEditorStateChanged;
			MapEditor.MapEditor.TagRegistrationRequested += ShowSpaceAlignment;

			OnNetcodeStateChanged(NetcodeManagement.State);
			if (MapEditor.MapEditor.IsActive && MapEditorTool.CurrentMode is MapEditorTool.Mode.Tags or MapEditorTool.Mode.MeasureTagSize)
				ShowSpaceAlignment();
			else if (MapEditor.MapEditor.IsActive) OnMapEditorStateChanged(true);
			OnMatchStateChanged(MatchReferee.State);
			SelectTab(mapsSelected);
			errors.Bind(navView);
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
			if (mapsNavigation != null) mapsNavigation.Changed -= OnNavPageChanged;
			SetEditorActive(false);
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
			mapsNavigation = null;
			matchNavigation = null;
		}

		private void Update()
		{
			RefreshBaseRequirements();
			if (!mapsSelected) return;
			if (mapsNavigation?.CurrentPage == spaceDetailsPage)
			{
				spaceDetails?.Refresh();
				UpdateEditor();
			}
			if (mapsNavigation?.CurrentPage == editingMapPage) mapName?.Refresh();
		}

		private void SelectTab(bool maps)
		{
			if (maps && !mapsTab.enabledSelf) return;
			mapsSelected = maps;
			mapsNavigation.EnableInClassList("game-menu-hidden", !maps);
			matchNavigation.EnableInClassList("game-menu-hidden", maps);
			mapsTab.EnableInClassList("game-menu-tab-selected", maps);
			matchTab.EnableInClassList("game-menu-tab-selected", !maps);
			OnNavPageChanged(mapsNavigation.CurrentPage);
		}

		private void RefreshBaseRequirements()
		{
			bool ready = MapBaseRequirements.HasBothTeamBases;
			if (hadRequiredBases == ready) return;
			hadRequiredBases = ready;
			matchNavigation.SetModalPresented(missingBasesPage, !ready, 10);
			OnNetcodeStateChanged(NetcodeManagement.State);
		}

		private void ShowMapEditing()
		{
			if (!mapsTab.enabledSelf) return;
			mapsSelected = true;
			mapsNavigation.PresentModal(editingMapPage, 10);
			SelectTab(true);
			MapEditorTool.SetMode(MapEditorTool.Mode.Move);
		}

		private void ShowSpaceAlignment()
		{
			if (!mapsTab.enabledSelf) { SetEditorActive(false); return; }
			mapsNavigation.DismissModal(editingMapPage);
			mapsNavigation.GoToPage("map-manager-page");
			mapsNavigation.GoToPage(spaceDetailsPage);
			SelectTab(true);
		}

		private void OnAlignmentChanging()
		{
			if (mapsSelected && mapsNavigation.CurrentPage == spaceDetailsPage &&
				MapEditorTool.CurrentMode == MapEditorTool.Mode.MeasureTagSize)
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
		}

		private void OnAlignmentChanged()
		{
			if (mapsNavigation.CurrentPage == spaceDetailsPage) UpdateEditor();
		}

		private void UpdateEditor()
		{
			bool spaceTools = mapsNavigation.CurrentPage == spaceDetailsPage && spaceDetails.Alignment.UsesTags;
			bool active = mapsSelected && mapsTab.enabledSelf &&
				(mapsNavigation.CurrentPage == editingMapPage || spaceTools);
			SetEditorActive(active);
			if (active && spaceTools && MapEditorTool.CurrentMode is not (MapEditorTool.Mode.Tags or MapEditorTool.Mode.MeasureTagSize))
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
		}

		private void SetEditorActive(bool active)
		{
			updatingEditor = true;
			try { MapEditor.MapEditor.SetActive(active); }
			finally { updatingEditor = false; }
		}

		private void OnNavPageChanged(NavPage page)
		{
			if (page == spaceDetailsPage) spaceDetails.Refresh();
			if (page == editingMapPage) mapName.Refresh();
			UpdateEditor();
			if (mapsSelected && page?.name == "map-manager-page") mapProbe?.Probe();
		}

		private void OnMapEditorStateChanged(bool active)
		{
			if (updatingEditor) return;
			if (active)
			{
				if (!mapsTab.enabledSelf) { SetEditorActive(false); return; }
				mapsNavigation.PresentModal(editingMapPage, 10);
				SelectTab(true);
			}
			else
			{
				mapsNavigation.DismissModal(editingMapPage);
				if (mapsNavigation.CurrentPage == spaceDetailsPage)
					mapsNavigation.GoToPage("map-manager-page");
			}
		}

		private void OnMatchStateChanged(MatchState state)
		{
			bool playing = state != MatchState.NotPlaying;
			mapsTab.SetEnabled(!playing);
			matchNavigation.SetModalPresented(playingPage, playing, 20);
			if (playing) SelectTab(false);
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			matchSettings.StartButton.SetEnabled(state == NetcodeState.Connected && MapBaseRequirements.HasBothTeamBases);
		}
	}
}
