using System;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Menu;
using Anaglyph.Netcode.SyncVariables;
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
			Require<Button>(page, "start-apriltag-setup").EnableInClassList("map-field-hidden", !operatorMode);
			Alignment = new AlignmentSettingsBinder(Require<VisualElement>(page, "alignment-settings-section"), operatorMode);
			Alignment.Changed += Refresh;
			name.RegisterValueChangedCallback(OnNameChanged);
			reset.clicked += Reset;
			page.NavigatingAway += Disarm;
			page.NavigatingHere += Refresh;
			MapSpaceStore.Default.Changed += Refresh;
			LaserTagMapCoordinator.CurrentSpaceChanged += OnSpace;
			LaserTagMapCoordinator.ColocationSettingsChanged += Refresh;
			MenuCopy.Changed += Refresh;
			Refresh();
		}
		private bool IsCurrent => boundSpace != null && boundSpace == LaserTagMapCoordinator.Instance?.CurrentSpace?.id;
		private void FlushPendingSize() => Alignment.FlushPendingSize();
		private void Disarm() { FlushPendingSize(); armedReset = false; Refresh(); }
		private void OnSpace(MapSpace space)
		{
			if (boundSpace != space?.id && page.ParentView?.CurrentPage == page) page.GoBack();
			Refresh();
		}
		private void OnNameChanged(ChangeEvent<string> change)
		{
			if (!IsCurrent || !LaserTagMapCoordinator.Instance.RenameSpace(change.newValue)) Refresh();
		}
		private void Reset()
		{
			if (!IsCurrent) return;
			if (!armedReset) { armedReset = true; Refresh(); return; }
			armedReset = false; LaserTagMapCoordinator.Instance.ResetSpaceAlignment(); Refresh();
		}
		public void Refresh()
		{
			Alignment.Refresh();
			var manager = LaserTagMapCoordinator.Instance; var space = manager?.CurrentSpace;
			if (boundSpace != space?.id)
			{
				boundSpace = space?.id; armedReset = false;
				name.SetValueWithoutNotify(space?.name ?? "");
			}
			if (name.panel?.focusController?.focusedElement is not VisualElement focused || (focused != name && !name.Contains(focused)))
				name.SetValueWithoutNotify(space?.name ?? "");
			name.label = MenuCopy.Get("Game", "space.name"); name.maxLength = LaserTagMapCoordinator.MaxMapNameLength;
			reset.text = MenuCopy.Get("Game", armedReset ? "space.confirm-reset" : "space.reset");
			name.SetEnabled(space != null && manager.DescribeRenameBlocker() == null);
			reset.SetEnabled(space != null && !SyncBus.Active && !manager.IsChangingColocation);
		}
		public void Dispose()
		{
			Alignment.Changed -= Refresh;
			Alignment.Dispose();
			name.UnregisterValueChangedCallback(OnNameChanged);
			reset.clicked -= Reset;
			page.NavigatingAway -= Disarm;
			page.NavigatingHere -= Refresh;
			MapSpaceStore.Default.Changed -= Refresh;
			LaserTagMapCoordinator.CurrentSpaceChanged -= OnSpace;
			LaserTagMapCoordinator.ColocationSettingsChanged -= Refresh;
			MenuCopy.Changed -= Refresh;
		}
	}
}
