using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;
using Space = Anaglyph.LaserTag.Maps.Space;

namespace Anaglyph.LaserTag.Interface
{
	public sealed class MapPickerBinder : IDisposable
	{
		private readonly bool operatorMode;
		private bool bound, armedDelete;
		private string selectedMapId;
		private string selectedSpaceId;
		private Button newMapButton, newSpaceButton;
		private ScrollView mapList;
		private Action editMap, editSpace;
		private NavView navigation;
		private NavPage deleteSpaceModal;
		private Label deleteSpaceSubject, deleteSpaceDetails, deleteSpaceBlocker;
		private Button confirmDeleteSpace, cancelDeleteSpace;
		private string pendingDeleteSpaceId;

		public MapPickerBinder(bool operatorMode) => this.operatorMode = operatorMode;

		public void Bind(VisualElement root, Action editMap, Action editSpace)
		{
			Dispose();
			this.editMap = editMap; this.editSpace = editSpace;
			root.MakeButtonsActOnPress();
			newMapButton = Require<Button>(root, "new-map-button");
			newSpaceButton = Require<Button>(root, "new-space-button");
			mapList = Require<ScrollView>(root, "map-list");
			navigation = root.GetFirstAncestorOfType<NavView>();
			if (navigation != null)
			{
				deleteSpaceModal = navigation.GetPage("delete-space-modal");
				deleteSpaceModal.MakeButtonsActOnPress();
				deleteSpaceSubject = Require<Label>(deleteSpaceModal, "delete-space-subject");
				deleteSpaceDetails = Require<Label>(deleteSpaceModal, "delete-space-details");
				deleteSpaceBlocker = Require<Label>(deleteSpaceModal, "delete-space-blocker");
				confirmDeleteSpace = Require<Button>(deleteSpaceModal, "confirm-delete-space-button");
				cancelDeleteSpace = Require<Button>(deleteSpaceModal, "cancel-delete-space-button");
				confirmDeleteSpace.clicked += ConfirmSpaceDeletion;
				cancelDeleteSpace.clicked += DismissSpaceDeletion;
				deleteSpaceModal.NavigatingBack += ClearSpaceDeletion;
			}
			Require<Button>(root, "probe-maps-button").style.display = DisplayStyle.None;
			newSpaceButton.style.display = operatorMode ? DisplayStyle.Flex : DisplayStyle.None;
			bound = true;
			newMapButton.clicked += OnNewMap;
			newSpaceButton.clicked += OnNewSpace;
			MenuCopy.Changed += Rebuild;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			SpaceManager.SpaceChanged += Rebuild;
			SpaceManager.MapChanged += Rebuild;
			SpaceManager.SpacesChanged += Rebuild;
			MatchReferee.StateChanged += OnMatchStateChanged;
			Rebuild();
		}

		public void Dispose()
		{
			if (!bound) return;
			bound = false;
			MenuCopy.Changed -= Rebuild;
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			SpaceManager.SpaceChanged -= Rebuild;
			SpaceManager.MapChanged -= Rebuild;
			SpaceManager.SpacesChanged -= Rebuild;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			newMapButton.clicked -= OnNewMap;
			newSpaceButton.clicked -= OnNewSpace;
			mapList.Clear();
			mapList = null; newMapButton = newSpaceButton = null;
			editMap = editSpace = null; armedDelete = false;
			if (deleteSpaceModal != null)
			{
				confirmDeleteSpace.clicked -= ConfirmSpaceDeletion;
				cancelDeleteSpace.clicked -= DismissSpaceDeletion;
				deleteSpaceModal.NavigatingBack -= ClearSpaceDeletion;
				DismissSpaceDeletion();
			}
			navigation = null; deleteSpaceModal = null;
			confirmDeleteSpace = cancelDeleteSpace = null;
			deleteSpaceSubject = deleteSpaceDetails = deleteSpaceBlocker = null;
		}

		private void Select(string id)
		{
			if (selectedMapId == id) return;
			DismissSpaceDeletion();
			selectedMapId = id; selectedSpaceId = null; armedDelete = false; Rebuild();
		}
		private void SelectSpace(string id)
		{
			if (selectedSpaceId == id) return;
			DismissSpaceDeletion();
			selectedSpaceId = id; selectedMapId = null; armedDelete = false; Rebuild();
		}
		private void EditSpace(string id)
		{
			if (SpaceManager.Instance?.Space?.id == id) editSpace?.Invoke();
		}
		private void OnLoad(string id) { if (selectedMapId == id) SpaceManager.Instance?.LoadMap(id); }
		private void OnNewMap() => SpaceManager.Instance?.NewMap();
		private void OnNewSpace()
		{
			if (operatorMode && SpaceManager.Instance?.NewSpace() == true) editSpace?.Invoke();
		}
		private void OnMatchStateChanged(MatchState _) => Rebuild();
		private void OnNetcodeStateChanged(NetcodeState _) => Rebuild();
		private void Delete(string id)
		{
			if (selectedMapId != id) return;
			if (!armedDelete) { armedDelete = true; Rebuild(); return; }
			armedDelete = false; SpaceManager.Instance?.DeleteMap(id); Rebuild();
		}

