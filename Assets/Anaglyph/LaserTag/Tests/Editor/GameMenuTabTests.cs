using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player.Teams;
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

namespace Anaglyph.LaserTag.Tests
{
	public class GameMenuTabTests
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

		private void Call(string method, params object[] arguments) =>
			typeof(GameMenu).GetMethod(method, Private).Invoke(menu, arguments);
		private T Handler<T>(string method) where T : Delegate =>
			(T)Delegate.CreateDelegate(typeof(T), menu, typeof(GameMenu).GetMethod(method, Private));

		[SetUp]
		public void SetUp()
		{
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null);
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
			LocalizationSettings.SelectedLocale = previousLocale;
		}

		[UnityTest]
		public IEnumerator RepeatedTabPressesPauseEditingAndPreserveNavigationAndMatchSettings()
		{
			Assert.That(maps.CurrentPage.name, Is.EqualTo("map-manager-page"));
			Assert.That(root.Q<Button>("maps-tab").ClassListContains("game-menu-tab-selected"), Is.True);
			MapEditor.MapEditor.SetActive(true);
			var score = root.Q<SliderInt>("score-target-slider");
			score.value = 17;
			for (int visit = 0; visit < 3; visit++)
			{
				yield return Layout();
				Press(root.Q<Button>("match-tab"));
				Assert.That(MapEditor.MapEditor.IsActive, Is.False);
				Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
				Assert.That(match.CurrentPage.name, Is.EqualTo("missing-bases-modal"));
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
			Assert.That(maps.CurrentPage.name, Is.EqualTo("map-manager-page"));
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
			Call("SelectTab", true);
			MapEditor.MapEditor.RequestTagRegistration();
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
			Call("OnMatchStateChanged", MatchState.NotPlaying);
			Assert.That(root.Q<Button>("maps-tab").enabledSelf, Is.True);
			Assert.That(match.CurrentPage.name, Is.EqualTo("missing-bases-modal"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Call("SelectTab", true);
			Assert.That(MapEditor.MapEditor.IsActive, Is.True);
		}

		[Test]
		public void RegistrationSelectsMapsButAllowsSwitchingAway()
		{
			Call("SelectTab", false);
			MapEditor.MapEditor.RequestTagRegistration();
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.False);
			Assert.That(maps.CurrentPage.name, Is.EqualTo("space-details"));
			Call("SelectTab", false);
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
			Assert.That(maps.CurrentPage.name, Is.EqualTo("space-details"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
		}

		[UnityTest]
		public IEnumerator ErrorsPreserveBothTreesAndTheSelectedTab()
		{
			MapEditor.MapEditor.SetActive(true);
			Call("SelectTab", false);
			var shell = root.Q<NavView>("game-nav");
			using var errors = new MenuErrorPresenter(MenuErrorArea.Game);
			errors.Bind(shell);
			errors.Show(new MenuError(MenuErrorArea.Game, "Test error", "Test details"));
			Assert.That(shell.CurrentPage.name, Is.EqualTo("error-modal"));
			yield return Layout();
			Press(root.Q<Button>("dismiss-error-button"));
			Assert.That(shell.CurrentPage.name, Is.EqualTo("home-page"));
			Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
			Assert.That(match.CurrentPage.name, Is.EqualTo("missing-bases-modal"));
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.True);
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
		}

		[UnityTest]
		public IEnumerator RebindingPreservesPagesAndTabButtonsStillWork()
		{
			MapEditor.MapEditor.SetActive(true);
			Call("SelectTab", false);
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
		public IEnumerator TabsSitFlushBelow400PixelPanelAndFitInsideXrTouchArea()
		{
			yield return Layout();
			var panel = root.Q("game-menu-panel");
			var tabs = root.Q("game-menu-tabs");
			Assert.That(panel.layout.size, Is.EqualTo(new Vector2(400, 400)));
			Assert.That(tabs.worldBound.yMin, Is.EqualTo(panel.worldBound.yMax).Within(.1f));
			Assert.That(tabs.worldBound.xMin, Is.EqualTo(panel.worldBound.xMin + 12).Within(.1f));
			Assert.That(tabs.layout.size, Is.EqualTo(new Vector2(376, 46)));
			Assert.That(root.Q<Button>("maps-tab").worldBound.yMin, Is.EqualTo(panel.worldBound.yMax).Within(.1f));
			Assert.That(root.Q<Button>("maps-tab").text, Is.EqualTo("Maps"));
			Assert.That(root.Q<Button>("match-tab").text, Is.EqualTo("Match"));
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Anaglyph/LaserTag/Interface/Main Menu/Menu.prefab");
			var game = prefab.transform.Find("Game Panel");
			Assert.That(game.GetComponent<UIDocument>().worldSpaceSize, Is.EqualTo(new Vector2(400, 446)));
			Assert.That(game.GetComponent<BoxCollider>().size.y, Is.EqualTo(4.46f).Within(.001f));
			Assert.That(game.localPosition.y + 2.23f * game.localScale.y, Is.EqualTo(.3f).Within(.001f));
		}

		[Test]
		public void MissingBasesCannotBeDismissedAndBothColorsRestoreMatchSettings()
		{
			Call("SelectTab", false);
			var score = root.Q<SliderInt>("score-target-slider");
			score.value = 17;
			Assert.That(match.CurrentPage.ModalUserDismissible, Is.False);
			for (int attempt = 0; attempt < 3; attempt++) match.GoBack();
			Assert.That(match.CurrentPage.name, Is.EqualTo("missing-bases-modal"));
			Call("OnNetcodeStateChanged", NetcodeState.Connected);
			Assert.That(root.Q<Button>("start-button").enabledSelf, Is.False);

			AddBase(Teams.Red);
			Call("Update");
			Assert.That(match.CurrentPage.name, Is.EqualTo("missing-bases-modal"));
			var blue = AddBase(Teams.Blue);
			Call("Update");
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
			Assert.That(score.value, Is.EqualTo(17));
			Call("OnNetcodeStateChanged", NetcodeState.Connected);
			Assert.That(root.Q<Button>("start-button").enabledSelf, Is.True);

			Object.DestroyImmediate(blue.gameObject);
			Call("Update");
			Assert.That(match.CurrentPage.name, Is.EqualTo("missing-bases-modal"));
			Assert.That(root.Q<Button>("start-button").enabledSelf, Is.False);
		}

		[Test]
		public void RequirementsFollowTeamChangesAndExcludeInactiveBases()
		{
			var red = AddBase(Teams.Red);
			var other = AddBase(Teams.Red);
			AddBase(0);
			Assert.That(MapBaseRequirements.HasBothTeamBases, Is.False);
			SetTeam(other, Teams.Blue);
			Assert.That(MapBaseRequirements.HasBothTeamBases, Is.True);
			red.enabled = false;
			Assert.That(MapBaseRequirements.HasBothTeamBases, Is.False);
			red.enabled = true;
			other.gameObject.SetActive(false);
			Assert.That(MapBaseRequirements.HasBothTeamBases, Is.False);
			other.gameObject.SetActive(true);
			Assert.That(MapBaseRequirements.HasBothTeamBases, Is.True);
			SetTeam(other, Teams.Red);
			Call("Update");
			Assert.That(match.CurrentPage.name, Is.EqualTo("missing-bases-modal"));
		}

		[UnityTest]
		public IEnumerator PlaceBasesButtonSelectsMapsAndEntersObjectEditing()
		{
			Call("SelectTab", false);
			MapEditor.Tools.MapEditorTool.SetMode(MapEditor.Tools.MapEditorTool.Mode.Tags);
			yield return Layout();
			Press(root.Q<Button>("place-bases-button"));
			Assert.That(maps.ClassListContains("game-menu-hidden"), Is.False);
			Assert.That(maps.CurrentPage.name, Is.EqualTo("editing-map-page"));
			Assert.That(MapEditor.MapEditor.IsActive, Is.True);
			Assert.That(MapEditor.Tools.MapEditorTool.CurrentMode, Is.EqualTo(MapEditor.Tools.MapEditorTool.Mode.Move));
			Call("SelectTab", false);
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			Assert.That(match.CurrentPage.name, Is.EqualTo("missing-bases-modal"));
		}

		[Test]
		public void LosingBasesDuringMatchPreservesPlayingControlsUntilMatchEnds()
		{
			AddBase(Teams.Red);
			var blue = AddBase(Teams.Blue);
			Call("Update");
			Call("OnMatchStateChanged", MatchState.Playing);
			Object.DestroyImmediate(blue.gameObject);
			Call("Update");
			Assert.That(match.CurrentPage.name, Is.EqualTo("playing-page"));
			Call("OnMatchStateChanged", MatchState.NotPlaying);
			Assert.That(match.CurrentPage.name, Is.EqualTo("missing-bases-modal"));
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
