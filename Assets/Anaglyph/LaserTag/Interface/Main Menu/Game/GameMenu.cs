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
		/// <summary>The menu shows one of two navigations: the match pages, or the space and map pages.</summary>
		public enum Section { Match, Maps }

		private MenuErrorPresenter errors;
		private NavView navView;
		private NavView mapsNavigation;
		private NavView matchNavigation;
		private Section shownSection = Section.Match;
		private bool matchInProgress;
		private bool updatingEditor;
		private NavPage playingPage;
		private NavPage editingMapPage;

		private readonly UIEventBindings bindings = new();
		private MapManagerUI mapManager;
		private MapProbeBinder mapProbe;
		private LoadedSpaceMapsBinder loadedSpaceMaps;
		private NavPage manageMapsPage;
		private MatchSettingsBinder matchSettings;
		private MatchObjectsBinder matchObjects;
		private NavPage matchObjectsPage;
		private bool placingMatchObjects;
		private float nextMatchObjectsRefresh;
		private const float MatchObjectsRefreshSeconds = .25f;
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
			playingPage = matchNavigation.GetPage("playing-page");
			editingMapPage = mapsNavigation.GetPage("editing-map-page");
			spaceDetailsPage = mapsNavigation.GetPage("space-details");
			manageMapsPage = mapsNavigation.GetPage("manage-maps-page");
			matchObjectsPage = matchNavigation.GetPage(MatchObjectsBinder.PageName);

			mapManager = GetComponent<MapManagerUI>();
			if (mapManager == null)
				throw new InvalidOperationException("GameMenu requires MapManagerUI on the same object.");
			mapManager.Bind(Require<VisualElement>(root, "map-catalog-section"), spaceDetailsPage,
				ShowMapEditing);
			mapProbe = new MapProbeBinder(Require<Button>(root, "probe-maps-button"));
			loadedSpaceMaps = new LoadedSpaceMapsBinder(Require<VisualElement>(root, "loaded-space-maps-section"), ShowMapEditing);
			matchSettings = new MatchSettingsBinder(root);
			matchSettings.StartButton.SetBinding("text", MenuCopy.String("GameMenu", "match-objects.next"));
			matchObjects = new MatchObjectsBinder(matchObjectsPage);
			mapName = new MapNameBinder(Require<VisualElement>(editingMapPage, "map-name-section"));
			spaceDetails = new SpaceDetailsBinder(spaceDetailsPage);
			spaceDetails.Alignment.Changing += OnAlignmentChanging;
			spaceDetails.Alignment.Changed += OnAlignmentChanged;
			mapsNavigation.Changed += OnNavPageChanged;
			matchNavigation.Changed += OnMatchPageChanged;

			bindings.Click(matchSettings.StartButton, ShowMatchObjects);
			bindings.Click(matchObjects.PlaceObjectsButton, ShowMatchObjects);
			bindings.Click(matchObjects.StartButton, QueueMatchIfReady);
			bindings.Click(Require<Button>(root, "stop-button"), () => Referee?.EndMatch());
			bindings.Click(Require<Button>(root, "finish-editing-button"),
				() => MapEditor.MapEditor.SetActive(false));
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
			ShowSection(shownSection);
			errors.Bind(navView);
		}

		private void OnDisable()
		{
			errors?.Unbind();
			bindings.Dispose();
			mapManager?.Unbind();
			mapProbe?.Dispose();
			mapProbe = null;
			loadedSpaceMaps?.Dispose();
			loadedSpaceMaps = null;
			matchSettings?.Dispose();
			matchSettings = null;
			matchObjects = null;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MapEditor.MapEditor.ActiveChanged -= OnMapEditorStateChanged;
			MapEditor.MapEditor.TagRegistrationRequested -= ShowSpaceAlignment;
			if (mapsNavigation != null) mapsNavigation.Changed -= OnNavPageChanged;
			if (matchNavigation != null) matchNavigation.Changed -= OnMatchPageChanged;
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
			if (matchObjects != null && shownSection == Section.Match && matchNavigation.CurrentPage == matchObjectsPage &&
				Time.unscaledTime >= nextMatchObjectsRefresh) RefreshMatchObjects();
			if (shownSection != Section.Maps) return;
			if (mapsNavigation?.CurrentPage == spaceDetailsPage)
			{
				spaceDetails?.Refresh();
				UpdateEditor();
			}
			if (mapsNavigation?.CurrentPage == editingMapPage) mapName?.Refresh();
		}

		private void ShowSection(Section section)
		{
			if (section == Section.Maps && matchInProgress) return;
			shownSection = section;
			mapsNavigation.EnableInClassList("game-menu-hidden", section != Section.Maps);
			matchNavigation.EnableInClassList("game-menu-hidden", section != Section.Match);
			OnNavPageChanged(mapsNavigation.CurrentPage);
		}

		/// <summary>Opens the page that asks for the match's bases and flags, with the editor on.</summary>
		private void ShowMatchObjects()
		{
			placingMatchObjects = true;
			matchNavigation.GoToPage(matchObjectsPage);
			ShowSection(Section.Match);
			RefreshMatchObjects();
		}

		private void LeaveMatchObjects()
		{
			foreach (NavPage page in matchNavigation.History)
				if (page == matchObjectsPage)
				{
					matchNavigation.GoToPage(matchNavigation.FirstPageName);
					return;
				}
		}

		private bool RefreshMatchObjects()
		{
			nextMatchObjectsRefresh = Time.unscaledTime + MatchObjectsRefreshSeconds;
			return matchObjects.Refresh(matchSettings.Settings, placingMatchObjects);
		}

		private void QueueMatchIfReady()
		{
			if (!RefreshMatchObjects()) return;
			LeaveMatchObjects();
			if (Referee == null) return;
			Referee.QueueMatch(matchSettings.Settings);
			GetComponentInParent<MainMenuController>()?.SetVisible(false);
		}

		private void OnMatchPageChanged(NavPage page)
		{
			placingMatchObjects = page == matchObjectsPage;
			UpdateEditor();
			RefreshMatchObjects();
		}

		private void ShowMapEditing()
		{
			if (matchInProgress) return;
			mapsNavigation.PresentModal(editingMapPage, 10);
			ShowSection(Section.Maps);
			MapEditorTool.SetMode(MapEditorTool.Mode.Move);
		}

		private void ShowSpaceAlignment()
		{
			if (matchInProgress) { SetEditorActive(false); return; }
			mapsNavigation.DismissModal(editingMapPage);
			LeaveMatchObjects();
			mapsNavigation.GoToPage("maps-page");
			mapsNavigation.GoToPage(manageMapsPage);
			mapsNavigation.GoToPage(spaceDetailsPage);
			ShowSection(Section.Maps);
		}

		private void OnAlignmentChanging()
		{
			if (shownSection == Section.Maps && mapsNavigation.CurrentPage == spaceDetailsPage &&
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
			bool editingFromMaps = shownSection == Section.Maps && !matchInProgress &&
				(mapsNavigation.CurrentPage == editingMapPage || spaceTools);
			bool placingForMatch = shownSection == Section.Match && placingMatchObjects &&
				matchNavigation.CurrentPage == matchObjectsPage;
			SetEditorActive(editingFromMaps || placingForMatch);
			if (editingFromMaps && spaceTools && MapEditorTool.CurrentMode is not (MapEditorTool.Mode.Tags or MapEditorTool.Mode.MeasureTagSize))
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
		}

		private void SetEditorActive(bool active)
		{
			updatingEditor = true;
			try { MapEditor.MapEditor.SetActive(active); }
			finally { updatingEditor = false; }
		}

		// no menu page leads to the maps section: leaving space settings returns to the match pages
		private bool IsBehindSpaceSettings(NavPage page) =>
			page == null || page == manageMapsPage || page.name == mapsNavigation.FirstPageName;

		private void OnNavPageChanged(NavPage page)
		{
			if (shownSection == Section.Maps && IsBehindSpaceSettings(page))
			{
				ShowSection(Section.Match);
				return;
			}
			if (page == spaceDetailsPage) spaceDetails.Refresh();
			if (page == editingMapPage) mapName.Refresh();
			UpdateEditor();
			if (shownSection == Section.Maps && page == manageMapsPage) mapProbe?.Probe();
		}

		private void OnMapEditorStateChanged(bool active)
		{
			if (updatingEditor) return;
			if (active)
			{
				if (matchInProgress) SetEditorActive(false);
				else ShowMatchObjects();
			}
			else
			{
				placingMatchObjects = false;
				RefreshMatchObjects();
				mapsNavigation.DismissModal(editingMapPage);
				if (mapsNavigation.CurrentPage == spaceDetailsPage)
					mapsNavigation.GoToPage(manageMapsPage);
			}
		}

		private void OnMatchStateChanged(MatchState state)
		{
			bool playing = state != MatchState.NotPlaying;
			if (playing) MapEditor.MapEditor.SetActive(false);
			if (playing) LeaveMatchObjects();
			matchInProgress = playing;
			matchNavigation.SetModalPresented(playingPage, playing, 20);
			if (playing) ShowSection(Section.Match);
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			// the panel is shown only in a session: placement ends with it
			if (state != NetcodeState.Connected) LeaveMatchObjects();
		}
	}
}