		private static Space FindSpace(SpaceManager manager, string id)
		{
			if (manager == null || id == null) return null;
			foreach (Space space in manager.Spaces) if (space.id == id) return space;
			return null;
		}

		private void RequestSpaceDeletion(string id)
		{
			var manager = SpaceManager.Instance;
			if (selectedSpaceId != id || deleteSpaceModal == null || manager == null || manager.DeleteSpaceBlocker(id) != null) return;
			pendingDeleteSpaceId = id;
			RefreshSpaceDeletion();
			navigation.PresentModal(deleteSpaceModal, 100);
		}
		private void ConfirmSpaceDeletion()
		{
			string id = pendingDeleteSpaceId;
			var manager = SpaceManager.Instance;
			if (id == null || navigation?.CurrentPage != deleteSpaceModal || manager == null) return;
			if (manager.DeleteSpaceBlocker(id) != null) { RefreshSpaceDeletion(); return; }
			DismissSpaceDeletion();
			manager.DeleteSpace(id);
		}
		private void ClearSpaceDeletion() => pendingDeleteSpaceId = null;
		private void DismissSpaceDeletion()
		{
			ClearSpaceDeletion();
			if (deleteSpaceModal != null) navigation.DismissModal(deleteSpaceModal);
		}
		private void RefreshSpaceDeletion()
		{
			if (pendingDeleteSpaceId == null) return;
			var manager = SpaceManager.Instance;
			Space space = FindSpace(manager, pendingDeleteSpaceId);
			if (space == null) { DismissSpaceDeletion(); return; }
			deleteSpaceSubject.text = MenuCopy.Format("Map", "space.delete-title", space.name);
			deleteSpaceDetails.text = MenuCopy.Get("Map", "space.delete-details");
			confirmDeleteSpace.text = MenuCopy.Get("Map", "space.delete");
			cancelDeleteSpace.text = MenuCopy.Get("Map", "space.cancel-delete");
			string blocker = manager.DeleteSpaceBlocker(space.id);
			confirmDeleteSpace.SetEnabled(blocker == null);
			deleteSpaceBlocker.text = blocker ?? "";
			deleteSpaceBlocker.style.display = blocker == null ? DisplayStyle.None : DisplayStyle.Flex;
		}

		private void Rebuild()
		{
			if (!bound) return;
			RefreshSpaceDeletion();
			var manager = SpaceManager.Instance;
			var currentMap = manager?.Map;
			var currentSpace = manager?.Space;
			List<Space> spaces = manager != null ? new List<Space>(manager.Spaces) : new List<Space>();
			if (manager?.SupportsEnvironmentProbing == true)
				spaces.RemoveAll(space => space != currentSpace && manager.GetSpacePresence(space.id) != MapPresence.Here);
			var visibleIds = new HashSet<string>();
			foreach (var space in spaces) foreach (var map in space.maps) visibleIds.Add(map.id);
			if (selectedMapId != null && !visibleIds.Contains(selectedMapId)) { selectedMapId = null; armedDelete = false; }
			if (selectedSpaceId != null && !spaces.Exists(s => s.id == selectedSpaceId)) selectedSpaceId = null;

			mapList.Clear();
			if (spaces.Count == 0)
			{
				var empty = new Label(MenuCopy.Get("Map", operatorMode ? "maps.empty-operator" : "space.searching")) { name = "catalog-empty" };
				empty.AddToClassList("body-copy"); mapList.Add(empty);
			}
			foreach (var space in spaces)
			{
				bool active = space == currentSpace;
				var group = new VisualElement { name = "space-" + space.id, userData = space.id };
				group.AddToClassList("catalog-space");
				var heading = new VisualElement { name = "space-row" }; heading.AddToClassList("catalog-space-row");
				group.Add(heading);
				var select = new Button { clickable = new PressClickable(() => SelectSpace(space.id)), name = "select-space-button", tooltip = space.name };
				select.AddToClassList("map-row"); select.EnableInClassList("selected", selectedSpaceId == space.id);
				var title = new Label(space.name) { pickingMode = PickingMode.Ignore }; title.AddToClassList("catalog-space-name"); select.Add(title);
				var status = new Label(MenuCopy.Get("Map", active ? "space.active" : "space.presence." + (manager?.GetSpacePresence(space.id) ?? MapPresence.Unknown))) { pickingMode = PickingMode.Ignore };
				status.AddToClassList("catalog-row-status"); select.Add(status); heading.Add(select);
				if (active)
				{
					var edit = IconButton("edit-space-button", "catalog-edit", MenuCopy.Get("Map", "space.edit"), () => EditSpace(space.id));
					edit.SetEnabled(editSpace != null);
					heading.Add(edit);
				}
				if (selectedSpaceId == space.id)
				{
					var delete = IconButton("delete-space-button", "catalog-delete", MenuCopy.Get("Map", "space.delete"), () => RequestSpaceDeletion(space.id));
					delete.AddToClassList("destructive");
					string deleteBlocker = manager == null ? MenuCopy.Get("Map", "maps.unavailable") : manager.DeleteSpaceBlocker(space.id);
					delete.SetEnabled(deleteSpaceModal != null && deleteBlocker == null);
					if (deleteBlocker != null) delete.tooltip = deleteBlocker;
					heading.Add(delete);
				}
				var children = new VisualElement(); children.AddToClassList("catalog-maps"); group.Add(children);
				var maps = new List<Map>(space.maps);
				maps.Sort((a, b) => b.lastUsed.CompareTo(a.lastUsed));
				foreach (var map in maps) children.Add(MapRow(map, map == currentMap, manager));
				if (children.childCount == 0)
				{
					var create = new Button { clickable = new PressClickable(() => {
						var current = SpaceManager.Instance;
						if (current?.Space?.id == space.id) current.NewMap();
						else current?.LoadSpace(space.id);
					}), text = MenuCopy.Get("Map", "GameMapsPage.new-map-button.text") };
					create.AddToClassList("catalog-empty-space");
					string blocker = manager == null ? MenuCopy.Get("Map", "maps.unavailable") : active ? manager.NewMapBlocker() : manager.LoadSpaceBlocker(space.id);
					SetBlocker(create, blocker); children.Add(create);
				}
				mapList.Add(group);
			}
			mapList.MakeButtonsActOnPress();
			newSpaceButton.text = MenuCopy.Get("Map", "space.new");
			SetBlocker(newMapButton, manager == null ? MenuCopy.Get("Map", "maps.unavailable") : manager.NewMapBlocker());
			SetBlocker(newSpaceButton, manager == null ? MenuCopy.Get("Map", "maps.unavailable") : manager.NewSpaceBlocker());
		}

