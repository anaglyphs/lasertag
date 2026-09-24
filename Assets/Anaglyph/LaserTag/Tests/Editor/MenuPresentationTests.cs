using System.Collections;
using System.Linq;
using Anaglyph.LaserTag.Interface;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Pseudo;
using UnityEngine.Localization.Settings;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Tests
{
	public class MenuPresentationTests
	{
		private MenuPresentationTestWindow window;
		private Locale previousLocale;
		private Locale english;

		[SetUp]
		public void SetUp()
		{
			previousLocale = LocalizationSettings.SelectedLocale;
			english = AssetDatabase.LoadAssetAtPath<Locale>("Assets/Anaglyph/LaserTag/Localization/English.asset");
			LocalizationSettings.SelectedLocale = english;
			window = ScriptableObject.CreateInstance<MenuPresentationTestWindow>();
			window.position = new Rect(100, 100, 440, 650);
			window.Show();
		}

		[TearDown]
		public void TearDown()
		{
			window.Close();
			LocalizationSettings.SelectedLocale = previousLocale;
		}

		private VisualElement Load(string menu)
		{
			var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				$"Assets/Anaglyph/LaserTag/Interface/Main Menu/{menu.Replace("Menu", "")}/{menu}.uxml").CloneTree();
			tree.style.flexGrow = 1;
			tree.MakeButtonsActOnPress();
			window.rootVisualElement.Add(tree);
			return tree;
		}

		[Test]
		public void SavedSpacePreferenceCopyDoesNotDescribeAPendingMethodSwitch()
		{
			Assert.That(MenuCopy.Format("Map", "alignment.saved-preference", "Shared spatial anchors"),
				Is.EqualTo("Space preference: Shared spatial anchors"));
			Assert.That(MenuCopy.Format("Map", "alignment.saved-preference", "TEST-METHOD"),
				Is.EqualTo("Space preference: TEST-METHOD"));
			Assert.That(MenuCopy.Get("Map", "alignment.session-scope"),
				Is.EqualTo("Changes alignment for everyone. Successful changes are saved with this space."));
		}

		[UnityTest]
		public IEnumerator RemainingDocumentsAndSmartStringsFollowLocaleChanges()
		{
			var hud = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				"Assets/Anaglyph/LaserTag/Interface/HUD/HUD.uxml").CloneTree();
			var hand = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				"Assets/Anaglyph/LaserTag/Interface/HUD/HandHUD.uxml").CloneTree();
			var op = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				"Assets/Anaglyph/LaserTag/Operator/OperatorMenu.uxml").CloneTree();
			window.rootVisualElement.Add(hud);
			window.rootVisualElement.Add(hand);
			window.rootVisualElement.Add(op);
			for (int i = 0; i < 10; i++) yield return null;
			Assert.That(hud.Q<Label>("muster-label").text, Is.EqualTo("Everyone must stand in a base\nfor the game to start!"));
			Assert.That(hud.Q<Label>("countdown-label"), Is.Not.InstanceOf<FlashingLabel>());
			Assert.That(op.Q<Button>("host-button").text, Is.EqualTo("Start hosting"));
			Assert.That(op.Q<Tab>("maps-tab").label, Is.EqualTo("Maps"));
			Assert.That(hand.Q<Anaglyph.LaserTag.Interface.HUD.ScoreDisplay>(), Is.Not.Null);
			Assert.That(MenuCopy.Format("OperatorMenu", "client.count", 0), Is.EqualTo("0 players connected"));
			Assert.That(MenuCopy.Format("OperatorMenu", "client.count", 1), Is.EqualTo("1 player connected"));
			Assert.That(MenuCopy.Format("OperatorMenu", "client.count", 2), Is.EqualTo("2 players connected"));
			Assert.That(MenuCopy.Format("HUDMenu", "respawn.countdown", 2.3f), Is.EqualTo("RESPAWN: 2.3s"));
			var error = MenuCopy.String("OperatorMenu", "error.host-start", "test detail");
			string original = error.GetLocalizedString();
			var pseudo = PseudoLocale.CreatePseudoLocale();
			try
			{
				LocalizationSettings.SelectedLocale = pseudo;
				for (int i = 0; i < 10; i++) yield return null;
				Assert.That(hud.Q<Label>("muster-label").text, Is.Not.EqualTo("Everyone must stand in a base\nfor the game to start!"));
				Assert.That(op.Q<Button>("host-button").text, Is.Not.EqualTo("Start hosting"));
				Assert.That(op.Q<Tab>("maps-tab").label, Is.Not.EqualTo("Maps"));
				Assert.That(error.GetLocalizedString(), Is.Not.EqualTo(original));
			}
			finally
			{
				LocalizationSettings.SelectedLocale = english;
				Object.DestroyImmediate(pseudo);
			}
		}

		[UnityTest]
		public IEnumerator DismissingWarningsReturnsToSpaceAlignment()
		{
			var root = Load("GameMenu");
			for (int i = 0; i < 2; i++) yield return null;
			var nav = NavView.RequireIn(root);
			var maps = root.Q<NavView>("maps-nav");
			var space = maps.GetPage("space-details");
			using var binder = new SpaceDetailsBinder(space);
			maps.GoToPage(space);
			using var errors = new MenuErrorPresenter(MenuErrorArea.Game);
			errors.Bind(nav);
			errors.Show(new MenuError(MenuErrorArea.Game, "Alignment change unavailable", "Cannot share anchors"));
			errors.Show(new MenuError(MenuErrorArea.Game, "Another warning", "Details"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("error-modal"));
			for (int i = 0; i < 2; i++) yield return null;
			Submit(root.Q<Button>("dismiss-error-button"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("error-modal"));
			Submit(root.Q<Button>("dismiss-error-button"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("home-page"));
			Assert.That(maps.CurrentPage, Is.SameAs(space));
			maps.GoBack();
			Assert.That(maps.CurrentPage.name, Is.EqualTo("map-manager-page"));
		}

		[UnityTest]
		public IEnumerator UxmlBindingsUpdateNamedVariablesAndLocale()
		{
			var root = Load("SettingsMenu");
			Label version = root.Q<Label>("version");
			MenuCopy.SetVariable(version, "version", "test-build");
			for (int i = 0; i < 10; i++) yield return null;
			Assert.That(version.text, Is.EqualTo("Version: test-build"));
			Assert.That(root.Q<NavHeader>().Title, Is.EqualTo("Settings"));
			Assert.That(root.Q<Button>("graphics-button").text, Is.EqualTo("Graphics"));
			Assert.That(root.Q<Button>("show-debug-mesh-for-everyone"), Is.Null);
			MenuCopy.SetVariable(version, "version", "next-build");
			double deadline = EditorApplication.timeSinceStartup + 2;
			while (version.text != "Version: next-build" && EditorApplication.timeSinceStartup < deadline) yield return null;
			Assert.That(version.text, Is.EqualTo("Version: next-build"));

			var pseudo = PseudoLocale.CreatePseudoLocale();
			try
			{
				LocalizationSettings.SelectedLocale = pseudo;
				for (int i = 0; i < 10; i++) yield return null;
				Assert.That(root.Q<Button>("graphics-button").text, Is.Not.EqualTo("Graphics"));
				Assert.That(version.text, Is.Not.EqualTo("Version: next-build"));
				Assert.That(((UnityEngine.Localization.SmartFormat.PersistentVariables.StringVariable)
					((LocalizedString)version.GetBinding("text"))["version"]).Value, Is.EqualTo("next-build"));
			}
			finally
			{
				LocalizationSettings.SelectedLocale = english;
				Object.DestroyImmediate(pseudo);
			}
		}

		[UnityTest]
		public IEnumerator ErrorsStayInTheirPanelAndSurviveRebindingWithoutDuplicates()
		{
			var root = Load("GameMenu");
			var nav = NavView.RequireIn(root);
			using var errors = new MenuErrorPresenter(MenuErrorArea.Game);
			errors.Bind(nav);
			errors.Show(new MenuError(MenuErrorArea.Connection, "Connection failure", "Network detail"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("home-page"));
			errors.Show(new MenuError(MenuErrorArea.Game, "Alignment failure", "First detail"));
			errors.Show(new MenuError(MenuErrorArea.Game, "Alignment failure", "First detail"));
			errors.Show(new MenuError(MenuErrorArea.Game, "Map failure", "Second detail"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("error-modal"));
			Assert.That(root.Q<Label>("error-subject").text, Is.EqualTo("Alignment failure"));
			errors.Unbind();
			errors.Bind(nav);
			for (int i = 0; i < 2; i++) yield return null;
			Submit(root.Q<Button>("dismiss-error-button"));
			Assert.That(root.Q<Label>("error-subject").text, Is.EqualTo("Map failure"));
			Submit(root.Q<Button>("dismiss-error-button"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("home-page"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("home-page"));
		}

		[UnityTest]
		public IEnumerator PendingLocalizedErrorsFollowLocaleChanges()
		{
			var root = Load("ConnectionMenu");
			using var errors = new MenuErrorPresenter(MenuErrorArea.Connection);
			errors.Bind(NavView.RequireIn(root));
			errors.Show(new MenuError(MenuErrorArea.Connection, "error.anchors-title", "error.anchors-details", "Map"));
			string original = root.Q<Label>("error-subject").text;
			Assert.That(original, Is.EqualTo("Shared spatial anchors unavailable"));
			var pseudo = PseudoLocale.CreatePseudoLocale();
			try
			{
				LocalizationSettings.SelectedLocale = pseudo;
				for (int i = 0; i < 5; i++) yield return null;
				Assert.That(root.Q<Label>("error-subject").text, Is.Not.EqualTo(original));
			}
			finally
			{
				LocalizationSettings.SelectedLocale = english;
				Object.DestroyImmediate(pseudo);
			}
		}

		[UnityTest]
		public IEnumerator ProducerEventsReachOnlyTheirPanelAndDisposeUnsubscribes()
		{
			var game = Load("GameMenu");
			var connection = Load("ConnectionMenu");
			var gameNav = NavView.RequireIn(game);
			var connectionNav = NavView.RequireIn(connection);
			using var gameErrors = new MenuErrorPresenter(MenuErrorArea.Game);
			using var connectionErrors = new MenuErrorPresenter(MenuErrorArea.Connection);
			gameErrors.Bind(gameNav);
			connectionErrors.Bind(connectionNav);
			var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
			var servicesError = typeof(NetcodeManagement).GetMethod("RaiseServicesError", flags);
			var alignmentError = typeof(LaserTagMapCoordinator).GetMethod("ReportAlignmentRejection", flags);
			var anchorError = typeof(LaserTagMapCoordinator).GetMethod("OnAnchorError", flags);

			servicesError.Invoke(null, null);
			Assert.That(connectionNav.CurrentPage.name, Is.EqualTo("error-modal"));
			Assert.That(connection.Q<Label>("error-subject").text, Is.EqualTo(MenuCopy.Get("ConnectionMenu", "error.relay-title")));
			Assert.That(gameNav.CurrentPage.name, Is.Not.EqualTo("error-modal"));
			for (int i = 0; i < 2; i++) yield return null;
			Submit(connection.Q<Button>("dismiss-error-button"));

			foreach (var error in new[] { SpatialAnchorColocationConstraintProvider.Error.SharingFailed,
				SpatialAnchorColocationConstraintProvider.Error.SharingUnsupported })
			{
				anchorError.Invoke(null, new object[] { error });
				string key = error == SpatialAnchorColocationConstraintProvider.Error.SharingFailed ? "share" : "anchors";
				Assert.That(connectionNav.CurrentPage.name, Is.EqualTo("error-modal"));
				Assert.That(connection.Q<Label>("error-subject").text, Is.EqualTo(MenuCopy.Get("Map", $"error.{key}-title")));
				Assert.That(gameNav.CurrentPage.name, Is.Not.EqualTo("error-modal"));
				for (int i = 0; i < 2; i++) yield return null;
				Submit(connection.Q<Button>("dismiss-error-button"));
				Assert.That(connectionNav.CurrentPage.name, Is.EqualTo("home-page"));
			}

			alignmentError.Invoke(null, null);
			Assert.That(gameNav.CurrentPage.name, Is.EqualTo("error-modal"));
			Assert.That(game.Q<Label>("error-subject").text, Is.EqualTo(MenuCopy.Get("Map", "error.alignment-title")));
			Assert.That(connectionNav.CurrentPage.name, Is.EqualTo("home-page"));

			gameErrors.Dispose();
			connectionErrors.Dispose();
			var pending = typeof(MenuErrorPresenter).GetField("pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			((System.Collections.Generic.Queue<MenuError>)pending.GetValue(gameErrors)).Clear();
			((System.Collections.Generic.Queue<MenuError>)pending.GetValue(connectionErrors)).Clear();
			servicesError.Invoke(null, null);
			alignmentError.Invoke(null, null);
			anchorError.Invoke(null, new object[] { SpatialAnchorColocationConstraintProvider.Error.SharingFailed });
			Assert.That((System.Collections.ICollection)pending.GetValue(gameErrors), Is.Empty);
			Assert.That((System.Collections.ICollection)pending.GetValue(connectionErrors), Is.Empty);
		}

		[UnityTest]
		public IEnumerator MatchChoiceBindingsPreserveControlOrderAndSettings()
		{
			var root = Load("GameMenu");
			root.Q<NavView>("maps-nav").AddToClassList("game-menu-hidden");
			root.Q<NavView>("match-nav").RemoveFromClassList("game-menu-hidden");
			var binder = new MatchSettingsBinder(root);
			for (int i = 0; i < 10; i++) yield return null;
			var win = root.Q<RadioButtonGroup>("win-by-radio");
			Assert.That(win.Query<RadioButton>().ToList().Select(r => r.text),
				Is.EqualTo(new[] { "Time", "Points", "Either" }));
			win.value = 1;
			Assert.That(binder.Settings.CheckWinByScore(), Is.True);
			Assert.That(binder.Settings.CheckWinByTimer(), Is.False);
			win.value = 2;
			Assert.That(binder.Settings.CheckWinByScore(), Is.True);
			Assert.That(binder.Settings.CheckWinByTimer(), Is.True);
			Assert.That(root.Q<Button>("start-button").text, Is.EqualTo("Start match"));
			Assert.That(root.Q<Button>("start-button").worldBound.height, Is.GreaterThan(0));
		}

		private static void Submit(Button button)
		{
			using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = button.worldBound.center }))
				button.SendEvent(down);
			using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = button.worldBound.center }))
				button.SendEvent(up);
		}
	}

	public sealed class MenuPresentationTestWindow : EditorWindow { }
}
