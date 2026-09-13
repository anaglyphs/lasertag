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
				Assert.That(root.Q("alignment-settings-section")?.Q<DropdownField>("colocation-method-field"), Is.Not.Null);
				var catalog = root.Q("map-catalog-section");
				Assert.That(catalog, Is.Not.Null);
				Assert.That(catalog.Q<ScrollView>("map-list"), Is.Not.Null);
				Assert.That(catalog.Q("load-map-button"), Is.Null);
				Assert.That(catalog.Q("probe-maps-button").ClassListContains("map-field-hidden"), Is.True);
				foreach (string action in new[] { "new-map-button", "probe-maps-button", "new-space-button" })
					Assert.That(catalog.Q<Button>(action), Is.Not.Null, action);
				foreach (string removed in new[] { "space-picker", "load-space-button", "duplicate-map-button", "current-space-label", "undo-space-rebase" })
					Assert.That(root.Q(removed), Is.Null, removed);
				var details = root.Q<NavPage>("space-details");
				Assert.That(details, Is.Not.Null);
				Assert.That(details.hierarchy.parent, Is.TypeOf<NavView>());
				Assert.That(details.Q<TextField>("space-name-field"), Is.Not.Null);
				Assert.That(catalog.Q("space-name-field"), Is.Null);
				Assert.That(details.Q("delete-space-button"), Is.Null);
				var deletion = root.Q<NavPage>("delete-space-modal");
				Assert.That(deletion, Is.Not.Null);
				Assert.That(deletion.hierarchy.parent, Is.SameAs(details.hierarchy.parent));
				Assert.That(deletion.ModalUserDismissible, Is.True);
				Assert.That(deletion.Q<Button>("confirm-delete-space-button"), Is.Not.Null);
				Assert.That(deletion.Q<Button>("cancel-delete-space-button"), Is.Not.Null);
			}

			var editing = desktop.Q<NavPage>("map-editing-page");
			Assert.That(editing, Is.Not.Null);
			Assert.That(editing.Q("map-name-section"), Is.Not.Null);
			Assert.That(editing.Q("alignment-settings-section"), Is.Null);
			Assert.That(editing.Q("tag-configuration-section"), Is.Null);
			foreach (var root in new[] { desktop, headset })
			{
				Assert.That(root.Q<NavPage>("space-details").Q("alignment-settings-section"), Is.Not.Null);
				Assert.That(root.Q("map-editing-nav"), Is.Null);
				Assert.That(root.Q("tags-mode-button"), Is.Null);
				Assert.That(root.Q("alignment-settings-page"), Is.Null);
				Assert.That(root.Q("setup-tags-button"), Is.Null);
			}
			Assert.That(desktop.Q("tag-configuration-section")?.Q<Slider>("tag-size-slider"), Is.Not.Null);
			Assert.That(desktop.Q("tag-configuration-section")?.Q<Button>("unregister-all-tags-button"), Is.Not.Null);
			Assert.That(headset.Q("tag-configuration-section").ClassListContains("map-field-hidden"), Is.True);
			foreach (string control in new[] { "measure-tag-size-button", "tag-measurement-hint", "map-editing-nav" })
				Assert.That(desktop.Q(control), Is.Null, "Desktop must not instantiate " + control);
			Assert.That(palette.Q<Slider>("tag-size-slider"), Is.Not.Null);
			Assert.That(palette.Q<Button>("measure-tag-size-button"), Is.Not.Null);
			Assert.That(palette.Q<Button>("unregister-all-tags-button"), Is.Not.Null);
			var done = palette.Q<Button>("done-button");
			Assert.That(done, Is.Not.Null);
			Assert.That(done.GetFirstAncestorOfType<NavView>(), Is.Null,
				"Done remains visible outside the mode-specific navigation pages.");
		}

		[UnityTest]
		public IEnumerator OperatorCatalogKeepsMapsReachableInAShortSidebar()
		{
			window.position = new Rect(100, 100, 1280, 800);
			window.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Anaglyph/LaserTag/Interface/LaserTagRuntimeTheme.tss"));
			var root = Load("Assets/Anaglyph/LaserTag/Operator/OperatorMenu.uxml");
			root.Q<TabView>("tabs").activeTab = root.Q<Tab>("maps-tab");
			using var picker = new MapPickerBinder(true);
			picker.Bind(root.Q("map-catalog-section"), () => { }, () => { });
			for (int i = 0; i < 8; i++) root.Q<ScrollView>("map-list").Add(new Button { text = "Layout " + i });
			for (int i = 0; i < 4; i++) yield return null;
			Assert.That(root.Q("map-list").resolvedStyle.height, Is.GreaterThanOrEqualTo(180));
			Assert.That(root.Q("space-details").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
			Assert.That(root.Q("new-map-button").resolvedStyle.width, Is.GreaterThanOrEqualTo(110));
			Assert.That(root.Q("new-space-button").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
			Assert.That(root.Q<MultiColumnListView>("client-list").columns["source"], Is.Not.Null);
			var mapList = root.Q<ScrollView>("map-list");
			Assert.That(root.Q<ScrollView>(className: "operator-map-catalog-scroll"), Is.Null);
			Assert.That(mapList.contentContainer.layout.height, Is.GreaterThan(mapList.contentViewport.layout.height));
			Assert.That(root.Q("new-map-button").worldBound.yMax, Is.LessThanOrEqualTo(root.Q<NavPage>("maps-page").worldBound.yMax + 1));
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
		public IEnumerator SharedMapFieldsBindAloneAndOperatorErrorsReturnToSpaceSettings()
		{
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null, "This test must not change an active map.");
			var catalog = Load(sharedPath + "MapCatalog.uxml");
			var name = Load(sharedPath + "MapNameField.uxml");
			var tags = Load(sharedPath + "AlignmentSettings.uxml");
			foreach (var fragment in new[] { catalog, name, tags })
			{
				Assert.That(fragment.Q<NavView>(), Is.Null);
				foreach (string tool in new[] { "setup-tags-button", "measure-tag-size-button", "tag-measurement-hint" })
					Assert.That(fragment.Q(tool), Is.Null, tool);
			}
			for (int i = 0; i < 2; i++) yield return null;
			for (int binding = 0; binding < 2; binding++)
			{
				using var nameBinder = new MapNameBinder(name.Q("map-name-section"));
				using var tagBinder = new AlignmentSettingsBinder(tags.Q("alignment-settings-section"), operatorMode: true);
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
			var settings = navigation.GetPage("space-details");
			using var spaceBinder = new SpaceDetailsBinder(settings, operatorMode: true);
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
			foreach (string section in new[] { "space-name-field", "alignment-settings-section", "tag-configuration-section" })
				Assert.That(settings.Q(section), Is.Not.Null, section);
		}

		[UnityTest]
		public IEnumerator SpaceDetailsShowsTagValidationSeparatelyFromTheSavedAnchorPreference()
		{
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null);
			Assert.That(ColocationManager.Instance, Is.Null);
			Assert.That(Anaglyph.Netcode.SyncVariables.SyncBus.Current, Is.Null);
			var owner = new GameObject("Alignment status test");
			owner.SetActive(false);
			string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lasertag-status-" + System.Guid.NewGuid().ToString("N"));
			bool wasColocated = ColocationManager.IsColocated;
			const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
			var coordinator = owner.AddComponent<LaserTagMapCoordinator>();
			var manager = owner.AddComponent<ColocationManager>();
			owner.AddComponent<Unity.Netcode.NetworkObject>();
			var bus = owner.AddComponent<Anaglyph.Netcode.SyncVariables.SyncBus>();
			System.IDisposable alignment = null;
			try
			{
				var space = MapSpace.Create("Status test");
				space.preferredColocationMethod = ColocationManager.ColocationMethod.MetaSharedAnchor;
				var spaces = new MapSpaceManager(new MapSpaceStore(directory)); spaces.Load(space);
				var transition = new ReferenceAlignmentTransition(System.Guid.NewGuid(), space,
					ColocationManager.ColocationMethod.MetaSharedAnchor, ColocationManager.ColocationMethod.AprilTag,
					ReferenceTransitionIntent.ActivateTarget, true);
				transition.SetCandidate(System.Guid.NewGuid());
				var alignmentType = typeof(LaserTagMapCoordinator).Assembly.GetType("Anaglyph.LaserTag.Maps.MapSpaceAlignmentController", true);
				alignment = (System.IDisposable)System.Activator.CreateInstance(alignmentType, manager, (System.Func<bool>)(() => false));
				alignmentType.GetProperty("Transition").SetValue(alignment, transition);
				typeof(LaserTagMapCoordinator).GetField("spaces", fields).SetValue(coordinator, spaces);
				typeof(LaserTagMapCoordinator).GetField("alignment", fields).SetValue(coordinator, alignment);
				typeof(LaserTagMapCoordinator).GetProperty("Instance").SetValue(null, coordinator);
				typeof(ColocationManager).GetProperty("Instance").SetValue(null, manager);
				typeof(ColocationManager).GetProperty("IsColocated").SetValue(null, true);
				typeof(Unity.Netcode.NetworkBehaviour).GetProperty("IsSpawned").SetValue(bus, true);
				typeof(Anaglyph.Netcode.SyncVariables.SyncBus).GetProperty("Current").SetValue(null, bus);

				foreach (string path in new[] { "Assets/Anaglyph/LaserTag/Operator/OperatorMenu.uxml",
					"Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMenu.uxml" })
				{
					var root = Load(path);
					var details = root.Q<NavPage>("space-details");
					if (root.Q<TabView>("tabs") is TabView tabs) tabs.activeTab = root.Q<Tab>("maps-tab");
					details.GetFirstAncestorOfType<NavView>().GoToPage(details);
					using var binder = new AlignmentSettingsBinder(details.Q("alignment-settings-section"));
					binder.Refresh();
					for (int i = 0; i < 2; i++) yield return null;
					var status = details.Q<Label>("colocation-status");
					Assert.That(status.text, Is.EqualTo("AprilTags: Validating references; aligned using Shared spatial anchors"));
					Assert.That(status.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
					Assert.That(status.worldBound.height, Is.GreaterThan(0));
					Assert.That(details.Q<Label>("colocation-preference-status").text, Is.EqualTo("Space preference: Shared spatial anchors"));
					typeof(ColocationManager).GetProperty("IsColocated").SetValue(null, false);
					binder.Refresh();
					Assert.That(status.text, Is.EqualTo("AprilTags: Validating references; waiting for alignment"));
					typeof(ColocationManager).GetProperty("IsColocated").SetValue(null, true);
					root.RemoveFromHierarchy();
				}
			}
			finally
			{
				typeof(Unity.Netcode.NetworkBehaviour).GetProperty("IsSpawned").SetValue(bus, false);
				typeof(Anaglyph.Netcode.SyncVariables.SyncBus).GetProperty("Current").SetValue(null, null);
				alignment?.Dispose();
				typeof(LaserTagMapCoordinator).GetProperty("Instance").SetValue(null, null);
				typeof(ColocationManager).GetProperty("Instance").SetValue(null, null);
				typeof(ColocationManager).GetProperty("IsColocated").SetValue(null, wasColocated);
				Object.DestroyImmediate(owner);
				if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
			}
		}

		[UnityTest]
		public IEnumerator AlignmentSettingsRebindAndFollowLocaleChanges()
		{
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null, "This test must not change an active map.");
			var fragment = Load(sharedPath + "AlignmentSettings.uxml");
			Assert.That(fragment.Q<NavView>(), Is.Null);
			Assert.That(fragment.Q<Slider>("tag-size-slider"), Is.Not.Null);
			var section = fragment.Q("alignment-settings-section");
			var field = section.Q<DropdownField>("colocation-method-field");
			int staleChanges = 0;
			int changing = 0;
			int changes = 0;
			using var previous = new AlignmentSettingsBinder(section);
			previous.Changed += () => staleChanges++;
			previous.Refresh();
			previous.Dispose();
			previous.Dispose();
			using var current = new AlignmentSettingsBinder(section);
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
			var first = Load(sharedPath + "AlignmentSettings.uxml").Q("alignment-settings-section");
			var second = Load(sharedPath + "AlignmentSettings.uxml").Q("alignment-settings-section");
			using var firstBinder = new AlignmentSettingsBinder(first);
			using var secondBinder = new AlignmentSettingsBinder(second);
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

		[TestCase(ColocationManager.ColocationMethod.AprilTag)]
		[TestCase(ColocationManager.ColocationMethod.TwoAprilTags)]
		public void BothTagMethodsExposeThePaletteSliderAndMeasurementPage(ColocationManager.ColocationMethod method)
		{
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null);
			Assert.That(ColocationManager.Instance, Is.Null);
			var owner = new GameObject("Tag palette test");
			owner.SetActive(false);
			bool wasEditing = MapEditor.MapEditor.IsActive;
			var previousMode = MapEditorTool.CurrentMode;
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			try
			{
				var coordinator = owner.AddComponent<LaserTagMapCoordinator>();
				var maps = new MapManager(new MapStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"))));
				var space = MapSpace.Create("Test"); space.tagSizeCm = 18f; space.preferredColocationMethod = method;
				var spaces = new MapSpaceManager(new MapSpaceStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N")))); spaces.Load(space);
				maps.Create(space.storageFrameId);
				typeof(LaserTagMapCoordinator).GetField("maps", flags).SetValue(coordinator, maps);
				typeof(LaserTagMapCoordinator).GetField("spaces", flags).SetValue(coordinator, spaces);
				typeof(LaserTagMapCoordinator).GetProperty("Instance").SetValue(null, coordinator);
				typeof(MapEditor.MapEditor).GetProperty("IsActive").SetValue(null, true);
				var headset = Load("Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMenu.uxml");
				var page = headset.Q<NavPage>("space-details");
				using var details = new SpaceDetailsBinder(page);
				headset.Q<NavView>().GoToPage(page);
				Assert.That(details.Alignment.UsesTags, Is.True);
				Assert.That(headset.Q("tag-configuration-section").ClassListContains("map-field-hidden"), Is.True);
				Assert.That(headset.Q("choose-two-tag-pair").ClassListContains("map-field-hidden"),
					Is.EqualTo(method != ColocationManager.ColocationMethod.TwoAprilTags));
				var palette = Load("Assets/Anaglyph/LaserTag/MapEditor/MapEditorPalette.uxml");
				Assert.That(palette.Q<NavPage>("tags-page").Q<Slider>("tag-size-slider"), Is.Not.Null);
				Assert.That(palette.Q<NavPage>("tags-page").Q<Button>("measure-tag-size-button"), Is.Not.Null);
				Assert.That(palette.Q<NavPage>("measure-tag-size-page"), Is.Not.Null);
				Assert.That(coordinator.EffectiveTagSizeCm, Is.EqualTo(18f));
			}
			finally
			{
				typeof(LaserTagMapCoordinator).GetProperty("Instance").SetValue(null, null);
				typeof(MapEditor.MapEditor).GetProperty("IsActive").SetValue(null, wasEditing);
				MapEditorTool.SetMode(previousMode);
				Object.DestroyImmediate(owner);
			}
		}

		private static void AssertChoices(DropdownField field)
		{
			Assert.That(field.choices, Is.EqualTo(new[]
			{
				MenuCopy.Get("Game", "alignment.anchors"),
				MenuCopy.Get("Game", "alignment.tags"),
				MenuCopy.Get("Game", "alignment.system"),
				MenuCopy.Get("Game", "alignment.two-tags")
			}));
		}
	}

	public sealed class MapMenuCompositionTestWindow : EditorWindow { }
}
