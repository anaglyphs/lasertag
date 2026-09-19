using System;
using System.Collections.Generic;
using Anaglyph.Debugging;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.MapEditor
{
	/// <summary>
	/// Maps the editor tool's mode to the palette's pages, and builds the object
	/// categories and thumbnails from the map object database.
	/// </summary>
	[RequireComponent(typeof(UIDocument))]
	public class PaletteMenu : MonoBehaviour
	{
		[SerializeField] private MapObjectDatabase database;

		private VisualElement categoryRail;
		private Label categoryTitle;
		private Label editingContext;
		private ScrollView objectGrid;
		private NavView navView;
		private NavPage objectsPage;
		private NavPage tagsPage;
		private NavPage measureTagSizePage;
		private List<Slider> tagSizeSliders;
		private List<Label> tagSizeNotes;
		private Label tagStatus;
		private Label measurementHint;
		private Button measureTagSizeButton;
		private Button unregisterAllTagsButton;
		private Button doneButton;
		private Label setupMeasureInstructions;
		private Label setupRegisterInstructions;
		private Label setupFinish;
		private VisualElement registrationTagSize;
		private Button continueRegistrationButton;
		private Button backToMeasurementButton;
		private Button clearTagsForMeasurementButton;

		private readonly List<(MapObjectDatabase.Category category, Button button)> categoryButtons = new();
		private readonly List<(MapObject prefab, Button button)> objectButtons = new();

		private MapObjectDatabase.Category selectedCategory;
		private MapObject selectedPrefab;
		private float? pendingTagSizeCm;
		private float? submittedTagSizeCm;
		private float submittedTagSizeTime;
		private Guid pendingSizeContext;
		private float pendingTagSizeTime;
		private bool navigatingForMode;
		private const float tagSizeSettleSeconds = 0.1f;

		private void OnEnable()
		{
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"PaletteMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			navView = NavView.RequireIn(root);
			objectsPage = navView.GetPage("objects-page");
			tagsPage = navView.GetPage("tags-page");
			measureTagSizePage = navView.GetPage("measure-tag-size-page");
			categoryRail = Require<VisualElement>(root, "category-rail");
			categoryTitle = Require<Label>(root, "category-title");
			editingContext = Require<Label>(root, "editing-context");
			objectGrid = Require<ScrollView>(root, "object-grid");
			tagSizeSliders = root.Query<Slider>("tag-size-slider").ToList();
			tagSizeNotes = root.Query<Label>("tag-size-note").ToList();
			tagStatus = Require<Label>(root, "tag-status");
			measurementHint = Require<Label>(root, "tag-measurement-hint");
			measureTagSizeButton = Require<Button>(root, "measure-tag-size-button");
			unregisterAllTagsButton = Require<Button>(root, "unregister-all-tags-button");
			doneButton = Require<Button>(root, "done-button");
			setupMeasureInstructions = Require<Label>(root, "tag-setup-measure-instructions");
			setupRegisterInstructions = Require<Label>(root, "tag-setup-register-instructions");
			setupFinish = Require<Label>(root, "tag-setup-finish");
			registrationTagSize = Require<VisualElement>(root, "registration-tag-size");
			continueRegistrationButton = Require<Button>(root, "continue-tag-registration");
			backToMeasurementButton = Require<Button>(root, "tag-setup-back");
			clearTagsForMeasurementButton = Require<Button>(root, "clear-tags-for-measurement");

			MenuCopy.Changed += OnCopyChanged;
			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			MapEditorTool.ModeChanged += OnToolModeChanged;
			navView.Changed += OnNavigationChanged;
			foreach (var slider in tagSizeSliders) slider.RegisterValueChangedCallback(OnTagSizeChanged);
			MapEditorTool.TagSizeMeasured += OnTagSizeMeasured;
			continueRegistrationButton.clicked += OnContinueRegistration;
			backToMeasurementButton.clicked += OnMeasureTagSizeClicked;
			clearTagsForMeasurementButton.clicked += OnUnregisterAllTagsClicked;
			measureTagSizeButton.clicked += OnMeasureTagSizeClicked;
			unregisterAllTagsButton.clicked += OnUnregisterAllTagsClicked;
			doneButton.clicked += OnDoneClicked;
			RebuildRail();
			OnToolModeChanged(MapEditorTool.CurrentMode);
		}

		private void OnDisable()
		{
			FlushPendingTagSize();
			MenuCopy.Changed -= OnCopyChanged;
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;
			MapEditorTool.ModeChanged -= OnToolModeChanged;
			if (navView != null) navView.Changed -= OnNavigationChanged;
			if (tagSizeSliders != null)
				foreach (var slider in tagSizeSliders) slider.UnregisterValueChangedCallback(OnTagSizeChanged);
			MapEditorTool.TagSizeMeasured -= OnTagSizeMeasured;
			if (continueRegistrationButton != null) continueRegistrationButton.clicked -= OnContinueRegistration;
			if (backToMeasurementButton != null) backToMeasurementButton.clicked -= OnMeasureTagSizeClicked;
			if (clearTagsForMeasurementButton != null) clearTagsForMeasurementButton.clicked -= OnUnregisterAllTagsClicked;
			submittedTagSizeCm = null;
			if (measureTagSizeButton != null) measureTagSizeButton.clicked -= OnMeasureTagSizeClicked;
			if (unregisterAllTagsButton != null) unregisterAllTagsButton.clicked -= OnUnregisterAllTagsClicked;
			if (doneButton != null) doneButton.clicked -= OnDoneClicked;
			categoryButtons.Clear();
			objectButtons.Clear();
			navView = null;
		}

		private void OnDebugModeChanged(bool debugMode) => RebuildRail();
		private void OnCopyChanged()
		{
			RebuildRail();
			RefreshTagSettings();
			RefreshMeasurementHint();
			RefreshSetupGuide();
		}

		private static void OnDoneClicked()
		{
			var setup = LaserTagMapCoordinator.Instance?.TagSetup;
			if (setup?.IsGuidingHeadset != true) MapEditor.SetActive(false);
			else if (setup.CanFinish) setup.Finish();
			else setup.Cancel();
		}

		private void OnToolModeChanged(MapEditorTool.Mode mode)
		{
			if (mode != MapEditorTool.Mode.Tags && mode != MapEditorTool.Mode.MeasureTagSize)
				FlushPendingTagSize();

			// Move is the editor's initial state. Keep the catalog available there so selecting its
			// first object can enter Place mode; tag modes alone replace the catalog.
			bool selectingObjects = mode is MapEditorTool.Mode.Move or MapEditorTool.Mode.Place;
			categoryRail.SetEnabled(selectingObjects);
			objectGrid.SetEnabled(selectingObjects);
			selectedPrefab = MapEditorTool.SelectedObject;
			RefreshHighlights();
			ShowPageForMode(mode);
			RefreshTagSettings();
			RefreshMeasurementHint();
			RefreshSetupGuide();
		}

		private void ShowPageForMode(MapEditorTool.Mode mode)
		{
			if (navView == null)
				return;

			navView.style.display = DisplayStyle.Flex;
			navigatingForMode = true;
			try
			{
				// Rebuild the short, mode-shaped history. Palette navigation never changes the tool;
				// a mode transition is the only way to expose one of these pages.
				string targetPageName = PageNameForMode(mode);
				GoToPage(objectsPage);
				if (targetPageName != objectsPage.name)
					GoToPage(tagsPage);
				if (targetPageName == measureTagSizePage.name)
					GoToPage(measureTagSizePage);
			}
			finally
			{
				navigatingForMode = false;
			}
		}

		private static string PageNameForMode(MapEditorTool.Mode mode) => mode switch
		{
			MapEditorTool.Mode.Move or MapEditorTool.Mode.Place => "objects-page",
			MapEditorTool.Mode.Tags => "tags-page",
			MapEditorTool.Mode.MeasureTagSize => "measure-tag-size-page",
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
		};

		private void GoToPage(NavPage page)
		{
			if (navView.CurrentPage != page)
				navView.GoToPage(page);
		}

		private void OnNavigationChanged(NavPage page)
		{
			if (navigatingForMode)
				return;

			ShowPageForMode(MapEditorTool.CurrentMode);
		}

		private void Update()
		{
			var context = editingContext;
			if (context != null)
			{
				var coordinator = LaserTagMapCoordinator.Instance;
				bool objects = MapEditorTool.CurrentMode is MapEditorTool.Mode.Place or MapEditorTool.Mode.Move;
				context.text = MenuCopy.Format("Map", objects ? "palette.map-context" : "palette.space-context",
					objects ? coordinator?.CurrentMap?.name ?? "" : coordinator?.CurrentSpace?.name ?? "");
			}
			if (pendingTagSizeCm.HasValue && Time.unscaledTime - pendingTagSizeTime >= tagSizeSettleSeconds)
				FlushPendingTagSize();

			if (MapEditorTool.CurrentMode is MapEditorTool.Mode.Tags or MapEditorTool.Mode.MeasureTagSize)
				RefreshTagSettings();
			if (MapEditorTool.CurrentMode == MapEditorTool.Mode.MeasureTagSize)
				RefreshMeasurementHint();
			RefreshSetupGuide();
		}

		// ------- tag settings ----------------------------------------
		private void RefreshSetupGuide()
		{
			var setup = LaserTagMapCoordinator.Instance?.TagSetup;
			bool guided = setup?.IsGuidingHeadset == true;
			setupMeasureInstructions.style.display = guided ? DisplayStyle.Flex : DisplayStyle.None;
			setupRegisterInstructions.style.display = guided ? DisplayStyle.Flex : DisplayStyle.None;
			setupFinish.style.display = guided ? DisplayStyle.Flex : DisplayStyle.None;
			backToMeasurementButton.style.display = guided ? DisplayStyle.Flex : DisplayStyle.None;
			registrationTagSize.style.display = guided ? DisplayStyle.None : DisplayStyle.Flex;
			measureTagSizeButton.style.display = guided ? DisplayStyle.None : DisplayStyle.Flex;
			unregisterAllTagsButton.style.display = guided ? DisplayStyle.None : DisplayStyle.Flex;
			doneButton.style.display = guided && !setup.CanEndFromHere ? DisplayStyle.None : DisplayStyle.Flex;
			doneButton.text = MenuCopy.Get("Map", guided && !setup.CanFinish ? "tag-setup.cancel" : "GameMapEditingPage.finish-editing-button.text");
			measurementHint.style.display = guided && !setup.CanMeasure ? DisplayStyle.None : DisplayStyle.Flex;
			if (!guided) return;
			setupFinish.text = MenuCopy.Get("Map", setup.State.operatorManaged ? "tag-setup.headset-finish" : "tag-setup.headset-finish-local");
			setupMeasureInstructions.text = MenuCopy.Get("Map", setup.CanMeasure || setup.SizeConfirmed
				? "tag-setup.headset-measure" : "tag-setup.headset-wait");
			foreach (var slider in tagSizeSliders) slider.SetEnabled(setup.CanMeasure);
			continueRegistrationButton.SetEnabled(setup.CanMeasure || setup.SizeConfirmed);
		}

		private void OnTagSizeChanged(ChangeEvent<float> change)
		{
			pendingSizeContext = LaserTagMapCoordinator.Instance?.ReferenceContext ?? Guid.Empty;
			pendingTagSizeCm = change.newValue;
			pendingTagSizeTime = Time.unscaledTime;
			submittedTagSizeCm = null;
			LaserTagMapCoordinator.Instance?.TagSetup?.ReturnToMeasurement();
			foreach (var slider in tagSizeSliders) slider.SetValueWithoutNotify(change.newValue);
		}

		private void OnTagSizeMeasured(float centimeters)
		{
			pendingSizeContext = LaserTagMapCoordinator.Instance?.ReferenceContext ?? Guid.Empty;
			pendingTagSizeCm = null;
			submittedTagSizeCm = centimeters;
			submittedTagSizeTime = Time.unscaledTime;
			LaserTagMapCoordinator.Instance?.TagSetup?.ReturnToMeasurement();
			foreach (var slider in tagSizeSliders)
			{
				slider.highValue = Mathf.Max(50f, centimeters);
				slider.SetValueWithoutNotify(centimeters);
			}
		}

		private void FlushPendingTagSize()
		{
			if (!pendingTagSizeCm.HasValue)
				return;

			float centimeters = pendingTagSizeCm.Value;
			pendingTagSizeCm = null;
			var manager = LaserTagMapCoordinator.Instance;
			if (manager != null && manager.ReferenceContext == pendingSizeContext && manager.SetTagSize(centimeters))
			{
				submittedTagSizeCm = centimeters;
				submittedTagSizeTime = Time.unscaledTime;
			}
		}

		private void OnContinueRegistration()
		{
			var manager = LaserTagMapCoordinator.Instance;
			if (manager == null) return;
			float centimeters = pendingTagSizeCm ?? submittedTagSizeCm ?? manager.EffectiveTagSizeCm;
			FlushPendingTagSize();
			if (manager.TagSetup?.IsGuidingHeadset == true)
				manager.TagSetup.ContinueToRegistration(centimeters);
			else
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
		}

		private void OnMeasureTagSizeClicked()
		{
			var manager = LaserTagMapCoordinator.Instance;
			FlushPendingTagSize();
			if (manager?.TagSetup?.IsGuidingHeadset == true)
				manager.TagSetup.ReturnToMeasurement();
			else if (MapEditorTool.DominantHand != null && manager?.DescribeTagSizeBlocker() == null)
				MapEditorTool.SetMode(MapEditorTool.Mode.MeasureTagSize);
		}

		private void OnUnregisterAllTagsClicked()
		{
			LaserTagMapCoordinator.Instance?.UnregisterAllTags();
			RefreshTagSettings();
		}

		private void RefreshTagSettings()
		{
			if (tagSizeSliders == null)
				return;

			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (manager == null)
			{
				foreach (var slider in tagSizeSliders) slider.SetEnabled(false);
				unregisterAllTagsButton.SetEnabled(false);
				measureTagSizeButton.SetEnabled(false);
				continueRegistrationButton.SetEnabled(false);
				clearTagsForMeasurementButton.SetEnabled(false);
				clearTagsForMeasurementButton.style.display = DisplayStyle.None;
				foreach (var note in tagSizeNotes) SetMessage(note, null);
				SetMessage(tagStatus, null);
				return;
			}

			string sizeBlocker = manager.DescribeTagSizeBlocker();
			if (pendingSizeContext != manager.ReferenceContext)
			{
				pendingTagSizeCm = null;
				submittedTagSizeCm = null;
			}
			foreach (var slider in tagSizeSliders) slider.SetEnabled(sizeBlocker == null);
			measureTagSizeButton.SetEnabled(MapEditorTool.DominantHand != null && sizeBlocker == null);
			foreach (var note in tagSizeNotes) SetMessage(note, sizeBlocker);

			if (submittedTagSizeCm.HasValue && (Mathf.Abs(submittedTagSizeCm.Value - manager.EffectiveTagSizeCm) < .001f ||
				Time.unscaledTime - submittedTagSizeTime > 2f)) submittedTagSizeCm = null;
			if (!pendingTagSizeCm.HasValue && !submittedTagSizeCm.HasValue)
				foreach (var slider in tagSizeSliders)
					if (!IsBeingEdited(slider))
					{
						slider.highValue = Mathf.Max(50f, manager.EffectiveTagSizeCm);
						slider.SetValueWithoutNotify(manager.EffectiveTagSizeCm);
					}
			continueRegistrationButton.SetEnabled(true);

			int registered = manager.CurrentSpace != null ? manager.CurrentSpace.tags.Count : 0;
			string registrationBlocker = manager.DescribeTagRegistrationBlocker();
			bool canRemove = registered > 0 && manager.DescribeTagRemovalBlocker() == null;
			unregisterAllTagsButton.SetEnabled(canRemove);
			clearTagsForMeasurementButton.style.display = registered == 0 ? DisplayStyle.None : DisplayStyle.Flex;
			clearTagsForMeasurementButton.SetEnabled(canRemove);

			bool twoTags = (ColocationManager.Instance != null ? ColocationManager.Instance.SelectedMethod :
				manager.CurrentSpace?.preferredColocationMethod) == ColocationManager.ColocationMethod.TwoAprilTags;
			if (twoTags)
				tagStatus.text = MenuCopy.Get("Map", "alignment.two-tags-description");
			else if (registrationBlocker != null && registrationBlocker != sizeBlocker)
				tagStatus.text = MenuCopy.Format("Map", "alignment.tag-blocked", registered, registrationBlocker);
			else if (manager.SessionIsWaitingOnFirstTag)
				tagStatus.text = MenuCopy.Get("Map", "alignment.no-tags");
			else
				tagStatus.text = MenuCopy.Format("Map", "alignment.tag-count", registered);
		}

		private void RefreshMeasurementHint()
		{
			if (measurementHint == null)
				return;

			measurementHint.text = MenuCopy.Get("Map", MapEditorTool.MeasurementHint ?? "ruler.first-point");
		}

		private static bool IsBeingEdited(VisualElement field)
		{
			Focusable focused = field.panel?.focusController?.focusedElement;
			return focused is VisualElement element &&
			       (element == field || field.Contains(element));
		}

		private static void SetMessage(Label label, string message)
		{
			label.text = message ?? "";
			label.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
		}

		// ------- building --------------------------------------------

		private void RebuildRail()
		{
			categoryRail.Clear();
			categoryButtons.Clear();

			if (database == null)
			{
					ShowGridMessage(MenuCopy.Get("PaletteMenu", "error.no-database"), "warning");
				return;
			}

			foreach (MapObjectDatabase.Category category in database.Categories)
			{
				if (category.DebugOnly && !AnaglyphDebugging.DebugMode)
					continue;

				Button button = MakeThumbnailButton("palette-tab", category.Icon, category.Name);
				MapObjectDatabase.Category captured = category;
				button.clicked += () => SelectCategory(captured);

				categoryRail.Add(button);
				categoryButtons.Add((category, button));
			}

			// Whatever was open stays open, unless debug mode just took it away.
			bool selectionStillListed = false;
			foreach ((MapObjectDatabase.Category category, Button _) in categoryButtons)
				selectionStillListed |= category == selectedCategory;

			if (!selectionStillListed)
				selectedCategory = categoryButtons.Count > 0 ? categoryButtons[0].category : null;

			RebuildGrid();
		}

		private void RebuildGrid()
		{
			objectGrid.Clear();
			objectButtons.Clear();

			categoryTitle.text = selectedCategory != null ? selectedCategory.Name : MenuCopy.Get("PaletteMenu", "title");

			if (selectedCategory == null)
			{
				ShowGridMessage(MenuCopy.Get("PaletteMenu", "empty.categories"), "body-copy");
				return;
			}

			foreach (MapObjectDatabase.Entry entry in selectedCategory.Objects)
			{
				// A half-authored entry is skipped rather than made unplaceable-but-clickable.
				if (entry.Prefab == null)
					continue;

				Button button = MakeThumbnailButton("palette-item", entry.Icon, entry.DisplayName);
				MapObject captured = entry.Prefab;
				button.clicked += () => SelectObject(captured);

				objectGrid.Add(button);
				objectButtons.Add((entry.Prefab, button));
			}

			if (objectButtons.Count == 0)
				ShowGridMessage(MenuCopy.Get("PaletteMenu", "empty.objects"), "body-copy");

			RefreshHighlights();
		}

		private void ShowGridMessage(string message, string className)
		{
			objectGrid.Clear();
			objectButtons.Clear();

			Label label = new(message);
			label.AddToClassList(className);
			objectGrid.Add(label);
		}

		/// <summary>An icon above its name, for both the category tabs and the object grid.</summary>
		private static Button MakeThumbnailButton(string className, VectorImage icon, string caption)
		{
			Button button = new();
			button.MakeActOnPress(); // built after the tree-wide pass, so it opts in itself
			button.AddToClassList(className);

			VisualElement thumbnail = new();
			thumbnail.AddToClassList($"{className}-icon");

			// Until an icon exists the empty frame is the placeholder; the caption names it.
			if (icon != null)
				thumbnail.style.backgroundImage = new StyleBackground(icon);

			button.Add(thumbnail);

			Label label = new(caption);
			label.AddToClassList($"{className}-label");
			button.Add(label);

			return button;
		}

		// ------- selection -------------------------------------------

		private void SelectCategory(MapObjectDatabase.Category category)
		{
			if (selectedCategory == category)
				return;

			selectedCategory = category;
			RebuildGrid();
		}

		private void SelectObject(MapObject prefab)
		{
			if (MapEditorTool.CurrentMode == MapEditorTool.Mode.Tags ||
				MapEditorTool.CurrentMode == MapEditorTool.Mode.MeasureTagSize)
				return;

			selectedPrefab = prefab;
			MapEditorTool.SetMode(MapEditorTool.Mode.Place, prefab);
			RefreshHighlights();
		}

		private void RefreshHighlights()
		{
			foreach ((MapObjectDatabase.Category category, Button button) in categoryButtons)
				SetSelected(button, category == selectedCategory);

			foreach ((MapObject prefab, Button button) in objectButtons)
				SetSelected(button, prefab == selectedPrefab);
		}

		private static void SetSelected(VisualElement element, bool selected)
		{
			element.EnableInClassList("selected", selected);
		}

		private static T Require<T>(VisualElement root, string name)
			where T : VisualElement
		{
			T element = root.Q<T>(name);
			if (element == null)
				throw new InvalidOperationException(
					$"Required UI Toolkit element '{name}' ({typeof(T).Name}) was not found.");

			return element;
		}
	}
}
