using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player.Teams;
using Anaglyph.LaserTag.Weapons;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using TeamBase = Anaglyph.LaserTag.Objects.Gameplay.Base.Base;
using TeamFlag = Anaglyph.LaserTag.Objects.Gameplay.Flag.Flag;

namespace Anaglyph.LaserTag.Tests
{
	public class GameMenuTabTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private GameObject owner;
		private GameMenu menu;
		private WeaponDatabase database;
		private MenuPresentationTestWindow window;
		private VisualElement root;
		private NavView maps, match;
		private Locale previousLocale;
		private MapEditor.Tools.MapEditorTool.Mode previousMode;
		private readonly List<TeamBase> bases = new();
		private readonly List<GameObject> flags = new();

		private void Call(string method, params object[] arguments) =>
			typeof(GameMenu).GetMethod(method, Private).Invoke(menu, arguments);
		private T Handler<T>(string method) where T : Delegate =>
			(T)Delegate.CreateDelegate(typeof(T), menu, typeof(GameMenu).GetMethod(method, Private));

		[SetUp]
		public void SetUp()
		{
			Assert.That(SpaceManager.Instance, Is.Null);
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Assert.That(TeamBase.AllBases, Is.Empty);
			previousMode = MapEditor.Tools.MapEditorTool.CurrentMode;
			previousLocale = LocalizationSettings.SelectedLocale;
			LocalizationSettings.SelectedLocale = AssetDatabase.LoadAssetAtPath<Locale>(
				"Assets/Anaglyph/LaserTag/Localization/English.asset");
			window = ScriptableObject.CreateInstance<MenuPresentationTestWindow>();
			window.position = new Rect(100, 100, 440, 500);
			window.Show();
			window.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
				"Assets/Anaglyph/LaserTag/Interface/LaserTagRuntimeTheme.tss"));
			root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				"Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMenu.uxml").CloneTree();
			window.rootVisualElement.Add(root);
			maps = root.Q<NavView>("maps-nav");
			match = root.Q<NavView>("match-nav");
			owner = new GameObject("Game menu tabs test");
			owner.SetActive(false);
			owner.AddComponent<MapManagerUI>();
			menu = owner.AddComponent<GameMenu>();
			database = AssetDatabase.LoadAssetAtPath<WeaponDatabase>("Assets/Anaglyph/LaserTag/Weapons/Weapon Database.asset");
			var serializedMenu = new SerializedObject(menu);
			serializedMenu.FindProperty("weaponDatabase").objectReferenceValue = database;
			serializedMenu.ApplyModifiedPropertiesWithoutUndo();
			Bind();
		}

		private void Bind()
		{
			Call("InitializeUI", root);
			MapEditor.MapEditor.ActiveChanged += Handler<Action<bool>>("OnMapEditorStateChanged");
			MapEditor.MapEditor.TagRegistrationRequested += Handler<Action>("ShowSpaceAlignment");
			Call("OnMatchStateChanged", MatchState.NotPlaying);
		}

		[TearDown]
		public void TearDown()
		{
			Call("OnDisable");
			MapEditor.Tools.MapEditorTool.SetMode(previousMode);
			Object.DestroyImmediate(owner);
			window.Close();
			foreach (var homeBase in bases)
			{
				TeamBase.AllBases.Remove(homeBase);
				if (homeBase) Object.DestroyImmediate(homeBase.gameObject);
			}
			bases.Clear();
			foreach (var flag in flags) Object.DestroyImmediate(flag);
			flags.Clear();
			LocalizationSettings.SelectedLocale = previousLocale;
		}

		[UnityTest]
		public IEnumerator RepeatedTabPressesPauseEditingAndPreserveNavigationAndMatchSettings()
		{
			Assert.That(maps.CurrentPage.name, Is.EqualTo("maps-page"));
			Assert.That(root.Q<Button>("maps-tab").ClassListContains("game-menu-tab-selected"), Is.True);
			MapEditor.MapEditor.SetActive(true);
			var score = root.Q<DetentField>("score-target-field");
			score.value = 17;
			for (int visit = 0; visit < 3; visit++)
			{
				yield return Layout();
				Press(root.Q<Button>("match-tab"));
				Assert.That(MapEditor.MapEditor.IsActive, Is.False);
				Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
				Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
				yield return Layout();
				Assert.That(maps.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
				Assert.That(match.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
				Press(root.Q<Button>("maps-tab"));
				Assert.That(MapEditor.MapEditor.IsActive, Is.True);
				Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
				Assert.That(score.value, Is.EqualTo(17));
			}
			yield return Layout();
			Press(root.Q<Button>("finish-editing-button"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Assert.That(maps.CurrentPage.name, Is.EqualTo("maps-page"));
		}

		[TestCase(MatchState.Mustering)]
		[TestCase(MatchState.Countdown)]
		[TestCase(MatchState.Playing)]
		[TestCase(MatchState.RoundBreak)]
		public void ActiveMatchForcesMatchTabAndRejectsMapAndWizardSelection(MatchState state)
		{
			MapEditor.MapEditor.SetActive(true);
			Call("OnMatchStateChanged", state);
			Assert.That(match.CurrentPage.name, Is.EqualTo("playing-page"));
			Assert.That(root.Q<Button>("maps-tab").enabledSelf, Is.False);
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Call("SelectTab", GameMenu.Tab.Maps);
			MapEditor.MapEditor.RequestTagRegistration();
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
			Call("OnMatchStateChanged", MatchState.NotPlaying);
			Assert.That(root.Q<Button>("maps-tab").enabledSelf, Is.True);
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Call("SelectTab", GameMenu.Tab.Maps);
			Assert.That(MapEditor.MapEditor.IsActive, Is.True);
		}

		[Test]
		public void RegistrationSelectsMapsButAllowsSwitchingAway()
		{
			Call("SelectTab", GameMenu.Tab.Match);
			MapEditor.MapEditor.RequestTagRegistration();
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.False);
			Assert.That(maps.CurrentPage.name, Is.EqualTo("space-details"));
			Call("SelectTab", GameMenu.Tab.Match);
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
			Assert.That(maps.CurrentPage.name, Is.EqualTo("space-details"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
		}

		[UnityTest]
		public IEnumerator ErrorsPreserveBothTreesAndTheSelectedTab()
		{
			MapEditor.MapEditor.SetActive(true);
			Call("SelectTab", GameMenu.Tab.Match);
			var shell = root.Q<NavView>("game-nav");
			using var errors = new MenuErrorPresenter(MenuErrorArea.Game);
			errors.Bind(shell);
			errors.Show(new MenuError(MenuErrorArea.Game, "Test error", "Test details"));
			Assert.That(shell.CurrentPage.name, Is.EqualTo("error-modal"));
			yield return Layout();
			Press(root.Q<Button>("dismiss-error-button"));
			Assert.That(shell.CurrentPage.name, Is.EqualTo("home-page"));
			Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
		}

		[UnityTest]
		public IEnumerator RebindingPreservesPagesAndTabButtonsStillWork()
		{
			MapEditor.MapEditor.SetActive(true);
			Call("SelectTab", GameMenu.Tab.Match);
			for (int cycle = 0; cycle < 3; cycle++)
			{
				Call("OnDisable");
				Bind();
				yield return Layout();
				Press(root.Q<Button>("maps-tab"));
				Assert.That(MapEditor.MapEditor.IsActive, Is.True);
				Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
				yield return Layout();
				Press(root.Q<Button>("match-tab"));
				Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			}
		}

		[UnityTest]
		public IEnumerator TabsSitInsidePanelTopOnlyOnHomePagesAndFitInsideXrTouchArea()
		{
			yield return Layout();
			var panel = root.Q("game-menu-panel");
			var tabs = root.Q("game-menu-tabs");
			Assert.That(panel.layout.size, Is.EqualTo(new Vector2(400, 400)));
			Assert.That(tabs.worldBound.yMin, Is.EqualTo(panel.worldBound.yMin + 8).Within(.1f));
			Assert.That(root.Q<Button>("maps-tab").worldBound.xMin, Is.EqualTo(panel.worldBound.xMin + 8).Within(.1f));
			Assert.That(tabs.layout.height, Is.EqualTo(46));
			Assert.That(tabs.style.display.value, Is.EqualTo(DisplayStyle.Flex));
			maps.GoToPage("manage-maps-page");
			Assert.That(tabs.style.display.value, Is.EqualTo(DisplayStyle.None), "Pages past the home page cover the tabs.");
			maps.GoBack();
			Assert.That(tabs.style.display.value, Is.EqualTo(DisplayStyle.Flex));
			Assert.That(root.Q<Button>("maps-tab").text, Is.EqualTo("MAPS"));
			Assert.That(root.Q<Button>("match-tab").text, Is.EqualTo("MATCHES"));
			Assert.That(root.Q<Button>("demo-tab").text, Is.EqualTo("DEMO"));
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Anaglyph/LaserTag/Interface/Main Menu/Menu.prefab");
			var game = prefab.transform.Find("Game Panel");
			Assert.That(game.GetComponent<UIDocument>().worldSpaceSize, Is.EqualTo(new Vector2(400, 400)));
			var collider = game.GetComponent<BoxCollider>();
			Assert.That(collider.size.y, Is.EqualTo(4f).Within(.001f));
			float gameTop = game.TransformPoint(collider.center + Vector3.up * collider.size.y * .5f).y;
			foreach (var document in prefab.GetComponentsInChildren<UIDocument>(true))
			{
				var panelCollider = document.GetComponent<BoxCollider>();
				float panelTop = document.transform.TransformPoint(panelCollider.center + Vector3.up * panelCollider.size.y * .5f).y;
				Assert.That(panelTop, Is.EqualTo(gameTop).Within(.001f), document.name);
			}
		}

		[UnityTest]
		public IEnumerator StartWithoutBothBasesWarnsAndKeepsMatchSettings()
		{
			Call("SelectTab", GameMenu.Tab.Match);
			Call("OnNetcodeStateChanged", NetcodeState.Connected);
			var start = root.Q<Button>("start-button");
			var score = root.Q<DetentField>("score-target-field");
			score.value = 17;
			Assert.That(start.enabledSelf, Is.True);
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));

			yield return Layout();
			Press(start);
			Assert.That(match.CurrentPage.name, Is.EqualTo(MatchStartWarningsBinder.PageName));
			Assert.That(Shown("missing-bases-warning"), Is.True);
			Assert.That(Shown("missing-flags-warning"), Is.False);
			match.GoBack();
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));

			AddBase(Teams.Red);
			Call("StartMatchUnlessWarned");
			Assert.That(match.CurrentPage.name, Is.EqualTo(MatchStartWarningsBinder.PageName));
			yield return Layout();
			Press(root.Q<Button>("match-warnings-dismiss-button"));
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));

			AddBase(Teams.Blue);
			Call("StartMatchUnlessWarned");
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
			Assert.That(score.value, Is.EqualTo(17));
			Assert.That(start.enabledSelf, Is.True);
		}

		[Test]
		public void FlagCapturePointsWarnUntilTheMapHasAFlag()
		{
			AddBase(Teams.Red);
			AddBase(Teams.Blue);
			Call("SelectTab", GameMenu.Tab.Match);
			var points = root.Q<DetentField>("points-field");

			points.value = 0;
			Call("StartMatchUnlessWarned");
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"), "Kills need no flag.");

			points.value = 1;
			Call("StartMatchUnlessWarned");
			Assert.That(match.CurrentPage.name, Is.EqualTo(MatchStartWarningsBinder.PageName));
			Assert.That(Shown("missing-bases-warning"), Is.False);
			Assert.That(Shown("missing-flags-warning"), Is.True);

			AddFlag(Teams.Red);
			Assert.That(MapRequirements.HasAnyFlag, Is.True);
			Call("StartMatchUnlessWarned");
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
		}

		[Test]
		public void OneTeamsFlagStartsOnlyWithNextRoundRespawn()
		{
			const int nextRound = 0, atBase = 1;
			AddBase(Teams.Red);
			AddBase(Teams.Blue);
			AddFlag(Teams.Red);
			Call("SelectTab", GameMenu.Tab.Match);
			root.Q<DetentField>("points-field").value = 1;
			var respawn = root.Q<DetentField>("respawn-field");

			respawn.value = atBase;
			Call("StartMatchUnlessWarned");
			Assert.That(match.CurrentPage.name, Is.EqualTo(MatchStartWarningsBinder.PageName));
			Assert.That(Shown("missing-flags-warning"), Is.False);
			Assert.That(Shown("missing-both-team-flags-warning"), Is.True);
			match.GoBack();

			respawn.value = nextRound;
			Call("StartMatchUnlessWarned");
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));

			respawn.value = atBase;
			AddFlag(Teams.Blue);
			Call("StartMatchUnlessWarned");
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
		}

		[Test]
		public void TimedOutRoundWithoutALeaderGoesToTheTeamDefendingTheOnlyFlags()
		{
			MatchSettings flagCaptures = MatchSettings.DemoGame();
			flagCaptures.pointsPerKill = 0;
			flagCaptures.pointsPerFlagCapture = 1;
			MatchSettings kills = MatchSettings.DemoGame();

			Assert.That(TimeoutWinner(flagCaptures), Is.EqualTo(0), "No flags: draw.");

			AddFlag(Teams.Blue);
			Assert.That(MapRequirements.TeamDefendingTheOnlyFlags, Is.EqualTo(Teams.Blue));
			Assert.That(TimeoutWinner(flagCaptures), Is.EqualTo(Teams.Blue));
			Assert.That(TimeoutWinner(kills), Is.EqualTo(0), "Flags decide nothing when kills earn the points.");

			AddFlag(Teams.Red);
			Assert.That(MapRequirements.TeamDefendingTheOnlyFlags, Is.EqualTo(0));
			Assert.That(TimeoutWinner(flagCaptures), Is.EqualTo(0), "Both teams attack: draw.");
		}

		[Test]
		public void MissingBasesAndFlagsAreListedTogether()
		{
			Call("SelectTab", GameMenu.Tab.Match);
			root.Q<DetentField>("points-field").value = 1;
			Call("StartMatchUnlessWarned");
			Assert.That(Shown("missing-bases-warning"), Is.True);
			Assert.That(Shown("missing-flags-warning"), Is.True);
		}

		[Test]
		public void RequirementsFollowTeamChangesAndExcludeInactiveBases()
		{
			var red = AddBase(Teams.Red);
			var other = AddBase(Teams.Red);
			AddBase(0);
			Assert.That(MapRequirements.HasBothTeamBases, Is.False);
			SetTeam(other, Teams.Blue);
			Assert.That(MapRequirements.HasBothTeamBases, Is.True);
			red.enabled = false;
			Assert.That(MapRequirements.HasBothTeamBases, Is.False);
			red.enabled = true;
			other.gameObject.SetActive(false);
			Assert.That(MapRequirements.HasBothTeamBases, Is.False);
			other.gameObject.SetActive(true);
			Assert.That(MapRequirements.HasBothTeamBases, Is.True);
		}

		[UnityTest]
		public IEnumerator WarningEditMapButtonSelectsMapsAndEntersObjectEditing()
		{
			Call("SelectTab", GameMenu.Tab.Match);
			MapEditor.Tools.MapEditorTool.SetMode(MapEditor.Tools.MapEditorTool.Mode.Tags);
			Call("StartMatchUnlessWarned");
			yield return Layout();
			Press(root.Q<Button>("match-warnings-edit-map-button"));
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.False);
			Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.True);
			Assert.That(MapEditor.Tools.MapEditorTool.CurrentMode, Is.EqualTo(MapEditor.Tools.MapEditorTool.Mode.Move));
			Call("SelectTab", GameMenu.Tab.Match);
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
		}

		[Test]
		public void StartWarningIsGoneAfterAMatchStartedElsewhereEnds()
		{
			Call("StartMatchUnlessWarned");
			Assert.That(match.CurrentPage.name, Is.EqualTo(MatchStartWarningsBinder.PageName));
			Call("OnMatchStateChanged", MatchState.Playing);
			Assert.That(match.CurrentPage.name, Is.EqualTo("playing-page"));
			Call("OnMatchStateChanged", MatchState.NotPlaying);
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
		}

		[UnityTest]
		public IEnumerator DemoPreservesEditingAndYieldsToMatchUntilItEnds()
		{
			MapEditor.MapEditor.SetActive(true);
			var demo = root.Q<NavView>("demo-nav");
			for (int visit = 0; visit < 3; visit++)
			{
				yield return Layout();
				Press(root.Q<Button>("demo-tab"));
				Assert.That(MapEditor.MapEditor.IsActive, Is.False);
				Assert.That(demo.ClassListContains("game-menu-hidden"), Is.False);
				Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
				Assert.That(match.ClassListContains("game-menu-hidden"), Is.True);
				Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
				Call("SelectTab", GameMenu.Tab.Maps);
				Assert.That(MapEditor.MapEditor.IsActive, Is.True);
			}
			Call("SelectTab", GameMenu.Tab.Demo);
			foreach (var state in new[] { MatchState.Mustering, MatchState.Countdown, MatchState.Playing, MatchState.RoundBreak })
			{
				Call("OnMatchStateChanged", state);
				Assert.That(demo.ClassListContains("game-menu-hidden"), Is.True);
				Assert.That(match.ClassListContains("game-menu-hidden"), Is.False);
				Assert.That(match.CurrentPage.name, Is.EqualTo("playing-page"));
				Assert.That(root.Q<Button>("demo-tab").enabledSelf, Is.False);
				Assert.That(root.Q<Button>("maps-tab").enabledSelf, Is.False);
				Assert.That(MapEditor.MapEditor.IsActive, Is.False);
				Call("SelectTab", GameMenu.Tab.Demo);
				Assert.That(demo.ClassListContains("game-menu-hidden"), Is.True);
			}
			Call("OnMatchStateChanged", MatchState.NotPlaying);
			Assert.That(root.Q<Button>("demo-tab").enabledSelf, Is.True);
			Call("SelectTab", GameMenu.Tab.Demo);
			Assert.That(demo.ClassListContains("game-menu-hidden"), Is.False);
		}

		[UnityTest]
		public IEnumerator DemoUsesDatabaseWeaponsAndOwnsNetworkMeshButtons()
		{
			Assert.That(SessionWeaponSwitcher.Instance, Is.Null);
			for (int cycle = 0; cycle < 3; cycle++)
			{
				if (cycle > 0) { Call("OnDisable"); Bind(); }
				Call("SelectTab", GameMenu.Tab.Demo);
				yield return Layout();
				var weapons = root.Q("demo-weapons");
				Assert.That(weapons.childCount, Is.EqualTo(database.Count));
				Assert.That(weapons.enabledSelf, Is.False);
				for (int id = 0; id < database.Count; id++)
					Assert.That(weapons.Q<Button>($"demo-weapon-{id}").text, Is.EqualTo(database.GetWeapon(id).name));
				var demo = root.Q<NavView>("demo-nav");
				Assert.That(demo.Q<Button>("show-debug-mesh-for-everyone").text, Is.EqualTo("Show mesh for everyone"));
				Assert.That(demo.Q<Button>("hide-debug-mesh-for-everyone").enabledSelf, Is.False);
			}
			var settings = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				"Assets/Anaglyph/LaserTag/Interface/Main Menu/Settings/SettingsMenu.uxml").CloneTree();
			Assert.That(settings.Q<Button>("show-debug-mesh-for-everyone"), Is.Null);
			Assert.That(settings.Q<Button>("hide-debug-mesh-for-everyone"), Is.Null);
			Assert.That(settings.Q<Toggle>("show-debug-mesh-toggle"), Is.Not.Null);
		}

		private TeamBase AddBase(byte team)
		{
			var baseObject = new GameObject("Team base test");
			baseObject.SetActive(false);
			baseObject.AddComponent<NetworkObject>();
			var teamOwner = baseObject.AddComponent<TeamOwner>();
			var homeBase = baseObject.AddComponent<TeamBase>();
			bases.Add(homeBase);
			typeof(TeamBase).GetField("teamOwner", Private).SetValue(homeBase, teamOwner);
			SetTeam(homeBase, team);
			baseObject.SetActive(true);
			if (!TeamBase.AllBases.Contains(homeBase))
				typeof(TeamBase).GetMethod("Awake", Private).Invoke(homeBase, null);
			return homeBase;
		}

		private void AddFlag(byte team)
		{
			var flagObject = new GameObject("Flag test");
			flagObject.SetActive(false);
			flagObject.AddComponent<NetworkObject>();
			var teamOwner = flagObject.AddComponent<TeamOwner>();
			var flag = flagObject.AddComponent<TeamFlag>();
			typeof(TeamFlag).GetField("teamOwner", Private).SetValue(flag, teamOwner);
			typeof(NetworkVariable<byte>).GetField("m_InternalValue", Private).SetValue(teamOwner.teamSync, team);
			flagObject.SetActive(true);
			flags.Add(flagObject);
		}

		private static byte TimeoutWinner(MatchSettings settings)
		{
			const BindingFlags privateStatic = BindingFlags.Static | BindingFlags.NonPublic;
			object settingsSync = typeof(MatchReferee).GetField("settingsSync", privateStatic).GetValue(null);
			FieldInfo current = settingsSync.GetType().GetField("current", Private);
			object previous = current.GetValue(settingsSync);
			current.SetValue(settingsSync, settings);
			try
			{
				return (byte)typeof(MatchReferee).GetMethod("GetTimeoutWinner", privateStatic).Invoke(null, null);
			}
			finally
			{
				current.SetValue(settingsSync, previous);
			}
		}

		private bool Shown(string elementName) =>
			match.Q(elementName).style.display.value == DisplayStyle.Flex;

		private static void SetTeam(TeamBase homeBase, byte team) =>
			typeof(NetworkVariable<byte>).GetField("m_InternalValue", Private)
				.SetValue(homeBase.TeamOwner.teamSync, team);

		private static IEnumerator Layout()
		{
			for (int frame = 0; frame < 5; frame++) yield return null;
		}

		private static void Press(Button button)
		{
			using (var down = PointerDownEvent.GetPooled(new Event
				{ type = EventType.MouseDown, button = 0, mousePosition = button.worldBound.center }))
			{
				down.target = button;
				button.SendEvent(down);
			}
			using (var up = PointerUpEvent.GetPooled(new Event
				{ type = EventType.MouseUp, button = 0, mousePosition = button.worldBound.center }))
			{
				up.target = button;
				button.SendEvent(up);
			}
		}
	}
}
