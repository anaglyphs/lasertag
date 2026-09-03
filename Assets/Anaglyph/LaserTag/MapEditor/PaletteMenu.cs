using System;
using System.Collections.Generic;
using Anaglyph.Debugging;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.MapEditor
{
	/// <summary>
	/// The map editor's palette. A home page of mode tiles leads to one page per mode: objects
	/// (a category rail from <see cref="MapObjectDatabase"/> beside a thumbnail grid), tags, and
	/// the map's own settings.
	///
	/// The page IS the mode — navigating somewhere puts <see cref="MapEditorTool"/> in the mode
	/// that page authors, so the two can never disagree. Home is Move: with no page open the
	/// hands still reposition what is already placed.
	///
	/// The object grid is built at runtime; the database is the only place objects are listed, so
	/// adding one there is all it takes to be able to place it.
	/// </summary>
	[RequireComponent(typeof(UIDocument))]
	public class PaletteMenu : MonoBehaviour
	{
		[SerializeField] private MapObjectDatabase database;

		private NavView navView;
		private NavPage homePage;
		private NavPage objectsPage;
		private NavPage tagsPage;
		private NavPage mapSettingsPage;

		private VisualElement categoryRail;
		private Label categoryTitle;
		private ScrollView objectGrid;

		private Slider tagSizeSlider;
		private Label tagSizeNote;
		private Label tagStatus;

		// The size the slider is passing through, held back until it settles.
		private const float tagSizeSettleSeconds = 0.25f;
		private float? pendingTagSizeCm;
		private float pendingTagSizeTime;

		private TextField mapNameField;
		private Label mapNameNote;
		private Label mapSummary;

		private Button doneButton;

		private readonly List<(MapObjectDatabase.Category category, Button button)> categoryButtons = new();
		private readonly List<(MapObject prefab, Button button)> objectButtons = new();

		private MapObjectDatabase.Category selectedCategory;
		private MapObject selectedPrefab;

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
			homePage = navView.GetPage("home-page");
			objectsPage = navView.GetPage("objects-page");
			tagsPage = navView.GetPage("tags-page");
			mapSettingsPage = navView.GetPage("map-settings-page");

			categoryRail = Require<VisualElement>(root, "category-rail");
			categoryTitle = Require<Label>(root, "category-title");
			objectGrid = Require<ScrollView>(root, "object-grid");

			tagSizeSlider = Require<Slider>(root, "tag-size-slider");
			tagSizeNote = Require<Label>(root, "tag-size-note");
			tagStatus = Require<Label>(root, "tag-status");

			mapNameField = Require<TextField>(root, "map-name-field");
			mapNameNote = Require<Label>(root, "map-name-note");
			mapSummary = Require<Label>(root, "map-summary");

			doneButton = Require<Button>(root, "done-button");
			doneButton.clicked += FinishEditing;

			// A typed size commits on Enter or on leaving the field, not per keystroke.
			TextField tagSizeInput = tagSizeSlider.Q<TextField>();
			if (tagSizeInput != null)
				tagSizeInput.isDelayed = true;

			tagSizeSlider.RegisterValueChangedCallback(OnTagSizeChanged);
			mapNameField.RegisterCallback<FocusOutEvent>(OnMapNameCommitted);

			navView.Changed += OnNavPageChanged;
			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;

			RebuildRail();

			// The view may have resolved its first page before this subscribed, so the mode the
			// current page authors is applied here rather than waited for.
			OnNavPageChanged(navView.CurrentPage);
		}

		private void OnDisable()
		{
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;

			if (navView != null)
			{
				navView.Changed -= OnNavPageChanged;
				navView = null;
			}

			// Closing the palette is as much a "done" as letting go of the slider.
			FlushPendingTagSize();

			mapNameField.UnregisterCallback<FocusOutEvent>(OnMapNameCommitted);
			tagSizeSlider.UnregisterValueChangedCallback(OnTagSizeChanged);
			doneButton.clicked -= FinishEditing;

			categoryButtons.Clear();
			objectButtons.Clear();
		}

		private void OnDebugModeChanged(bool debugMode) => RebuildRail();

		// ------- navigation is the mode ------------------------------

		private void OnNavPageChanged(NavPage page)
		{
			ApplyModeFor(page);

			if (page == tagsPage)
				RefreshTagPage();
			else if (page == mapSettingsPage)
				RefreshMapSettingsPage();
		}

		/// <summary>
		/// Every page names the mode it authors in. The objects page only arms placement once a
		/// prefab is picked; until then it stays in Move, so the hands still do something.
		/// </summary>
		private void ApplyModeFor(NavPage page)
		{
			if (page == tagsPage)
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
			else if (page == objectsPage && selectedPrefab != null)
				MapEditorTool.SetMode(MapEditorTool.Mode.Place, selectedPrefab);
			else
				MapEditorTool.SetMode(MapEditorTool.Mode.Move);
		}

		// A blocker turns on alignment and on what a mode just did, neither of which raises an
		// event this menu could subscribe to.
		private void Update()
		{
			if (navView == null)
				return;

			// Ticked wherever the palette is, so a size set and then navigated away from still
			// lands.
			if (pendingTagSizeCm.HasValue &&
			    Time.unscaledTime - pendingTagSizeTime >= tagSizeSettleSeconds)
				FlushPendingTagSize();

			if (navView.CurrentPage == tagsPage)
				RefreshTagPage();
			else if (navView.CurrentPage == mapSettingsPage)
				RefreshMapSettingsPage();
		}

		// ------- tags page -------------------------------------------

		// A drag reports a new size every frame it moves, and each one would be a separate size for
		// the whole session to agree on. Only the size it settles on is. The slider reports no
		// drag start or end and does not take focus, so settling is measured rather than observed.
		private void OnTagSizeChanged(ChangeEvent<float> change)
		{
			pendingTagSizeCm = change.newValue;
			pendingTagSizeTime = Time.unscaledTime;
		}

		private void FlushPendingTagSize()
		{
			if (pendingTagSizeCm.HasValue)
				CommitTagSize(pendingTagSizeCm.Value);
		}

		private void CommitTagSize(float centimeters)
		{
			pendingTagSizeCm = null;

			MapManager manager = MapManager.Instance;
			if (manager == null || manager.SetTagSize(centimeters))
				return;

			// Refused — put the slider back rather than leaving it showing a size nothing uses.
			RefreshTagPage();
		}

		private void RefreshTagPage()
		{
			MapManager manager = MapManager.Instance;
			if (manager == null)
			{
				tagStatus.text = "Map system unavailable.";
				return;
			}

			string sizeBlocker = manager.DescribeTagSizeBlocker();
			tagSizeSlider.SetEnabled(sizeBlocker == null);
			SetMessage(tagSizeNote, sizeBlocker);

			// Refreshed every frame, so a value being dragged or typed must survive the refresh.
			// A slider drag is only visible as an uncommitted value: it never takes focus.
			if (!pendingTagSizeCm.HasValue && !IsBeingEdited(tagSizeSlider))
				tagSizeSlider.SetValueWithoutNotify(manager.EffectiveTagSizeCm);

			int registered = manager.CurrentMap != null ? manager.CurrentMap.tags.Count : 0;
			string registrationBlocker = manager.DescribeTagRegistrationBlocker();

			tagStatus.text = registrationBlocker != null
				? $"{registered} registered — {registrationBlocker}."
				: $"{registered} registered.";
		}

		// ------- map settings page -----------------------------------

		private void OnMapNameCommitted(FocusOutEvent _)
		{
			MapManager manager = MapManager.Instance;
			if (manager == null || !manager.RenameMap(mapNameField.value))
				RefreshMapSettingsPage();
		}

		private void RefreshMapSettingsPage()
		{
			MapManager manager = MapManager.Instance;
			if (manager == null)
			{
				mapSummary.text = "Map system unavailable.";
				return;
			}

			string renameBlocker = manager.DescribeRenameBlocker();
			mapNameField.SetEnabled(renameBlocker == null);
			SetMessage(mapNameNote, renameBlocker);

			GameMap map = manager.CurrentMap;

			if (!IsBeingEdited(mapNameField))
				mapNameField.SetValueWithoutNotify(map != null ? map.name : "");

			mapSummary.text = map == null
				? "No map loaded. One is created as soon as you place something."
				: $"{map.objects.Count} objects, {map.tags.Count} tags, {map.anchors.Count} anchors.";
		}

		// ------- building --------------------------------------------

		private void RebuildRail()
		{
			categoryRail.Clear();
			categoryButtons.Clear();

			if (database == null)
			{
				ShowGridMessage("No map object database assigned.", "warning");
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

			categoryTitle.text = selectedCategory != null ? selectedCategory.Name : "Palette";

			if (selectedCategory == null)
			{
				ShowGridMessage("No object categories to show.", "body-copy");
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
				ShowGridMessage("Nothing in this category yet.", "body-copy");

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
		private static Button MakeThumbnailButton(string className, Sprite icon, string caption)
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
			selectedPrefab = prefab;
			MapEditorTool.SetMode(MapEditorTool.Mode.Place, prefab);
			RefreshHighlights();
		}

		private void FinishEditing()
		{
			// Leaving arms nothing: the hands are about to become weapons again.
			MapEditorTool.SetMode(MapEditorTool.Mode.Move);
			selectedPrefab = null;
			homePage.NavigateHere();

			// Edits already save on a debounce; this makes leaving the editor the sync point.
			MapManager.Instance?.SaveCurrentMap();

			MapEditor.SetActive(false);
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
