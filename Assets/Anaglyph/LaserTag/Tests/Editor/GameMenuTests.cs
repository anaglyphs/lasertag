using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player.Teams;
using Anaglyph.LaserTag.Weapons;
using Anaglyph.Netcode.SyncVariables;
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
	public class GameMenuTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private GameObject owner;
		private GameMenu menu;
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
			owner = new GameObject("Game menu test");
			owner.SetActive(false);
			owner.AddComponent<MapManagerUI>();
			menu = owner.AddComponent<GameMenu>();
			Bind();
		}

		private void Bind()
		{
			Call("InitializeUI", root);
			MapEditor.MapEditor.ActiveChanged += Handler<Action<bool>>("OnMapEditorStateChanged");
			MapEditor.MapEditor.TagRegistrationRequested += Handler<Action>("ShowSpaceAlignment");
			Call("OnMatchStateChanged", MatchState.NotPlaying);
			Call("ShowSection", GameMenu.Section.Match);
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
		public IEnumerator MatchPagesShowTheirHeaderAndThereIsNoTabBar()
		{
			yield return Layout();
			Assert.That(root.Q("game-menu-tabs"), Is.Null);
			Assert.That(match.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
			Assert.That(maps.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
			var header = match.CurrentPage.Q<NavHeader>();
			Assert.That(header.Title, Is.EqualTo("Match"));
			Assert.That(header.resolvedStyle.visibility, Is.EqualTo(Visibility.Visible));
			Assert.That(header.worldBound.height, Is.GreaterThan(0));
			Assert.That(root.Q("game-menu-panel").layout.size, Is.EqualTo(new Vector2(400, 400)));
		}

		[TestCase(MatchState.Mustering)]
		[TestCase(MatchState.Countdown)]
		[TestCase(MatchState.Playing)]
		[TestCase(MatchState.RoundBreak)]
		public void ActiveMatchClosesTheEditorAndRejectsSpaceSettings(MatchState state)
		{
			Call("ShowMatchObjects");
			Assert.That(MapEditor.MapEditor.IsActive, Is.True);
			Call("OnMatchStateChanged", state);
			Assert.That(match.CurrentPage.name, Is.EqualTo("playing-page"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			MapEditor.MapEditor.RequestTagRegistration();
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Call("OnMatchStateChanged", MatchState.NotPlaying);
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
		}

		[UnityTest]
		public IEnumerator ErrorsPreserveThePlacementPage()
		{
			Call("ShowMatchObjects");
			var shell = root.Q<NavView>("game-nav");
			using var errors = new MenuErrorPresenter(MenuErrorArea.Game);
			errors.Bind(shell);
			errors.Show(new MenuError(MenuErrorArea.Game, "Test error", "Test details"));
			Assert.That(shell.CurrentPage.name, Is.EqualTo("error-modal"));
			yield return Layout();
			Press(root.Q<Button>("dismiss-error-button"));
			Assert.That(shell.CurrentPage.name, Is.EqualTo("home-page"));
			Assert.That(match.CurrentPage.name, Is.EqualTo(MatchObjectsBinder.PageName));
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
		}

		[Test]
		public void RebindingKeepsThePlacementPageAndResumesTheEditor()
		{
			Call("ShowMatchObjects");
			for (int cycle = 0; cycle < 3; cycle++)
			{
				Call("OnDisable");
				Assert.That(MapEditor.MapEditor.IsActive, Is.False);
				Bind();
				Assert.That(match.CurrentPage.name, Is.EqualTo(MatchObjectsBinder.PageName));
				Assert.That(MapEditor.MapEditor.IsActive, Is.True);
			}
		}

		[Test]
		public void PanelFitsInsideXrTouchArea()
		{
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
		public IEnumerator NextOpensObjectPlacementAndStartWaitsForBothBases()
		{
			var next = root.Q<Button>("start-button");
			var start = root.Q<Button>("start-match-button");
			var score = root.Q<DetentField>("score-target-field");
			score.value = 17;
			Assert.That(next.text, Is.EqualTo("Next"));

			yield return Layout();
			Press(next);
			Assert.That(match.CurrentPage.name, Is.EqualTo(MatchObjectsBinder.PageName));
			Assert.That(MapEditor.MapEditor.IsActive, Is.True);
			Assert.That(Shown("missing-bases-warning"), Is.True);
			Assert.That(Shown("missing-flags-warning"), Is.False);
			Assert.That(Shown("place-objects-button"), Is.False);
			Assert.That(start.enabledSelf, Is.False);

			AddBase(Teams.Red);
			Call("RefreshMatchObjects");
			Assert.That(Shown("missing-bases-warning"), Is.True);
			AddBase(Teams.Blue);
			Call("RefreshMatchObjects");
			Assert.That(Shown("missing-bases-warning"), Is.False);
			Assert.That(start.enabledSelf, Is.True);
			yield return Layout();
			Press(start);
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Assert.That(score.value, Is.EqualTo(17));
		}

		[Test]
		public void FlagCapturePointsWaitUntilAFlagIsPlaced()
		{
			AddBase(Teams.Red);
			AddBase(Teams.Blue);
			var points = root.Q<DetentField>("points-field");
			var start = root.Q<Button>("start-match-button");

			points.value = 0;
			Call("ShowMatchObjects");
			Assert.That(start.enabledSelf, Is.True, "Kills need no flag.");

			points.value = 1;
			Call("RefreshMatchObjects");
			Assert.That(Shown("missing-bases-warning"), Is.False);
			Assert.That(Shown("missing-flags-warning"), Is.True);
			Assert.That(start.enabledSelf, Is.False);

			AddFlag(Teams.Red);
			Assert.That(MapRequirements.HasAnyFlag, Is.True);
			Call("RefreshMatchObjects");
			Assert.That(start.enabledSelf, Is.True);
		}

		[Test]
		public void OneTeamsFlagStartsOnlyWithNextRoundRespawn()
		{
			const int nextRound = 0, atBase = 1;
			AddBase(Teams.Red);
			AddBase(Teams.Blue);
			AddFlag(Teams.Red);
			root.Q<DetentField>("points-field").value = 1;
			var respawn = root.Q<DetentField>("respawn-field");
			var start = root.Q<Button>("start-match-button");

			respawn.value = atBase;
			Call("ShowMatchObjects");
			Assert.That(Shown("missing-flags-warning"), Is.False);
			Assert.That(start.enabledSelf, Is.False);

			respawn.value = nextRound;
			Call("RefreshMatchObjects");
			Assert.That(start.enabledSelf, Is.True);

			respawn.value = atBase;
			AddFlag(Teams.Blue);
			Call("RefreshMatchObjects");
			Assert.That(start.enabledSelf, Is.True);
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
			root.Q<DetentField>("points-field").value = 1;
			Call("ShowMatchObjects");
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
		public IEnumerator ClosingTheEditorKeepsThePlacementPageAndPlaceObjectsReopensIt()
		{
			Call("ShowMatchObjects");
			Assert.That(MapEditor.MapEditor.IsActive, Is.True);
			MapEditor.MapEditor.SetActive(false);
			Assert.That(match.CurrentPage.name, Is.EqualTo(MatchObjectsBinder.PageName));
			Assert.That(Shown("place-objects-button"), Is.True);
			yield return Layout();
			Press(root.Q<Button>("place-objects-button"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.True);
			Assert.That(Shown("place-objects-button"), Is.False);
			match.GoBack();
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
		}

		[Test]
		public void PlacementEndsWhenTheSessionDoes()
		{
			Call("ShowMatchObjects");
			Assert.That(MapEditor.MapEditor.IsActive, Is.True);
			Call("OnNetcodeStateChanged", NetcodeState.Disconnected);
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
		}

		[Test]
		public void PlacementIsLeftWhenAMatchStartsElsewhere()
		{
			Call("ShowMatchObjects");
			Assert.That(match.CurrentPage.name, Is.EqualTo(MatchObjectsBinder.PageName));
			Call("OnMatchStateChanged", MatchState.Playing);
			Assert.That(match.CurrentPage.name, Is.EqualTo("playing-page"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Call("OnMatchStateChanged", MatchState.NotPlaying);
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
		}

		[Test]
		public void TagRegistrationOpensSpaceSettingsAndBackReturnsToMatch()
		{
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
			Assert.That(match.ClassListContains("game-menu-hidden"), Is.False);

			MapEditor.MapEditor.RequestTagRegistration();
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.False);
			Assert.That(maps.CurrentPage.name, Is.EqualTo("space-details"));
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));

			maps.GoBack();
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True, "Back from space settings returns to the match pages.");
			Assert.That(match.ClassListContains("game-menu-hidden"), Is.False);
		}

		[Test]
		public void EditorOpenedElsewhereShowsObjectPlacement()
		{
			MapEditor.MapEditor.SetActive(true);
			Assert.That(match.CurrentPage.name, Is.EqualTo(MatchObjectsBinder.PageName));
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
			Assert.That(MapEditor.MapEditor.IsActive, Is.True);
		}

		[UnityTest]
		public IEnumerator DemoControlsLiveInSettingsAndListTheSessionWeapons()
		{
			Assert.That(SessionWeaponSwitcher.Instance, Is.Null);
			Assert.That(SyncBus.Active, Is.False);
			var settings = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				"Assets/Anaglyph/LaserTag/Interface/Main Menu/Settings/SettingsMenu.uxml").CloneTree();
			window.rootVisualElement.Add(settings);
			var navigation = settings.Q<NavView>();
			Assert.That(settings.Q<Toggle>("demo-mode-toggle"), Is.Null);
			Assert.That(settings.Q<NavButton>("demo-button").TargetPageName, Is.EqualTo("demo-page"));
			Assert.That(root.Q("demo-weapons"), Is.Null, "The game menu has no demo page.");

			var database = AssetDatabase.LoadAssetAtPath<WeaponDatabase>("Assets/Anaglyph/LaserTag/Weapons/Weapon Database.asset");
			var switcherOwner = new GameObject("Demo controls test");
			switcherOwner.SetActive(false);
			var switcher = switcherOwner.AddComponent<SessionWeaponSwitcher>();
			var serialized = new SerializedObject(switcher);
			serialized.FindProperty("database").objectReferenceValue = database;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			typeof(SessionWeaponSwitcher).GetMethod("Awake", Private).Invoke(switcher, null);
			try
			{
				NavPage page = navigation.GetPage("demo-page");
				using var demo = new DemoControlsBinder(page);
				navigation.GoToPage("debugging-page");
				navigation.GoToPage(page);
				yield return Layout();
				var weapons = page.Q("demo-weapons");
				Assert.That(weapons.childCount, Is.EqualTo(database.Count));
				Assert.That(weapons.enabledSelf, Is.False, "Demo controls need a session.");
				for (int id = 0; id < database.Count; id++)
					Assert.That(weapons.Q<Button>($"demo-weapon-{id}").text, Is.EqualTo(database.GetWeapon(id).name));
				Assert.That(page.Q<Button>("show-debug-mesh-for-everyone").text, Is.EqualTo("Show mesh for everyone"));
				Assert.That(page.Q<Button>("hide-debug-mesh-for-everyone").enabledSelf, Is.False);
				Assert.That(page.Q<NavHeader>().Title, Is.EqualTo("Demo"));
			}
			finally
			{
				typeof(SessionWeaponSwitcher).GetMethod("OnDestroy", Private).Invoke(switcher, null);
				Object.DestroyImmediate(switcherOwner);
			}
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
