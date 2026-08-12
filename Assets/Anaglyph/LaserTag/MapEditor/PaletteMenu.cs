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
	/// The map editor's palette. A rail of categories from <see cref="MapObjectDatabase"/> runs
	/// down the left; the selected category's objects fill the grid beside it as thumbnails.
	/// The whole thing is built at runtime — the database is the only place objects are listed,
	/// so adding one there is all it takes to be able to place it.
	/// </summary>
	[RequireComponent(typeof(UIDocument))]
	public class PaletteMenu : MonoBehaviour
	{
		[SerializeField] private MapObjectDatabase database;

		private VisualElement categoryRail;
		private Label categoryTitle;
		private ScrollView objectGrid;
		private Button moveToolButton;
		private Button tagToolButton;
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

			categoryRail = Require<VisualElement>(root, "category-rail");
			categoryTitle = Require<Label>(root, "category-title");
			objectGrid = Require<ScrollView>(root, "object-grid");
			moveToolButton = Require<Button>(root, "move-tool-button");
			tagToolButton = Require<Button>(root, "tag-tool-button");
			doneButton = Require<Button>(root, "done-button");

			moveToolButton.clicked += SelectMoveTool;
			tagToolButton.clicked += SelectTagTool;
			doneButton.clicked += FinishEditing;

			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			TagRegistrationTool.RegistrationModeChanged += OnRegistrationModeChanged;

			// The editor opens in move mode: a placement armed by a previous session would
			// otherwise be invisible until the first trigger pull placed something.
			SelectMoveTool();

			RebuildRail();
		}

		private void OnDisable()
		{
			TagRegistrationTool.RegistrationModeChanged -= OnRegistrationModeChanged;
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;

			doneButton.clicked -= FinishEditing;
			tagToolButton.clicked -= SelectTagTool;
			moveToolButton.clicked -= SelectMoveTool;

			categoryButtons.Clear();
			objectButtons.Clear();
		}

		private void OnDebugModeChanged(bool debugMode) => RebuildRail();

		private void OnRegistrationModeChanged(bool on) => RefreshHighlights();

		// ------- building -------------------------------------------

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

		// ------- selection ------------------------------------------

		private void SelectCategory(MapObjectDatabase.Category category)
		{
			if (selectedCategory == category)
				return;

			selectedCategory = category;
			RebuildGrid();
		}

		// Tag registration and object placement are mutually exclusive tools, so each of
		// these three sets both halves of the mode rather than only its own.

		private void SelectObject(MapObject prefab)
		{
			selectedPrefab = prefab;
			TagRegistrationTool.SetRegistrationMode(false);
			SetSpawnObject(prefab);
			RefreshHighlights();
		}

		private void SelectMoveTool()
		{
			selectedPrefab = null;
			TagRegistrationTool.SetRegistrationMode(false);
			SetSpawnObject(null);
			RefreshHighlights();
		}

		private void SelectTagTool()
		{
			selectedPrefab = null;
			SetSpawnObject(null);
			TagRegistrationTool.SetRegistrationMode(true);
			RefreshHighlights();
		}

		private void FinishEditing()
		{
			SelectMoveTool();

			// Edits already save on a debounce; this makes leaving the editor the sync point.
			MapManager.Instance?.SaveCurrentMap();

			MapEditor.SetActive(false);
		}

		private void RefreshHighlights()
		{
			bool registering = TagRegistrationTool.RegistrationMode;

			SetSelected(moveToolButton, !registering && selectedPrefab == null);
			SetSelected(tagToolButton, registering);

			foreach ((MapObjectDatabase.Category category, Button button) in categoryButtons)
				SetSelected(button, category == selectedCategory);

			foreach ((MapObject prefab, Button button) in objectButtons)
				SetSelected(button, !registering && prefab == selectedPrefab);
		}

		private static void SetSelected(VisualElement element, bool selected)
		{
			element.EnableInClassList("selected", selected);
		}

		private static void SetSpawnObject(MapObject mapObjPrefab)
		{
			MapEditorTool[] tools = FindObjectsByType<MapEditorTool>(
				FindObjectsInactive.Include, FindObjectsSortMode.None);

			foreach (MapEditorTool tool in tools)
				tool.SetSpawnObject(mapObjPrefab);
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
