using System;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.Menu;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>Composes the headset map editor, including its nested navigation and physical tools.</summary>
	public sealed class MapEditingMenuBinder : IDisposable
	{
		private readonly NavView navView;
		private readonly NavPage optionsPage;
		private readonly NavPage alignmentSettingsPage;
		private readonly MapNameBinder mapName;
		private readonly AlignmentMethodBinder alignmentMethod;
		private readonly Button setupTagsButton;
		private bool presented;
		private bool tagSetupRequested;

		public MapEditingMenuBinder(VisualElement root)
		{
			navView = Require<NavView>(root, "map-editing-nav");
			optionsPage = navView.GetPage("map-options-page");
			alignmentSettingsPage = navView.GetPage("alignment-settings-page");
			mapName = new MapNameBinder(Require<VisualElement>(optionsPage, "map-name-section"));
			alignmentMethod = new AlignmentMethodBinder(
				Require<VisualElement>(alignmentSettingsPage, "alignment-method-section"));
			setupTagsButton = Require<Button>(alignmentSettingsPage, "setup-tags-button");
			setupTagsButton.clicked += BeginTagSetup;
			alignmentMethod.Changing += OnMethodChanging;
			alignmentMethod.Changed += OnMethodChanged;
			navView.Changed += OnPageChanged;
		}

		public void Dispose()
		{
			presented = false;
			navView.Changed -= OnPageChanged;
			alignmentMethod.Changing -= OnMethodChanging;
			alignmentMethod.Changed -= OnMethodChanged;
			setupTagsButton.clicked -= BeginTagSetup;
			alignmentMethod.Dispose();
			mapName.Dispose();
		}

		public void SetPresented(bool value)
		{
			if (presented == value)
				return;

			presented = value;
			Refresh();
		}

		public void ResetForEditingSession()
		{
			tagSetupRequested = false;
			navView.GoToPage(optionsPage);
		}

		public void ShowTagsPage()
		{
			tagSetupRequested = true;
			navView.GoToPage(alignmentSettingsPage);
			UpdateToolForPage();
		}

		private void BeginTagSetup()
		{
			string blocker = LaserTagMapCoordinator.Instance?.DescribeTagSetupBlocker();
			if (blocker != null)
			{
				UserErrors.RaiseLocalized(UserErrorArea.Game, "error.alignment-title", "error.alignment-details", blocker);
				return;
			}

			ShowTagsPage();
		}

		private void OnMethodChanging()
		{
			tagSetupRequested = false;
			if (MapEditorTool.CurrentMode == MapEditorTool.Mode.MeasureTagSize)
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
		}

		private void OnMethodChanged()
		{
			UpdateToolForPage();
			Refresh();
		}

		private void OnPageChanged(NavPage page)
		{
			UpdateToolForPage();
			Refresh();
		}

		public void Refresh()
		{
			if (presented && navView.CurrentPage == alignmentSettingsPage)
			{
				alignmentMethod.Refresh();
				UpdateToolForPage();
				setupTagsButton.SetEnabled(LaserTagMapCoordinator.Instance?.DescribeTagSetupBlocker() == null);
			}
			if (presented && navView.CurrentPage != alignmentSettingsPage)
				mapName.Refresh();
		}

		private void UpdateToolForPage()
		{
			if (!presented || !MapEditor.MapEditor.IsActive)
				return;

			bool needsTagTool = navView.CurrentPage == alignmentSettingsPage &&
				(tagSetupRequested || alignmentMethod.UsesTags);
			if (needsTagTool && MapEditorTool.CurrentMode is not (MapEditorTool.Mode.Tags or MapEditorTool.Mode.MeasureTagSize))
			{
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
				return;
			}

			if (!needsTagTool && MapEditorTool.CurrentMode is MapEditorTool.Mode.Tags or MapEditorTool.Mode.MeasureTagSize)
			{
				var selected = MapEditorTool.SelectedObject;
				MapEditorTool.SetMode(selected != null ? MapEditorTool.Mode.Place : MapEditorTool.Mode.Move, selected);
			}
		}
	}
}