		private VisualElement MapRow(Map map, bool current, SpaceManager manager)
		{
			string id = map.id;
			var row = new VisualElement { name = "map-" + id, userData = id }; row.AddToClassList("catalog-map-row");
			var select = new Button { clickable = new PressClickable(() => Select(id)), name = "select-map-button", tooltip = map.name };
			select.AddToClassList("map-row"); select.EnableInClassList("selected", selectedMapId == id);
			var label = new Label(map.name) { pickingMode = PickingMode.Ignore }; label.AddToClassList("catalog-map-name"); select.Add(label);
			if (current)
			{
				var status = new Label(MenuCopy.Get("Map", manager.IsChangingMap ? "maps.loading" : "maps.loaded")) { pickingMode = PickingMode.Ignore };
				status.AddToClassList("catalog-row-status"); select.Add(status);
			}
			row.Add(select);
			if (current)
			{
				var edit = IconButton("edit-map-button", "catalog-edit", MenuCopy.Get("Map", "maps.edit"), () => {
					if (SpaceManager.Instance?.Map?.id == id) editMap?.Invoke();
				});
				edit.SetEnabled(editMap != null); row.Add(edit);
			}
			if (selectedMapId == id)
			{
				string action = MenuCopy.Get("Map", manager != null && manager.Role != SpaceRole.Offline ? "maps.switch" : "maps.load");
				var load = IconButton("load-map-button", "catalog-load", action, () => OnLoad(id));
				string loadBlocker = manager == null ? MenuCopy.Get("Map", "maps.unavailable") : manager.LoadMapBlocker(id);
				load.SetEnabled(loadBlocker == null); if (loadBlocker != null) load.tooltip = loadBlocker;
				row.Add(load);
				var delete = IconButton("delete-map-button", "catalog-delete", MenuCopy.Get("Map", "maps.delete"), () => Delete(id));
				delete.AddToClassList("destructive"); delete.EnableInClassList("catalog-confirm-delete", armedDelete);
				if (armedDelete) delete.text = MenuCopy.Get("Map", "maps.confirm-delete");
				string blocker = manager == null ? MenuCopy.Get("Map", "maps.unavailable") : manager.DeleteMapBlocker(id);
				delete.SetEnabled(blocker == null); if (blocker != null) delete.tooltip = blocker;
				row.Add(delete);
			}
			return row;
		}
		private static Button IconButton(string name, string icon, string tooltip, Action action)
		{
			var button = new Button { clickable = new PressClickable(action), name = name, tooltip = tooltip };
			button.AddToClassList("catalog-row-action"); button.AddToClassList(icon); return button;
		}
		private static void SetBlocker(Button button, string blocker) { button.tooltip = blocker ?? string.Empty; button.SetEnabled(blocker == null); }
	}
}
