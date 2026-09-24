using System;
using Anaglyph.LaserTag.EnvSyncing;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Weapons;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.Netcode.SyncVariables;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	[DefaultExecutionOrder(100)]
	public class GameMenu : MonoBehaviour
	{
		public enum Tab { Maps, Match, Demo }

		[SerializeField] private WeaponDatabase weaponDatabase;

		private MenuErrorPresenter errors;
		private NavView navView;
		private NavView mapsNavigation;
		private NavView matchNavigation;
		private NavView demoNavigation;
		private Button mapsTab;
		private Button matchTab;
		private Button demoTab;
		private Tab selectedTab = Tab.Maps;
		private VisualElement demoWeapons;
		private Button showDebugMeshForEveryone;
		private Button hideDebugMeshForEveryone;
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
			demoNavigation = Require<NavView>(root, "demo-nav");
			mapsTab = Require<Button>(root, "maps-tab");
			matchTab = Require<Button>(root, "match-tab");
			demoTab = Require<Button>(root, "demo-tab");
			playingPage = matchNavigation.GetPage("playing-page");
			missingBasesPage = matchNavigation.GetPage("missing-bases-modal");
			hadRequiredBases = null;
			editingMapPage = mapsNavigation.GetPage("editing-map-page");
			spaceDetailsPage = mapsNavigation.GetPage("space-details");
			bindings.Click(mapsTab, () => SelectTab(Tab.Maps));
			bindings.Click(matchTab, () => SelectTab(Tab.Match));
			bindings.Click(demoTab, () => SelectTab(Tab.Demo));
			InitializeDemo(root);

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
			SyncBus.Activated += RefreshDemoControls;
			SyncBus.Deactivated += RefreshDemoControls;
			MapEditor.MapEditor.ActiveChanged += OnMapEditorStateChanged;
			MapEditor.MapEditor.TagRegistrationRequested += ShowSpaceAlignment;

			OnNetcodeStateChanged(NetcodeManagement.State);
			if (MapEditor.MapEditor.IsActive && MapEditorTool.CurrentMode is MapEditorTool.Mode.Tags or MapEditorTool.Mode.MeasureTagSize)
				ShowSpaceAlignment();
			else if (MapEditor.MapEditor.IsActive) OnMapEditorStateChanged(true);
			OnMatchStateChanged(MatchReferee.State);
			SelectTab(selectedTab);
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
			SyncBus.Activated -= RefreshDemoControls;
			SyncBus.Deactivated -= RefreshDemoControls;
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
			demoNavigation = null;
		}

		private void Update()
		{
			RefreshBaseRequirements();
			if (selectedTab != Tab.Maps) return;
			if (mapsNavigation?.CurrentPage == spaceDetailsPage)
			{
				spaceDetails?.Refresh();
				UpdateEditor();
			}
			if (mapsNavigation?.CurrentPage == editingMapPage) mapName?.Refresh();
		}

		private void SelectTab(Tab tab)
		{
			bool maps = tab == Tab.Maps;
			if (maps && !mapsTab.enabledSelf || tab == Tab.Demo && !demoTab.enabledSelf) return;
			selectedTab = tab;
			mapsNavigation.EnableInClassList("game-menu-hidden", !maps);
			matchNavigation.EnableInClassList("game-menu-hidden", tab != Tab.Match);
			demoNavigation.EnableInClassList("game-menu-hidden", tab != Tab.Demo);
			mapsTab.EnableInClassList("game-menu-tab-selected", maps);
			matchTab.EnableInClassList("game-menu-tab-selected", tab == Tab.Match);
			demoTab.EnableInClassList("game-menu-tab-selected", tab == Tab.Demo);
			OnNavPageChanged(mapsNavigation.CurrentPage);
		}

		private void InitializeDemo(VisualElement root)
		{
			demoWeapons = Require<VisualElement>(root, "demo-weapons");
			demoWeapons.Clear();
			if (weaponDatabase != null)
				for (int id = 0; id < weaponDatabase.Count; id++)
				{
					GameObject weapon = weaponDatabase.GetWeapon(id);
					if (weapon == null) continue;
					int weaponId = id;
					var button = new Button { name = $"demo-weapon-{id}", text = weapon.name };
					button.MakeActOnPress();
					bindings.Click(button, () => SessionWeaponSwitcher.Instance?.SwitchEveryone(weaponId));
					demoWeapons.Add(button);
				}

			showDebugMeshForEveryone = Require<Button>(root, "show-debug-mesh-for-everyone");
			hideDebugMeshForEveryone = Require<Button>(root, "hide-debug-mesh-for-everyone");
			bindings.Click(showDebugMeshForEveryone, () => EnvMeshSync.Instance?.SetEnvMeshVisibleEveryone(true));
			bindings.Click(hideDebugMeshForEveryone, () => EnvMeshSync.Instance?.SetEnvMeshVisibleEveryone(false));
			RefreshDemoControls();
		}

		private void RefreshDemoControls()
		{
			demoWeapons.SetEnabled(SyncBus.Active && SessionWeaponSwitcher.Instance != null);
			showDebugMeshForEveryone.SetEnabled(SyncBus.Active);
			hideDebugMeshForEveryone.SetEnabled(SyncBus.Active);
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
			mapsNavigation.PresentModal(editingMapPage, 10);
			SelectTab(Tab.Maps);
			MapEditorTool.SetMode(MapEditorTool.Mode.Move);
		}

		private void ShowSpaceAlignment()
		{
			if (!mapsTab.enabledSelf) { SetEditorActive(false); return; }
			mapsNavigation.DismissModal(editingMapPage);
			mapsNavigation.GoToPage("map-manager-page");
			mapsNavigation.GoToPage(spaceDetailsPage);
			SelectTab(Tab.Maps);
		}

		private void OnAlignmentChanging()
		{
			if (selectedTab == Tab.Maps && mapsNavigation.CurrentPage == spaceDetailsPage &&
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
			bool active = selectedTab == Tab.Maps && mapsTab.enabledSelf &&
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
			if (selectedTab == Tab.Maps && page?.name == "map-manager-page") mapProbe?.Probe();
		}

		private void OnMapEditorStateChanged(bool active)
		{
			if (updatingEditor) return;
			if (active)
			{
				if (!mapsTab.enabledSelf) { SetEditorActive(false); return; }
				mapsNavigation.PresentModal(editingMapPage, 10);
				SelectTab(Tab.Maps);
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
			demoTab.SetEnabled(!playing);
			matchNavigation.SetModalPresented(playingPage, playing, 20);
			if (playing) SelectTab(Tab.Match);
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			matchSettings.StartButton.SetEnabled(state == NetcodeState.Connected && MapBaseRequirements.HasBothTeamBases);
		}
	}
}
