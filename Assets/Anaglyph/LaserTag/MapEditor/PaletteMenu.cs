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
		private Slider tagSizeSlider;
		private Label tagSizeNote;
		private Label tagStatus;
		private Label measurementHint;
		private Button measureTagSizeButton;
		private Button unregisterAllTagsButton;
		private Button doneButton;
		private VisualElement setupGuide;
		private Label setupInstructions;
		private Label setupProgress;

		private readonly List<(MapObjectDatabase.Category category, Button button)> categoryButtons = new();
		private readonly List<(MapObject prefab, Button button)> objectButtons = new();

		private MapObjectDatabase.Category selectedCategory;
		private MapObject selectedPrefab;
		private float? pendingTagSizeCm;
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
			tagSizeSlider = Require<Slider>(root, "tag-size-slider");
			tagSizeNote = Require<Label>(root, "tag-size-note");
			tagStatus = Require<Label>(root, "tag-status");
			measurementHint = Require<Label>(root, "tag-measurement-hint");
			measureTagSizeButton = Require<Button>(root, "measure-tag-size-button");
			unregisterAllTagsButton = Require<Button>(root, "unregister-all-tags-button");
			doneButton = Require<Button>(root, "done-button");
			setupGuide = Require<VisualElement>(root, "tag-setup-guide");
			setupInstructions = Require<Label>(root, "tag-setup-instructions");
			setupProgress = Require<Label>(root, "tag-setup-progress");

			MenuCopy.Changed += OnCopyChanged;
			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			MapEditorTool.ModeChanged += OnToolModeChanged;
			navView.Changed += OnNavigationChanged;
			tagSizeSlider.RegisterValueChangedCallback(OnTagSizeChanged);
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
			tagSizeSlider?.UnregisterValueChangedCallback(OnTagSizeChanged);
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
		}

		private static void OnDoneClicked() => MapEditor.SetActive(false);

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
				context.text = MenuCopy.Format("Game", objects ? "palette.map-context" : "palette.space-context",
					objects ? coordinator?.CurrentMap?.name ?? "" : coordinator?.CurrentSpace?.name ?? "");
			}
			if (pendingTagSizeCm.HasValue && Time.unscaledTime - pendingTagSizeTime >= tagSizeSettleSeconds)
				FlushPendingTagSize();

			if (MapEditorTool.CurrentMode == MapEditorTool.Mode.Tags)
				RefreshTagSettings();
			else if (MapEditorTool.CurrentMode == MapEditorTool.Mode.MeasureTagSize)
				RefreshMeasurementHint();
			RefreshSetupGuide();
		}

		// ------- tag settings ----------------------------------------
		private void RefreshSetupGuide()
		{
			var coordinator = LaserTagMapCoordinator.Instance;
			var setup = coordinator?.TagSetup;
			bool guided = setup?.IsGuidingHeadset == true;
			setupGuide.EnableInClassList("tag-setup-hidden", !guided);
			doneButton.SetEnabled(!guided);
			unregisterAllTagsButton.EnableInClassList("tag-setup-hidden", guided);
			if (!guided) return;
			bool measuring = MapEditorTool.CurrentMode == MapEditorTool.Mode.MeasureTagSize;
			setupInstructions.text = MenuCopy.Get("Game", measuring ? "tag-setup.headset-measure" :
				!setup.SizeConfirmed ? "tag-setup.headset-wait" : "tag-setup.headset-register");
			setupProgress.text = MenuCopy.Format("Game", "tag-setup.headset-progress", coordinator.CurrentSpace.tags.Count,
				coordinator.EffectiveTagSizeCm);
			tagSizeSlider.SetEnabled(false);
			measureTagSizeButton.SetEnabled(false);
		}

		private void OnTagSizeChanged(ChangeEvent<float> change)
		{
			pendingSizeContext = LaserTagMapCoordinator.Instance?.ReferenceContext ?? Guid.Empty;
			pendingTagSizeCm = change.newValue;
			pendingTagSizeTime = Time.unscaledTime;
		}

		private void FlushPendingTagSize()
		{
			if (!pendingTagSizeCm.HasValue)
				return;

			float centimeters = pendingTagSizeCm.Value;
			pendingTagSizeCm = null;
			var manager = LaserTagMapCoordinator.Instance;
			if (manager != null && manager.ReferenceContext == pendingSizeContext) manager.SetTagSize(centimeters);
		}

		private void OnMeasureTagSizeClicked()
		{
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (MapEditorTool.DominantHand == null || manager?.DescribeTagSizeBlocker() != null)
				return;

			FlushPendingTagSize();
			MapEditorTool.SetMode(MapEditorTool.Mode.MeasureTagSize);
		}

		private void OnUnregisterAllTagsClicked()
		{
			LaserTagMapCoordinator.Instance?.UnregisterAllTags();
			RefreshTagSettings();
		}

		private void RefreshTagSettings()
		{
			if (tagSizeSlider == null)
				return;

			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (manager == null)
			{
				tagSizeSlider.SetEnabled(false);
				unregisterAllTagsButton.SetEnabled(false);
				measureTagSizeButton.SetEnabled(false);
				SetMessage(tagSizeNote, null);
				SetMessage(tagStatus, null);
				return;
			}

			string sizeBlocker = manager.DescribeTagSizeBlocker();
			tagSizeSlider.SetEnabled(sizeBlocker == null);
			measureTagSizeButton.SetEnabled(MapEditorTool.DominantHand != null && sizeBlocker == null);
			SetMessage(tagSizeNote, sizeBlocker);

			// A slider drag is an uncommitted local value until it settles; do not overwrite it
			// while the user is dragging or typing merely to reflect the current map value.
			if (!pendingTagSizeCm.HasValue && !IsBeingEdited(tagSizeSlider))
			{
				tagSizeSlider.highValue = Mathf.Max(50f, manager.EffectiveTagSizeCm);
				tagSizeSlider.SetValueWithoutNotify(manager.EffectiveTagSizeCm);
			}

			int registered = manager.CurrentSpace != null ? manager.CurrentSpace.tags.Count : 0;
			string registrationBlocker = manager.DescribeTagRegistrationBlocker();
			unregisterAllTagsButton.SetEnabled(registered > 0 && manager.DescribeTagRemovalBlocker() == null);

			bool twoTags = (ColocationManager.Instance != null ? ColocationManager.Instance.SelectedMethod :
				manager.CurrentSpace?.preferredColocationMethod) == ColocationManager.ColocationMethod.TwoAprilTags;
			if (twoTags)
				tagStatus.text = MenuCopy.Get("Game", "alignment.two-tags-description");
			else if (registrationBlocker != null && registrationBlocker != sizeBlocker)
				tagStatus.text = MenuCopy.Format("Game", "alignment.tag-blocked", registered, registrationBlocker);
			else if (manager.SessionIsWaitingOnFirstTag)
				tagStatus.text = MenuCopy.Get("Game", "alignment.no-tags");
			else
				tagStatus.text = MenuCopy.Format("Game", "alignment.tag-count", registered);
		}

		private void RefreshMeasurementHint()
		{
			if (measurementHint == null)
				return;

			measurementHint.text = MenuCopy.Get("Game", MapEditorTool.MeasurementHint ?? "ruler.first-point");
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
					ShowGridMessage(MenuCopy.Get("Palette", "error.no-database"), "warning");
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

			categoryTitle.text = selectedCategory != null ? selectedCategory.Name : MenuCopy.Get("Palette", "title");

			if (selectedCategory == null)
			{
				ShowGridMessage(MenuCopy.Get("Palette", "empty.categories"), "body-copy");
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
				ShowGridMessage(MenuCopy.Get("Palette", "empty.objects"), "body-copy");

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
