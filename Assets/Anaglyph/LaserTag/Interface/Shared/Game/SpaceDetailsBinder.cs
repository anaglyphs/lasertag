using System;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Menu;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	public sealed class SpaceDetailsBinder : IDisposable
	{
		private readonly NavPage page;
		private readonly TextField name;
		private readonly Button reset;
		public AlignmentSettingsBinder Alignment { get; }
		private bool armedReset;
		private string boundSpace;

		public SpaceDetailsBinder(NavPage page, bool operatorMode = false)
		{
			this.page = page;
			page.MakeButtonsActOnPress();
			name = Require<TextField>(page, "space-name-field");
			reset = Require<Button>(page, "reset-space-alignment");
			Alignment = new AlignmentSettingsBinder(Require<VisualElement>(page, "alignment-settings-section"), operatorMode);
			Alignment.Changed += Refresh;
			name.RegisterValueChangedCallback(OnNameChanged);
			reset.clicked += Reset;
			page.NavigatingAway += Disarm;
			page.NavigatingHere += Refresh;
			SpaceManager.SpaceChanged += OnSpace;
			SpaceManager.SpacesChanged += Refresh;
			MenuCopy.Changed += Refresh;
			Refresh();
		}
		private bool IsCurrent => boundSpace != null && boundSpace == SpaceManager.Instance?.Space?.id;
		private void FlushPendingSize() => Alignment.FlushPendingSize();
		private void Disarm() { FlushPendingSize(); armedReset = false; Refresh(); }
		private void OnSpace()
		{
			if (boundSpace != SpaceManager.Instance?.Space?.id && page.ParentView?.CurrentPage == page) page.GoBack();
			Refresh();
		}
		private void OnNameChanged(ChangeEvent<string> change)
		{
			if (!IsCurrent || !SpaceManager.Instance.RenameSpace(change.newValue)) Refresh();
		}
		private void Reset()
		{
			if (!IsCurrent) return;
			if (!armedReset) { armedReset = true; Refresh(); return; }
			armedReset = false; SpaceManager.Instance.ResetAlignment(); Refresh();
		}
		public void Refresh()
		{
			Alignment.Refresh();
			var manager = SpaceManager.Instance; var space = manager?.Space;
			if (boundSpace != space?.id)
			{
				boundSpace = space?.id; armedReset = false;
				name.SetValueWithoutNotify(space?.name ?? "");
			}
			if (name.panel?.focusController?.focusedElement is not VisualElement focused || (focused != name && !name.Contains(focused)))
				name.SetValueWithoutNotify(space?.name ?? "");
			name.label = MenuCopy.Get("Map", "space.name"); name.maxLength = Space.MaxNameLength;
			reset.text = MenuCopy.Get("Map", armedReset ? "space.confirm-reset" : "space.reset");
			name.SetEnabled(manager != null && manager.RenameSpaceBlocker() == null);
			reset.SetEnabled(manager != null && manager.ResetAlignmentBlocker() == null);
		}
		public void Dispose()
		{
			Alignment.Changed -= Refresh;
			Alignment.Dispose();
			name.UnregisterValueChangedCallback(OnNameChanged);
			reset.clicked -= Reset;
			page.NavigatingAway -= Disarm;
			page.NavigatingHere -= Refresh;
			SpaceManager.SpaceChanged -= OnSpace;
			SpaceManager.SpacesChanged -= Refresh;
			MenuCopy.Changed -= Refresh;
		}
	}
}
