using System.Collections;
using System.Linq;
using Anaglyph.LaserTag.Interface;
using Anaglyph.Menu;
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
			Assert.That(MenuCopy.Format("Operator", "client.count", 0), Is.EqualTo("0 players connected"));
			Assert.That(MenuCopy.Format("Operator", "client.count", 1), Is.EqualTo("1 player connected"));
			Assert.That(MenuCopy.Format("Operator", "client.count", 2), Is.EqualTo("2 players connected"));
			Assert.That(MenuCopy.Format("HUD", "respawn.countdown", 2.3f), Is.EqualTo("RESPAWN: 2.3s"));
			var error = MenuCopy.String("Operator", "error.host-start", "test detail");
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
		public IEnumerator DismissingWarningsReturnsToTheSameMapEditingSubpage()
		{
			var root = Load("GameMenu");
			for (int i = 0; i < 2; i++) yield return null;
			var nav = NavView.RequireIn(root);
			var editing = nav.GetPage("editing-map-page");
			using var binder = new MapEditingMenuBinder(editing);
			void OnPageChanged(NavPage page) => binder.SetPresented(page == editing);
			nav.Changed += OnPageChanged;
			using var errors = new MenuErrorPresenter(UserErrorArea.Game);
			errors.Bind(nav);
			try
			{
				binder.ResetForEditingSession();
				nav.SetModalPresented(editing, true, 10);
				binder.ShowTagsPage();
				var nested = editing.Q<NavView>("map-editing-nav");
				UserErrors.Raise(UserErrorArea.Game, "Alignment change unavailable", "Cannot share anchors");
				UserErrors.Raise(UserErrorArea.Game, "Another warning", "Details");
				Assert.That(nav.CurrentPage.name, Is.EqualTo("error-modal"));
				Assert.That(nested.CurrentPage.name, Is.EqualTo("alignment-settings-page"));
				for (int i = 0; i < 2; i++) yield return null;
				Submit(root.Q<Button>("dismiss-error-button"));
				Assert.That(nav.CurrentPage.name, Is.EqualTo("error-modal"));
				Submit(root.Q<Button>("dismiss-error-button"));
				Assert.That(nav.CurrentPage, Is.SameAs(editing));
				Assert.That(nested.CurrentPage.name, Is.EqualTo("alignment-settings-page"));
				binder.ResetForEditingSession();
				Assert.That(nested.CurrentPage.name, Is.EqualTo("map-options-page"));
			}
			finally { nav.Changed -= OnPageChanged; }
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
			Assert.That(root.Q<Button>("show-debug-mesh-for-everyone").text, Is.EqualTo("Show mesh for everyone"));
			MenuCopy.SetVariable(version, "version", "next-build");
			for (int i = 0; i < 5; i++) yield return null;
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
			using var errors = new MenuErrorPresenter(UserErrorArea.Game);
			errors.Bind(nav);
			UserErrors.Raise(UserErrorArea.Connection, "Connection failure", "Network detail");
			Assert.That(nav.CurrentPage.name, Is.EqualTo("home-page"));
			UserErrors.Raise(UserErrorArea.Game, "Alignment failure", "First detail");
			UserErrors.Raise(UserErrorArea.Game, "Alignment failure", "First detail");
			UserErrors.Raise(UserErrorArea.Game, "Map failure", "Second detail");
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
			var root = Load("GameMenu");
			using var errors = new MenuErrorPresenter(UserErrorArea.Game);
			errors.Bind(NavView.RequireIn(root));
			UserErrors.RaiseLocalized(UserErrorArea.Game, "error.anchors-title", "error.anchors-details");
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
		public IEnumerator MatchChoiceBindingsPreserveControlOrderAndSettings()
		{
			var root = Load("GameMenu");
			var nav = NavView.RequireIn(root);
			nav.GoToPage("match-page");
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
