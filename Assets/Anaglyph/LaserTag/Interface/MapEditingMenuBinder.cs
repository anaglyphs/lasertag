using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>Binds the editing menu's map name and tag registration submenu.</summary>
	public sealed class MapEditingMenuBinder : IDisposable
	{
		private readonly NavView navView;
		private readonly NavPage optionsPage;
		private readonly NavPage alignmentSettingsPage;
		private readonly TextField mapNameField;
		private readonly Label mapNameNote;
		private readonly Slider tagSizeSlider;
		private readonly Label tagSizeNote;
		private readonly Label tagStatus;
		private readonly DropdownField colocationMethodField;
		private readonly Label colocationMethodStatus;
		private readonly VisualElement tagSettings;
		private const float tagSizeSettleSeconds = 0.1f;
		private float? pendingTagSizeCm;
		private float pendingTagSizeTime;
		private bool presented;
		private bool editingTags;

		public MapEditingMenuBinder(VisualElement root)
		{
			navView = Require<NavView>(root, "map-editing-nav");
			optionsPage = navView.GetPage("map-options-page");
			alignmentSettingsPage = navView.GetPage("alignment-settings-page");
			mapNameField = Require<TextField>(root, "map-name-field");
			mapNameNote = Require<Label>(root, "map-name-note");
			tagSizeSlider = Require<Slider>(root, "tag-size-slider");
			tagSizeNote = Require<Label>(root, "tag-size-note");
			tagStatus = Require<Label>(root, "tag-status");
			colocationMethodField = Require<DropdownField>(root, "colocation-method-field");
			colocationMethodStatus = Require<Label>(root, "colocation-method-status");
			tagSettings = Require<VisualElement>(root, "tag-settings");
			colocationMethodField.choices = new List<string> { "Shared spatial anchors", "AprilTags" };
			colocationMethodField.RegisterValueChangedCallback(OnColocationMethodChanged);

			TextField tagSizeInput = tagSizeSlider.Q<TextField>();
			if (tagSizeInput != null)
				tagSizeInput.isDelayed = true;

			tagSizeSlider.RegisterValueChangedCallback(OnTagSizeChanged);
			mapNameField.RegisterCallback<FocusOutEvent>(OnMapNameCommitted);
			navView.Changed += OnPageChanged;
		}

		public void Dispose()
		{
			SetPresented(false);
			colocationMethodField.UnregisterValueChangedCallback(OnColocationMethodChanged);
			navView.Changed -= OnPageChanged;
			tagSizeSlider.UnregisterValueChangedCallback(OnTagSizeChanged);
			mapNameField.UnregisterCallback<FocusOutEvent>(OnMapNameCommitted);
		}

		public void SetPresented(bool value)
		{
			if (presented == value)
				return;

			presented = value;
			if (!value)
				FlushPendingTagSize();
			// Each editing session starts on its options page.
			navView.GoToPage(optionsPage);
			OnPageChanged(navView.CurrentPage);
		}

		public void ShowTagsPage() => navView.GoToPage(alignmentSettingsPage);

		private void OnPageChanged(NavPage page)
		{
			RefreshTagTool();
			Refresh();
		}

		private void RefreshTagTool()
		{
			bool tagsOpen = presented && MapEditor.MapEditor.IsActive && navView.CurrentPage == alignmentSettingsPage &&
				LaserTagMapCoordinator.Instance?.CurrentMap?.preferredColocationMethod == ColocationManager.ColocationMethod.AprilTag;
			if (tagsOpen != editingTags)
			{
				editingTags = tagsOpen;
				MapObject selected = MapEditorTool.SelectedObject;
				MapEditorTool.SetMode(tagsOpen ? MapEditorTool.Mode.Tags :
					selected != null ? MapEditorTool.Mode.Place : MapEditorTool.Mode.Move, selected);
			}
		}

		public void Refresh()
		{
			RefreshTagTool();
			if (pendingTagSizeCm.HasValue &&
			    Time.unscaledTime - pendingTagSizeTime >= tagSizeSettleSeconds)
				FlushPendingTagSize();

			if (!presented)
				return;
			if (navView.CurrentPage == alignmentSettingsPage)
				RefreshTagPage();
			else
				RefreshMapName();
		}

		// ------- tags page -------------------------------------------

		private void OnColocationMethodChanged(ChangeEvent<string> change)
		{
			FlushPendingTagSize();
			int index = colocationMethodField.choices.IndexOf(change.newValue);
			LaserTagMapCoordinator.Instance?.SetPreferredColocationMethod((ColocationManager.ColocationMethod)index);
			Refresh();
		}

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

			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (manager == null || manager.SetTagSize(centimeters))
				return;

			// Refused — put the slider back rather than leaving it showing a size nothing uses.
			RefreshTagPage();
		}

		private void RefreshTagPage()
		{
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			GameMap map = manager != null ? manager.CurrentMap : null;
			var preference = map != null ? map.preferredColocationMethod : ColocationManager.ColocationMethod.MetaSharedAnchor;
			colocationMethodField.SetValueWithoutNotify(colocationMethodField.choices[(int)preference]);
			bool useTags = preference == ColocationManager.ColocationMethod.AprilTag;
			tagSettings.style.display = useTags ? DisplayStyle.Flex : DisplayStyle.None;
			string preferenceBlocker = manager != null ? manager.DescribeColocationPreferenceBlocker() : "Map system unavailable.";
			colocationMethodField.SetEnabled(preferenceBlocker == null);
			colocationMethodField.tooltip = preferenceBlocker ?? "Saved with this map; applies to everyone in its session.";
			string status = preferenceBlocker;
			if (manager != null && manager.IsChangingColocation)
				status = "Preference saved. Preparing shared anchors before switching.";
			else if (useTags && (map == null || !map.HasTags))
				status ??= "Register a tag to use AprilTag alignment. Existing anchors keep the map aligned while you register it.";
			else if (ColocationManager.Instance != null && ColocationManager.Instance.SelectedMethod != preference)
				status ??= "Preference saved. The current method remains active until the preferred method is available.";
			SetMessage(colocationMethodStatus, status);
			if (!useTags) return;
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

			if (registrationBlocker != null)
				tagStatus.text = $"{registered} registered — {registrationBlocker}.";
			else if (manager.SessionIsWaitingOnFirstTag)
				tagStatus.text = "No tags registered — register one to align this session.";
			else
				tagStatus.text = $"{registered} registered.";
		}

		// ------- map name --------------------------------------------

		private void OnMapNameCommitted(FocusOutEvent _)
		{
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (manager == null || !manager.RenameMap(mapNameField.value))
				RefreshMapName();
		}

		private void RefreshMapName()
		{
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (manager == null)
			{
				mapNameField.SetEnabled(false);
				SetMessage(mapNameNote, "Map system unavailable.");
				return;
			}

			string renameBlocker = manager.DescribeRenameBlocker();
			mapNameField.SetEnabled(renameBlocker == null);
			SetMessage(mapNameNote, renameBlocker);

			GameMap map = manager.CurrentMap;

			if (!IsBeingEdited(mapNameField))
				mapNameField.SetValueWithoutNotify(map != null ? map.name : "");
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
