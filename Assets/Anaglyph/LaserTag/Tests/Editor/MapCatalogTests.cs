using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Menu;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Anaglyph.LaserTag.Tests
{
	public class MapCatalogTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private string directory;
		private MapStore previousMaps;
		private MapSpaceStore previousSpaces;
		private GameObject owner;
		private LaserTagMapCoordinator coordinator;
		private MapSpaceWorkingCopy spaceDocument;
		private MapWorkingCopy mapDocument;
		private object documents, visit;
		private MapSpace first, second;
		private GameMap loaded, other, target;
		private MapMenuCompositionTestWindow window;
		private MapPickerBinder picker;
		private SpaceDetailsBinder details;
		private VisualElement root;
		private NavView nav;
		private int editCount;

		private static object Create(string name, params object[] args) =>
			Activator.CreateInstance(typeof(MapSpace).Assembly.GetType("Anaglyph.LaserTag.Maps." + name, true), args);
		private void Set(string name, object value) => typeof(LaserTagMapCoordinator).GetField(name, Private).SetValue(coordinator, value);

		[SetUp]
		public void SetUp()
		{
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null);
			Assert.That(SyncBus.Active, Is.False);
			Assert.That(MapObject.All.Count, Is.Zero, "The test must not replace objects in an open scene.");
			directory = Path.Combine(Path.GetTempPath(), "lasertag-catalog-tests-" + Guid.NewGuid().ToString("N"));
			previousMaps = MapStore.Default; previousSpaces = MapSpaceStore.Default;
			typeof(MapStore).GetProperty("Default").SetValue(null, new MapStore(Path.Combine(directory, "maps")));
			typeof(MapSpaceStore).GetProperty("Default").SetValue(null, new MapSpaceStore(Path.Combine(directory, "spaces")));
			first = MapSpace.Create("Arena A"); second = MapSpace.Create("Arena B");
			first.preferredColocationMethod = second.preferredColocationMethod = ColocationManager.ColocationMethod.SystemDetermined;
			first.lastUsed = 20; second.lastUsed = 10;
			loaded = Layout(first, "Loaded layout", 10); other = Layout(second, "Most recent layout", 100); target = Layout(second, "Chosen layout", 1);
			MapSpaceStore.Default.Save(first); MapSpaceStore.Default.Save(second);
			owner = new GameObject("Catalog integration test"); owner.SetActive(false);
			coordinator = owner.AddComponent<LaserTagMapCoordinator>();
			var manager = owner.AddComponent<ColocationManager>(); manager.ManagedSelection = true;
			var anchors = owner.AddComponent<SpatialAnchorColocationConstraintProvider>();
			var tags = owner.AddComponent<AprilTagColocationConstraintProvider>();
			typeof(ColocationManager).GetField("spatialAnchorColocationProvider", Private).SetValue(manager, anchors);
			typeof(ColocationManager).GetField("aprilTagColocationProvider", Private).SetValue(manager, tags);
			typeof(ColocationManager).GetField("systemDeterminedProvider", Private).SetValue(manager, new SystemDeterminedColocationConstraintProvider(null));
			visit = typeof(LaserTagMapCoordinator).GetField("visit", Private).GetValue(coordinator);
			documents = Create("MapCatalog", MapStore.Default, MapSpaceStore.Default, directory, visit);
			mapDocument = (MapWorkingCopy)documents.GetType().GetProperty("MapDocument").GetValue(documents); mapDocument.Load(loaded);
			spaceDocument = (MapSpaceWorkingCopy)documents.GetType().GetProperty("SpaceDocument").GetValue(documents); spaceDocument.Load(first);
			Set("documents", documents); Set("colocationManager", manager);
			Set("objects", Create("MapSceneObjectDirector", null, (Action)(() => { }), (Func<bool>)(() => false), (Func<MapSpaceFrame>)(() => spaceDocument.Frame)));
			Set("references", Create("MapSpaceColocationAdapter", manager));
			Set("alignment", Create("MapSpaceAlignmentController", manager, (Func<bool>)(() => false)));
			Set("discovery", Create("MapSpaceDiscovery", MapSpaceStore.Default, anchors, 1f, (Func<bool>)(() => false)));
			Set("session", Create("MapSessionSync"));
			typeof(LaserTagMapCoordinator).GetMethod("ComposeWorkflows", Private).Invoke(coordinator, null);
			typeof(LaserTagMapCoordinator).GetProperty("Instance").SetValue(null, coordinator);
			window = ScriptableObject.CreateInstance<MapMenuCompositionTestWindow>();
			window.Show(); window.position = new Rect(100, 100, 480, 700);
			window.rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Anaglyph/LaserTag/Interface/LaserTagRuntimeTheme.tss"));
			root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMenu.uxml").CloneTree();
			root.style.flexGrow = 1; window.rootVisualElement.Add(root);
			nav = root.Q<NavView>("maps-nav");
			picker = new MapPickerBinder(false);
			picker.Bind(root.Q("map-catalog-section"), () => editCount++, () => nav.GoToPage("space-details"));
			details = new SpaceDetailsBinder(nav.GetPage("space-details"));
		}
		private static GameMap Layout(MapSpace space, string name, long lastUsed)
		{
			var map = new GameMap { id = Guid.NewGuid().ToString("N"), version = Guid.NewGuid().ToString("N"), name = name, storageFrameId = space.storageFrameId, lastUsed = lastUsed };
			Assert.That(MapStore.Default.Save(map), Is.True); space.mapIds.Add(map.id); return map;
		}
		[TearDown]
		public void TearDown()
		{
			picker?.Dispose(); details?.Dispose(); window?.Close();
			typeof(LaserTagMapCoordinator).GetProperty("Instance").SetValue(null, null);
			if (owner) Object.DestroyImmediate(owner);
			if (previousMaps != null) typeof(MapStore).GetProperty("Default").SetValue(null, previousMaps);
			if (previousSpaces != null) typeof(MapSpaceStore).GetProperty("Default").SetValue(null, previousSpaces);
			if (directory != null && Directory.Exists(directory)) Directory.Delete(directory, true);
		}
		private static void Click(Button button)
		{
			using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = button.worldBound.center })) button.SendEvent(down);
			using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = button.worldBound.center })) button.SendEvent(up);
		}
		private VisualElement Row(GameMap map) => root.Q("map-" + map.id);

		private string BeginPendingNewMap()
		{
			Directory.CreateDirectory(Path.Combine(directory, "pending-catalog-operation.json.tmp"));
			LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("IOException|UnauthorizedAccessException"));
			Assert.That(coordinator.NewMap().Status, Is.EqualTo(CatalogOperationStatus.Pending));
			Assert.That(coordinator.HasPendingCatalogOperation, Is.True);
			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(loaded.id));
			var journal = (MapCatalogJournal)documents.GetType().GetProperty("Journal").GetValue(documents);
			return journal.PendingSpace.mapIds.Single(id => id != loaded.id);
		}

		private void RecoverPendingCatalogOperation()
		{
			Directory.Delete(Path.Combine(directory, "pending-catalog-operation.json.tmp"));
			Assert.That((bool)documents.GetType().GetMethod("RetryPending").Invoke(documents, null), Is.True);
			Assert.That(coordinator.HasPendingCatalogOperation, Is.False);
		}

		[Test]
		public void PendingNewMapCannotActivateAfterTheDeviceStartsFollowingASession()
		{
			string createdId = BeginPendingNewMap();
			var lifecycle = (MapLifecycle)typeof(LaserTagMapCoordinator).GetField("lifecycle", Private).GetValue(coordinator);
			lifecycle.EnterSession(authority: false);

			RecoverPendingCatalogOperation();

			Assert.That(coordinator.Phase, Is.EqualTo(MapPhase.AwaitingSessionMap));
			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(loaded.id));
			Assert.That(coordinator.CurrentSpace.id, Is.EqualTo(first.id));
			Assert.That(MapStore.Default.TryGet(createdId, out _), Is.True);
			Assert.That(coordinator.CurrentSpace.mapIds, Does.Contain(createdId));
		}

		[Test]
		public void PendingNewMapPreservesCompletedMembershipAfterTheReferenceContextChanges()
		{
			string createdId = BeginPendingNewMap();
			visit.GetType().GetProperty("ReferenceContext").SetValue(visit, Guid.NewGuid());
			spaceDocument.Rename("Current room revision");

			RecoverPendingCatalogOperation();

			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(loaded.id));
			Assert.That(coordinator.CurrentSpace.mapIds, Does.Contain(createdId));
			Assert.That(coordinator.CurrentSpace.name, Is.EqualTo("Current room revision"));
			Assert.That(coordinator.SaveCurrentMap(), Is.True);
			var reopened = new MapSpaceStore(Path.Combine(directory, "spaces"));
			Assert.That(reopened.TryGet(first.id, out var saved), Is.True);
			Assert.That(saved.mapIds, Is.EquivalentTo(new[] { loaded.id, createdId }));
			Assert.That(saved.name, Is.EqualTo("Current room revision"));
			Assert.That(new MapStore(Path.Combine(directory, "maps")).TryGet(createdId, out _), Is.True);
		}

		[Test]
		public void DeletingTheSelectedMapDuringRestoreCannotRecreateItOnSave()
		{
			var lifecycle = (MapLifecycle)typeof(LaserTagMapCoordinator).GetField("lifecycle", Private).GetValue(coordinator);
			lifecycle.BeginRestore();

			Assert.That(coordinator.DeleteMap(loaded.id).Status, Is.EqualTo(CatalogOperationStatus.Completed));

			Assert.That(coordinator.CurrentMap, Is.Null);
			Assert.That(coordinator.CurrentSpace.mapIds, Does.Not.Contain(loaded.id));
			Assert.That(coordinator.SaveCurrentMap(), Is.True);
			Assert.That(new MapStore(Path.Combine(directory, "maps")).TryGet(loaded.id, out _), Is.False);
			Assert.That(new MapSpaceStore(Path.Combine(directory, "spaces")).TryGet(first.id, out var saved), Is.True);
			Assert.That(saved.mapIds, Does.Not.Contain(loaded.id));
		}

		[Test]
		public void PendingMapDeletionClearsTheRestoreSnapshotWithoutUnloadingTheSessionMap()
		{
			Directory.CreateDirectory(Path.Combine(directory, "pending-catalog-operation.json.tmp"));
			LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("IOException|UnauthorizedAccessException"));
			Assert.That(coordinator.DeleteMap(loaded.id).Status, Is.EqualTo(CatalogOperationStatus.Pending));
			var sessionWorkflow = typeof(LaserTagMapCoordinator).GetField("sessionWorkflow", Private).GetValue(coordinator);
			var beforeSessionMap = sessionWorkflow.GetType().GetField("beforeSessionMap", Private);
			var beforeSessionSpace = sessionWorkflow.GetType().GetField("beforeSessionSpace", Private);
			beforeSessionMap.SetValue(sessionWorkflow, coordinator.CurrentMap);
			beforeSessionSpace.SetValue(sessionWorkflow, coordinator.CurrentSpace);
			var lifecycle = (MapLifecycle)typeof(LaserTagMapCoordinator).GetField("lifecycle", Private).GetValue(coordinator);
			lifecycle.FollowSession();
			owner.AddComponent<Unity.Netcode.NetworkObject>();
			var bus = owner.AddComponent<SyncBus>();
			typeof(Unity.Netcode.NetworkBehaviour).GetProperty("IsSpawned").SetValue(bus, true);
			typeof(Unity.Netcode.NetworkBehaviour).GetProperty("HasAuthority").SetValue(bus, false);
			typeof(SyncBus).GetProperty("Current").SetValue(null, bus);
			try
			{
				RecoverPendingCatalogOperation();

				Assert.That(coordinator.CurrentMap.id, Is.EqualTo(loaded.id));
				Assert.That(coordinator.CurrentSpace.id, Is.EqualTo(first.id));
				Assert.That(coordinator.CurrentSpace.mapIds, Does.Contain(loaded.id));
				Assert.That(beforeSessionMap.GetValue(sessionWorkflow), Is.Null);
				Assert.That(((MapSpace)beforeSessionSpace.GetValue(sessionWorkflow)).mapIds, Does.Not.Contain(loaded.id));
				Assert.That(new MapStore(Path.Combine(directory, "maps")).TryGet(loaded.id, out _), Is.False);
			}
			finally
			{
				typeof(Unity.Netcode.NetworkBehaviour).GetProperty("IsSpawned").SetValue(bus, false);
				typeof(SyncBus).GetProperty("Current").SetValue(null, null);
			}
		}

		private IEnumerator OpenSpaceDeletion()
		{
			for (int i = 0; i < 4; i++) yield return null;
			Assert.That(root.Q<Button>("delete-space-button"), Is.Null);
			Click(root.Q("space-" + second.id).Q<Button>("select-space-button"));
			for (int i = 0; i < 2; i++) yield return null;
			var delete = root.Q("space-" + second.id).Q<Button>("delete-space-button");
			Assert.That(delete.resolvedStyle.width, Is.EqualTo(48).Within(.1));
			Assert.That(delete.resolvedStyle.height, Is.EqualTo(48).Within(.1));
			Assert.That(delete.resolvedStyle.backgroundImage.vectorImage, Is.Not.Null);
			Assert.That(root.Q("space-" + first.id).Q<Button>("delete-space-button"), Is.Null);
			Click(delete);
			for (int i = 0; i < 2; i++) yield return null;
			Assert.That(nav.CurrentPage.name, Is.EqualTo("delete-space-modal"));
			Assert.That(root.Q<Label>("delete-space-subject").text, Does.Contain(second.name));
			Assert.That(coordinator.CurrentSpace.id, Is.EqualTo(first.id));
			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(loaded.id));
		}

		[UnityTest]
		public IEnumerator SpaceDeletionRequiresConfirmationAndPreservesTheActiveSpace()
		{
			yield return OpenSpaceDeletion();
			Assert.That(MapSpaceStore.Default.TryGet(second.id, out _), Is.True);
			Assert.That(MapStore.Default.TryGet(target.id, out _), Is.True);
			Click(root.Q<Button>("confirm-delete-space-button"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("map-manager-page"));
			Assert.That(MapSpaceStore.Default.TryGet(second.id, out _), Is.False);
			Assert.That(MapStore.Default.TryGet(target.id, out _), Is.False);
			Assert.That(MapStore.Default.TryGet(other.id, out _), Is.False);
			Assert.That(new MapSpaceStore(Path.Combine(directory, "spaces")).TryGet(second.id, out _), Is.False);
			Assert.That(new MapStore(Path.Combine(directory, "maps")).TryGet(target.id, out _), Is.False);
			Assert.That(root.Q("space-" + second.id), Is.Null);
			Assert.That(coordinator.CurrentSpace.id, Is.EqualTo(first.id));
			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(loaded.id));
			Assert.That(MapStore.Default.TryGet(loaded.id, out _), Is.True);
		}

		[UnityTest]
		public IEnumerator CancelAndBackDismissSpaceDeletionWithoutDeletingAnything()
		{
			yield return OpenSpaceDeletion();
			Click(root.Q<Button>("cancel-delete-space-button"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("map-manager-page"));
			Assert.That(MapSpaceStore.Default.TryGet(second.id, out _), Is.True);
			for (int i = 0; i < 2; i++) yield return null;
			Click(root.Q<Button>("delete-space-button"));
			nav.GoBack();
			Assert.That(nav.CurrentPage.name, Is.EqualTo("map-manager-page"));
			Assert.That(MapSpaceStore.Default.TryGet(second.id, out _), Is.True);
			Assert.That(MapStore.Default.TryGet(target.id, out _), Is.True);
			Assert.That(MapStore.Default.TryGet(other.id, out _), Is.True);
			Assert.That(coordinator.CurrentSpace.id, Is.EqualTo(first.id));
			Assert.That(typeof(MapPickerBinder).GetField("pendingDeleteSpaceId", Private).GetValue(picker), Is.Null);
		}

		[UnityTest]
		public IEnumerator SpaceDeletionRechecksEligibilityWhenConfirming()
		{
			yield return OpenSpaceDeletion();
			var lifecycle = (MapLifecycle)typeof(LaserTagMapCoordinator).GetField("lifecycle", Private).GetValue(coordinator);
			lifecycle.Stop();
			Click(root.Q<Button>("confirm-delete-space-button"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("delete-space-modal"));
			Assert.That(root.Q<Button>("confirm-delete-space-button").enabledSelf, Is.False);
			Assert.That(root.Q<Label>("delete-space-blocker").text, Is.Not.Empty);
			Assert.That(MapSpaceStore.Default.TryGet(second.id, out _), Is.True);
			Assert.That(MapStore.Default.TryGet(target.id, out _), Is.True);
		}

		[UnityTest]
		public IEnumerator SpaceNameSurvivesMenuDisposalWithoutLosingInputFocus()
		{
			nav.GoToPage("space-details");
			yield return null;
			var field = root.Q<TextField>("space-name-field");
			field.Focus();
			yield return null;
			Assert.That(field.panel.focusController.focusedElement is VisualElement focused &&
				(focused == field || field.Contains(focused)), Is.True);
			field.value = "Renamed arena";
			details.Refresh();
			Assert.That(field.value, Is.EqualTo("Renamed arena"));
			details.Dispose(); details = null;
			var reopened = new MapSpaceStore(Path.Combine(directory, "spaces"));
			Assert.That(reopened.TryGet(first.id, out var saved), Is.True);
			Assert.That(saved.name, Is.EqualTo("Renamed arena"));
		}

		[Test]
		public void CommittedSpaceNameSurvivesReopeningTheStore()
		{
			Assert.That(coordinator.RenameSpace("Renamed arena"), Is.True);
			var reopened = new MapSpaceStore(Path.Combine(directory, "spaces"));
			Assert.That(reopened.TryGet(first.id, out var saved), Is.True);
			Assert.That(saved.name, Is.EqualTo("Renamed arena"));
		}

		[UnityTest]
		public IEnumerator SpaceAlignmentOwnsHeadsetTagToolsAndAutomaticRegistration()
		{
			Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			var menu = owner.AddComponent<GameMenu>();
			using var name = new MapNameBinder(root.Q("map-name-section"));
			void Field(string key, object value) => typeof(GameMenu).GetField(key, Private).SetValue(menu, value);
			T Handler<T>(string method) where T : Delegate => (T)Delegate.CreateDelegate(typeof(T), menu, typeof(GameMenu).GetMethod(method, Private));
			Field("navView", root.Q<NavView>("game-nav")); Field("mapsNavigation", nav);
			Field("matchNavigation", root.Q<NavView>("match-nav"));
			Field("demoNavigation", root.Q<NavView>("demo-nav"));
			Field("demoTab", root.Q<Button>("demo-tab"));
			Field("mapsTab", root.Q<Button>("maps-tab")); Field("matchTab", root.Q<Button>("match-tab"));
			Field("editingMapPage", nav.GetPage("editing-map-page"));
			Field("spaceDetailsPage", nav.GetPage("space-details")); Field("spaceDetails", details); Field("mapName", name);
			var navigate = Handler<Action<NavPage>>("OnNavPageChanged");
			var active = Handler<Action<bool>>("OnMapEditorStateChanged");
			var register = Handler<Action>("ShowSpaceAlignment");
			var changing = Handler<Action>("OnAlignmentChanging");
			var changed = Handler<Action>("OnAlignmentChanged");
			var selectTab = Handler<Action<GameMenu.Tab>>("SelectTab");
			nav.Changed += navigate;
			MapEditor.MapEditor.ActiveChanged += active;
			MapEditor.MapEditor.TagRegistrationRequested += register;
			details.Alignment.Changing += changing;
			details.Alignment.Changed += changed;
			using var errors = new MenuErrorPresenter(MenuErrorArea.Game);
			errors.Bind(root.Q<NavView>("game-nav"));
			try
			{
				foreach (var method in new[] { ColocationManager.ColocationMethod.AprilTag, ColocationManager.ColocationMethod.TwoAprilTags })
				{
					first.preferredColocationMethod = method; spaceDocument.Load(first);
					nav.GoToPage("space-details");
					Assert.That(nav.CurrentPage.name, Is.EqualTo("space-details"));
					Assert.That(MapEditor.MapEditor.IsActive, Is.True);
					Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.Tags));
					MapEditorTool.SetMode(MapEditorTool.Mode.MeasureTagSize);
					errors.Show(new MenuError(MenuErrorArea.Game, "Alignment warning", "Test detail"));
					Assert.That(MapEditor.MapEditor.IsActive, Is.True);
					for (int i = 0; i < 2; i++) yield return null;
					Click(root.Q<Button>("dismiss-error-button"));
					Assert.That(nav.CurrentPage.name, Is.EqualTo("space-details"));
					Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.MeasureTagSize));
					selectTab(GameMenu.Tab.Match);
					Assert.That(MapEditor.MapEditor.IsActive, Is.False);
					Assert.That(nav.CurrentPage.name, Is.EqualTo("space-details"));
					selectTab(GameMenu.Tab.Maps);
					Assert.That(MapEditor.MapEditor.IsActive, Is.True);
					Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.Tags));
					nav.GoBack();
					Assert.That(MapEditor.MapEditor.IsActive, Is.False);
					Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.Move));
					MapEditor.MapEditor.RequestTagRegistration();
					Assert.That(nav.CurrentPage.name, Is.EqualTo("space-details"));
					Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.Tags));
					MapEditor.MapEditor.SetActive(false);
					Assert.That(nav.CurrentPage.name, Is.EqualTo("map-manager-page"));
				}
				MapEditor.MapEditor.SetActive(true);
				Assert.That(nav.CurrentPage.name, Is.EqualTo("editing-map-page"));
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.Move));
			}
			finally
			{
				nav.Changed -= navigate;
				MapEditor.MapEditor.ActiveChanged -= active;
				MapEditor.MapEditor.TagRegistrationRequested -= register;
				details.Alignment.Changing -= changing;
				details.Alignment.Changed -= changed;
				MapEditor.MapEditor.SetActive(false);
				Object.DestroyImmediate(menu);
			}
		}

		[UnityTest]
		public IEnumerator UnalignedCurrentDraftCanOpenSpaceSettingsAndSaveSystemAlignment()
		{
			first.automaticallyCreated = first.initializationPending = true;
			first.preferredColocationMethod = ColocationManager.ColocationMethod.MetaSharedAnchor;
			spaceDocument.Load(first);
			MapSpaceStore.Default.Save(first);
			Assert.That(coordinator.GetSpacePresence(first.id), Is.EqualTo(MapPresence.Unknown));
			Assert.That(ColocationManager.IsColocated, Is.False);
			for (int i = 0; i < 4; i++) yield return null;
			var edit = root.Q("space-" + first.id).Q<Button>("edit-space-button");
			Assert.That(edit.enabledInHierarchy, Is.True);
			Assert.That(Row(loaded), Is.Not.Null);
			Click(edit);
			Assert.That(nav.CurrentPage.name, Is.EqualTo("space-details"));
			var method = nav.CurrentPage.Q<DropdownField>("colocation-method-field");
			Assert.That(method.enabledInHierarchy, Is.True);
			method.value = method.choices[(int)ColocationManager.ColocationMethod.SystemDetermined];
			Assert.That(coordinator.CurrentSpace.preferredColocationMethod, Is.EqualTo(ColocationManager.ColocationMethod.SystemDetermined));
			Assert.That(new MapSpaceStore(Path.Combine(directory, "spaces")).TryGet(first.id, out var saved), Is.True);
			Assert.That(saved.preferredColocationMethod, Is.EqualTo(ColocationManager.ColocationMethod.SystemDetermined));
			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(loaded.id));
			nav.GoBack();
			Assert.That(root.Q("space-" + first.id).Q<Button>("edit-space-button"), Is.Not.Null);
		}

		[UnityTest]
		public IEnumerator GroupedRowsKeepEditingOnLoadedItemsAndDeletionOnTheSelection()
		{
			using var probe = new MapProbeBinder(root.Q<Button>("probe-maps-button"));
			for (int i = 0; i < 4; i++) yield return null;
			Assert.That(root.Query(className: "catalog-space").ToList().Select(r => (string)r.userData), Is.EqualTo(new[] { first.id, second.id }));
			Assert.That(root.Q("space-" + second.id).Query(className: "catalog-map-row").ToList().Count, Is.EqualTo(2));
			Assert.That(Row(loaded).Q<Button>("edit-map-button"), Is.Not.Null);
			Assert.That(Row(target).Q<Button>("edit-map-button"), Is.Null);
			Assert.That(root.Q("delete-map-button"), Is.Null);
			Assert.That(root.Q("load-map-button"), Is.Null);
			var newMap = root.Q<Button>("new-map-button");
			var check = root.Q<Button>("probe-maps-button");
			Assert.That(check.parent, Is.SameAs(newMap.parent));
			Assert.That(check.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
			Assert.That(check.worldBound.xMin, Is.GreaterThanOrEqualTo(newMap.worldBound.xMax));
			Assert.That(check.worldBound.yMin, Is.EqualTo(newMap.worldBound.yMin).Within(.1));
			Click(Row(target).Q<Button>("select-map-button"));
			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(loaded.id));
			Assert.That(Row(target).Q<Button>("delete-map-button"), Is.Not.Null);
			Assert.That(Row(loaded).Q<Button>("delete-map-button"), Is.Null);
			for (int i = 0; i < 2; i++) yield return null;
			var load = Row(target).Q<Button>("load-map-button");
			Assert.That(load.resolvedStyle.width, Is.EqualTo(48).Within(.1));
			Assert.That(load.resolvedStyle.width, Is.EqualTo(load.resolvedStyle.height).Within(.1));
			Assert.That(load.resolvedStyle.backgroundImage.vectorImage, Is.Not.Null);
			Assert.That(Row(loaded).Q<Button>("load-map-button"), Is.Null);
			var edit = Row(loaded).Q<Button>("edit-map-button");
			Assert.That(edit.resolvedStyle.width, Is.EqualTo(edit.resolvedStyle.height).Within(.1));
			Assert.That(edit.resolvedStyle.backgroundImage.vectorImage, Is.Not.Null);
			Click(edit); Assert.That(editCount, Is.EqualTo(1));
			Click(root.Q("space-" + first.id).Q<Button>("select-space-button"));
			for (int i = 0; i < 2; i++) yield return null;
			Click(root.Q<Button>("edit-space-button"));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("space-details"));
			nav.GoBack(); Assert.That(nav.CurrentPage.name, Is.EqualTo("map-manager-page"));
			Assert.That(Row(target).Q<Button>("delete-map-button"), Is.Null);
		}

		[UnityTest]
		public IEnumerator SelectingASpaceOnlyHighlightsItAndEditingLoadsItsMostRecentlyUsedMap()
		{
			for (int i = 0; i < 4; i++) yield return null;
			Click(Row(target).Q<Button>("select-map-button"));
			for (int i = 0; i < 2; i++) yield return null;
			Click(root.Q("space-" + second.id).Q<Button>("select-space-button"));
			for (int i = 0; i < 2; i++) yield return null;
			Assert.That(coordinator.CurrentSpace.id, Is.EqualTo(first.id));
			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(loaded.id));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("map-manager-page"));
			Assert.That(Row(target).Q<Button>("select-map-button").ClassListContains("selected"), Is.False);
			Assert.That(root.Q<Button>("delete-map-button"), Is.Null);
			Assert.That(root.Q<Button>("load-map-button"), Is.Null);
			Assert.That(root.Q("space-" + first.id).Q<Button>("edit-space-button"), Is.Null);
			var selected = root.Q("space-" + second.id);
			Assert.That(selected.Q<Button>("select-space-button").ClassListContains("selected"), Is.True);
			var edit = selected.Q<Button>("edit-space-button");
			Assert.That(edit.resolvedStyle.width, Is.EqualTo(48).Within(.1));
			Assert.That(edit.resolvedStyle.height, Is.EqualTo(48).Within(.1));
			Assert.That(edit.resolvedStyle.backgroundImage.vectorImage, Is.Not.Null);
			Click(edit);
			Assert.That(coordinator.CurrentSpace.id, Is.EqualTo(second.id));
			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(other.id));
			Assert.That(nav.CurrentPage.name, Is.EqualTo("space-details"));
			Assert.That(root.Q<TextField>("space-name-field").value, Is.EqualTo(second.name));
		}

		[UnityTest]
		public IEnumerator SelectingAMapClearsSpaceSelectionAndSpaceSelectionSurvivesRebinding()
		{
			for (int i = 0; i < 4; i++) yield return null;
			Click(root.Q("space-" + second.id).Q<Button>("select-space-button"));
			picker.Bind(root.Q("map-catalog-section"), () => editCount++, () => nav.GoToPage("space-details"));
			for (int i = 0; i < 2; i++) yield return null;
			Assert.That(root.Q("space-" + second.id).Q<Button>("select-space-button").ClassListContains("selected"), Is.True);
			Assert.That(root.Q<Button>("edit-space-button"), Is.Not.Null);
			Click(Row(target).Q<Button>("select-map-button"));
			Assert.That(root.Q("space-" + second.id).Q<Button>("select-space-button").ClassListContains("selected"), Is.False);
			Assert.That(root.Q<Button>("edit-space-button"), Is.Null);
			Assert.That(Row(target).Q<Button>("select-map-button").ClassListContains("selected"), Is.True);
		}

		[UnityTest]
		public IEnumerator LoadingAnotherSpacesMapLoadsTheExactSelectionInsteadOfItsMostRecentMap()
		{
			for (int i = 0; i < 4; i++) yield return null;
			Click(Row(target).Q<Button>("select-map-button"));
			for (int i = 0; i < 2; i++) yield return null;
			Assert.That(Row(target).Q<Button>("load-map-button").enabledSelf, Is.True);
			Click(Row(target).Q<Button>("load-map-button"));
			Assert.That(Row(target).Q<Button>("load-map-button").enabledSelf, Is.False);
			Assert.That(coordinator.CurrentSpace.id, Is.EqualTo(second.id));
			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(target.id));
			Assert.That(coordinator.CurrentMap.id, Is.Not.EqualTo(other.id));
			Assert.That(root.Q("space-" + second.id).Q<Button>("edit-space-button"), Is.Null);
			Assert.That(Row(target).Q<Button>("edit-map-button"), Is.Not.Null);
			Assert.That(Row(loaded).Q<Button>("edit-map-button"), Is.Null);
		}

		[UnityTest]
		public IEnumerator SelectionChangesDisarmDeletionAndSpaceDetailsCannotEditAnOldSpace()
		{
			for (int i = 0; i < 4; i++) yield return null;
			Click(Row(target).Q<Button>("select-map-button"));
			for (int i = 0; i < 2; i++) yield return null;
			Click(Row(target).Q<Button>("delete-map-button"));
			Assert.That(Row(target).Q<Button>("delete-map-button").ClassListContains("catalog-confirm-delete"), Is.True);
			for (int i = 0; i < 2; i++) yield return null;
			Click(Row(other).Q<Button>("select-map-button"));
			Assert.That(Row(other).Q<Button>("delete-map-button").ClassListContains("catalog-confirm-delete"), Is.False);
			for (int i = 0; i < 2; i++) yield return null;
			Click(Row(other).Q<Button>("delete-map-button"));
			for (int i = 0; i < 2; i++) yield return null;
			Click(Row(other).Q<Button>("delete-map-button"));
			Assert.That(MapStore.Default.TryGet(other.id, out _), Is.False);
			Assert.That(MapSpaceStore.Default.TryGet(second.id, out var savedSpace), Is.True);
			Assert.That(savedSpace.mapIds, Does.Not.Contain(other.id));
			Assert.That(coordinator.CurrentMap.id, Is.EqualTo(loaded.id));
			nav.GoToPage("space-details");
			Assert.That(coordinator.ChangeMap(target.id), Is.True);
			Assert.That(nav.CurrentPage.name, Is.EqualTo("map-manager-page"));
			Assert.That(root.Q<TextField>("space-name-field").value, Is.EqualTo(second.name));
		}
	}
}
