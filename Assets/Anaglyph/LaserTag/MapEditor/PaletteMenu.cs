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
	/// Object categories and thumbnails built from the map object database.
	/// Tag registration and map options live in the game menu.
	/// </summary>
	[RequireComponent(typeof(UIDocument))]
	public class PaletteMenu : MonoBehaviour
	{
		[SerializeField] private MapObjectDatabase database;

		private VisualElement categoryRail;
		private Label categoryTitle;
		private ScrollView objectGrid;

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

			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			MapEditorTool.ModeChanged += OnToolModeChanged;
			RebuildRail();
			OnToolModeChanged(MapEditorTool.CurrentMode);
		}

		private void OnDisable()
		{
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;
			MapEditorTool.ModeChanged -= OnToolModeChanged;
			categoryButtons.Clear();
			objectButtons.Clear();
		}

		private void OnDebugModeChanged(bool debugMode) => RebuildRail();

		private void OnToolModeChanged(MapEditorTool.Mode mode)
		{
			// The tags submenu owns the tools until the user goes back.
			categoryRail.SetEnabled(mode != MapEditorTool.Mode.Tags);
			objectGrid.SetEnabled(mode != MapEditorTool.Mode.Tags);
			selectedPrefab = MapEditorTool.SelectedObject;
			RefreshHighlights();
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
			if (MapEditorTool.CurrentMode == MapEditorTool.Mode.Tags)
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
