using System.Collections;
using System.Linq;
using System.Reflection;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Maps;
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
	public class MapMenuCompositionTests
	{
		private const string sharedPath = "Assets/Anaglyph/LaserTag/Interface/Shared/Game/";
		private MapMenuCompositionTestWindow window;
		private Locale previousLocale;
		[SetUp]
		public void SetUp()
		{
			previousLocale = LocalizationSettings.SelectedLocale;
			LocalizationSettings.SelectedLocale = AssetDatabase.LoadAssetAtPath<Locale>(
				"Assets/Anaglyph/LaserTag/Localization/English.asset");
			window = ScriptableObject.CreateInstance<MapMenuCompositionTestWindow>();
			window.position = new Rect(100, 100, 1200, 900);
			window.Show();
		}

		[TearDown]
		public void TearDown()
		{
			window.Close();
			LocalizationSettings.SelectedLocale = previousLocale;
		}

		private VisualElement Load(string path)
		{
			var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
			Assert.That(asset, Is.Not.Null, path);
			var root = asset.CloneTree();
			root.style.flexGrow = 1;
			window.rootVisualElement.Add(root);
			return root;
		}

		[UnityTest]
		public IEnumerator OperatorComposesSharedMapFieldsWithoutHeadsetTools()
		{
			var desktop = Load("Assets/Anaglyph/LaserTag/Operator/OperatorMenu.uxml");
			var headset = Load("Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMenu.uxml");
			var palette = Load("Assets/Anaglyph/LaserTag/MapEditor/MapEditorPalette.uxml");
			for (int i = 0; i < 2; i++) yield return null;

			foreach (var root in new[] { desktop, headset })
			{
				Assert.That(root.Q("map-name-section")?.Q<TextField>("map-name-field"), Is.Not.Null);
				Assert.That(root.Q("alignment-method-section")?.Q<DropdownField>("colocation-method-field"), Is.Not.Null);
				var catalog = root.Q("map-catalog-section");
				Assert.That(catalog, Is.Not.Null);
				Assert.That(catalog.Q<ScrollView>("map-list"), Is.Not.Null);
				foreach (string action in new[] { "new-map-button", "load-map-button", "delete-map-button" })
					Assert.That(catalog.Q<Button>(action), Is.Not.Null, action);
			}

			var editing = desktop.Q<NavPage>("map-editing-page");
			Assert.That(editing, Is.Not.Null);
			foreach (string section in new[] { "map-name-section", "alignment-method-section", "tag-configuration-section" })
				Assert.That(editing.Q(section), Is.Not.Null, "Desktop settings share a single page: " + section);
			Assert.That(desktop.Q("tag-configuration-section")?.Q<Slider>("tag-size-slider"), Is.Not.Null);
			Assert.That(desktop.Q("tag-configuration-section")?.Q<Button>("unregister-all-tags-button"), Is.Not.Null);
			Assert.That(headset.Q("tag-configuration-section"), Is.Null);
			Assert.That(headset.Q("setup-tags-button"), Is.Not.Null);
			foreach (string control in new[] { "probe-maps-button", "measure-tag-size-button", "tag-measurement-hint", "map-editing-nav" })
				Assert.That(desktop.Q(control), Is.Null, "Desktop must not instantiate " + control);
			Assert.That(palette.Q<Slider>("tag-size-slider"), Is.Not.Null);
			Assert.That(palette.Q<Button>("measure-tag-size-button"), Is.Not.Null);
			Assert.That(palette.Q<Button>("unregister-all-tags-button"), Is.Not.Null);
			var done = palette.Q<Button>("done-button");
			Assert.That(done, Is.Not.Null);
			Assert.That(done.GetFirstAncestorOfType<NavView>(), Is.Null,
				"Done remains visible outside the mode-specific navigation pages.");
		}

		[TestCase(MapEditorTool.Mode.Move, "objects-page")]
		[TestCase(MapEditorTool.Mode.Place, "objects-page")]
		[TestCase(MapEditorTool.Mode.Tags, "tags-page")]
		[TestCase(MapEditorTool.Mode.MeasureTagSize, "measure-tag-size-page")]
		public void PaletteModeMapsToOnlyItsAvailablePage(MapEditorTool.Mode mode, string expectedPageName)
		{
			MethodInfo pageNameForMode = typeof(PaletteMenu).GetMethod(
				"PageNameForMode", BindingFlags.NonPublic | BindingFlags.Static);
			Assert.That(pageNameForMode, Is.Not.Null);
			Assert.That(pageNameForMode.Invoke(null, new object[] { mode }), Is.EqualTo(expectedPageName));
		}

		[UnityTest]
		public IEnumerator SharedMapFieldsBindAloneAndOperatorErrorsReturnToFlatSettings()
		{
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null, "This test must not change an active map.");
			var catalog = Load(sharedPath + "MapCatalog.uxml");
			var name = Load(sharedPath + "MapNameField.uxml");
			var tags = Load(sharedPath + "TagConfiguration.uxml");
			foreach (var fragment in new[] { catalog, name, tags })
			{
				Assert.That(fragment.Q<NavView>(), Is.Null);
				foreach (string tool in new[] { "probe-maps-button", "setup-tags-button", "measure-tag-size-button", "tag-measurement-hint" })
					Assert.That(fragment.Q(tool), Is.Null, tool);
			}
			for (int i = 0; i < 2; i++) yield return null;
			for (int binding = 0; binding < 2; binding++)
			{
				using var nameBinder = new MapNameBinder(name.Q("map-name-section"));
				using var tagBinder = new TagConfigurationBinder(tags.Q("tag-configuration-section"));
				nameBinder.Refresh();
				tagBinder.Refresh();
				Assert.That(name.Q<TextField>("map-name-field").enabledSelf, Is.False);
				Assert.That(tags.Q<Slider>("tag-size-slider").enabledSelf, Is.False);
				Assert.That(tags.Q<Button>("unregister-all-tags-button").enabledSelf, Is.False);
			}
			catalog.RemoveFromHierarchy();
			name.RemoveFromHierarchy();
			tags.RemoveFromHierarchy();

			var desktop = Load("Assets/Anaglyph/LaserTag/Operator/OperatorMenu.uxml");
			desktop.MakeButtonsActOnPress();
			desktop.Q<TabView>("tabs").activeTab = desktop.Q<Tab>("maps-tab");
			var navigation = desktop.Q<NavView>("maps-nav");
			var settings = navigation.GetPage("map-editing-page");
			navigation.GoToPage(settings);
			using var errors = new MenuErrorPresenter(UserErrorArea.Game);
			errors.Bind(navigation);
			UserErrors.Raise(UserErrorArea.Game, "Alignment change unavailable", "Test detail");
			Assert.That(navigation.CurrentPage.name, Is.EqualTo("error-modal"));
			for (int i = 0; i < 2; i++) yield return null;
			var dismiss = navigation.CurrentPage.Q<Button>("dismiss-error-button");
			using (var down = PointerDownEvent.GetPooled(new Event
				{ type = EventType.MouseDown, button = 0, mousePosition = dismiss.worldBound.center }))
				dismiss.SendEvent(down);
			using (var up = PointerUpEvent.GetPooled(new Event
				{ type = EventType.MouseUp, button = 0, mousePosition = dismiss.worldBound.center }))
				dismiss.SendEvent(up);
			Assert.That(navigation.CurrentPage, Is.SameAs(settings));
			Assert.That(settings.Q<NavView>(), Is.Null);
			foreach (string section in new[] { "map-name-section", "alignment-method-section", "tag-configuration-section" })
				Assert.That(settings.Q(section), Is.Not.Null, section);
		}

		[UnityTest]
		public IEnumerator IsolatedAlignmentFieldRebindsAndFollowsLocaleChanges()
		{
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null, "This test must not change an active map.");
			var fragment = Load(sharedPath + "AlignmentMethodField.uxml");
			Assert.That(fragment.Q<NavView>(), Is.Null);
			Assert.That(fragment.Q<Slider>("tag-size-slider"), Is.Null);
			var section = fragment.Q("alignment-method-section");
			var field = section.Q<DropdownField>("colocation-method-field");
			int staleChanges = 0;
			int changing = 0;
			int changes = 0;
			using var previous = new AlignmentMethodBinder(section);
			previous.Changed += () => staleChanges++;
			previous.Refresh();
			previous.Dispose();
			previous.Dispose();
			using var current = new AlignmentMethodBinder(section);
			current.Changing += () => changing++;
			current.Changed += () => changes++;
			current.Refresh();
			for (int i = 0; i < 10; i++) yield return null;
			AssertChoices(field);
			string[] englishChoices = field.choices.ToArray();

			var pseudo = PseudoLocale.CreatePseudoLocale();
			try
			{
				LocalizationSettings.SelectedLocale = pseudo;
				for (int i = 0; i < 10; i++) yield return null;
				AssertChoices(field);
				Assert.That(field.choices, Is.Not.EqualTo(englishChoices));
				Assert.That(changes, Is.Zero, "Refreshing translated choices is not a user change.");
				Assert.That(changing, Is.Zero);
				field.value = field.choices[(int)ColocationManager.ColocationMethod.SystemDetermined];
				Assert.That(changing, Is.EqualTo(1));
				Assert.That(changes, Is.EqualTo(1));
				Assert.That(staleChanges, Is.Zero);
				current.Dispose();
				field.value = field.choices[(int)ColocationManager.ColocationMethod.AprilTag];
				Assert.That(changing, Is.EqualTo(1));
				Assert.That(changes, Is.EqualTo(1));
				Assert.That(staleChanges, Is.Zero);
			}
			finally
			{
				LocalizationSettings.SelectedLocale = previousLocale;
				Object.DestroyImmediate(pseudo);
			}
		}

		[UnityTest]
		public IEnumerator SeparateAlignmentFragmentsKeepTheirBindingsIndependent()
		{
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null, "This test must not change an active map.");
			var first = Load(sharedPath + "AlignmentMethodField.uxml").Q("alignment-method-section");
			var second = Load(sharedPath + "AlignmentMethodField.uxml").Q("alignment-method-section");
			using var firstBinder = new AlignmentMethodBinder(first);
			using var secondBinder = new AlignmentMethodBinder(second);
			firstBinder.Refresh();
			secondBinder.Refresh();
			for (int i = 0; i < 2; i++) yield return null;
			int firstChanges = 0;
			int secondChanges = 0;
			firstBinder.Changed += () => firstChanges++;
			secondBinder.Changed += () => secondChanges++;
			var firstField = first.Q<DropdownField>("colocation-method-field");
			var secondField = second.Q<DropdownField>("colocation-method-field");
			string secondValue = secondField.value;
			firstField.value = firstField.choices[(int)ColocationManager.ColocationMethod.AprilTag];
			Assert.That(firstChanges, Is.EqualTo(1));
			Assert.That(secondChanges, Is.Zero);
			Assert.That(secondField.value, Is.EqualTo(secondValue));

			firstBinder.Dispose();
			secondField.value = secondField.choices[(int)ColocationManager.ColocationMethod.SystemDetermined];
			Assert.That(firstChanges, Is.EqualTo(1));
			Assert.That(secondChanges, Is.EqualTo(1), "Disposing one fragment must not unbind another instance.");
		}

		private static void AssertChoices(DropdownField field)
		{
			Assert.That(field.choices, Is.EqualTo(new[]
			{
				MenuCopy.Get("Game", "alignment.anchors"),
				MenuCopy.Get("Game", "alignment.tags"),
				MenuCopy.Get("Game", "alignment.system")
			}));
		}
	}

	public sealed class MapMenuCompositionTestWindow : EditorWindow { }
}
