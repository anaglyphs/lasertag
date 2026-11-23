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
	/// <summary>Every space saved to this device with its maps, whether or not it belongs to this room. A space is saved once objects are placed in one of its maps.</summary>
	public sealed class MapPickerBinder : IDisposable
	{
		public const string SpaceRowUssClassName = "catalog-space-row";
		public const string SpaceRowHoveredUssClassName = "catalog-space-row--hovered";
		public const string ActiveSpaceRowUssClassName = "catalog-space-row--active";

		private readonly bool operatorMode;
		private readonly MapRows mapRows = new();
		private bool bound;
		private Button newSpaceButton;
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
			newSpaceButton.style.display = operatorMode ? DisplayStyle.Flex : DisplayStyle.None;
			bound = true;
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
			newSpaceButton.clicked -= OnNewSpace;
			mapList.Clear();
			mapList = null; newSpaceButton = null;
			editMap = editSpace = null;
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

		private void EditSpace(string id)
		{
			if (SpaceManager.Instance?.LoadSpace(id) == true) editSpace?.Invoke();
		}
		private void OnNewSpace()
		{
			if (operatorMode && SpaceManager.Instance?.NewSpace() == true) editSpace?.Invoke();
		}
		private void OnMatchStateChanged(MatchState _) => Rebuild();
		private void OnNetcodeStateChanged(NetcodeState _) => Rebuild();

		private static Space FindSpace(SpaceManager manager, string id)
		{
			if (manager == null || id == null) return null;
			foreach (Space space in manager.Spaces) if (space.id == id) return space;
			return null;
		}

		private void RequestSpaceDeletion(string id)
		{
			SpaceManager manager = SpaceManager.Instance;
			if (deleteSpaceModal == null || manager == null || manager.DeleteSpaceBlocker(id) != null) return;
			pendingDeleteSpaceId = id;
			RefreshSpaceDeletion();
			navigation.PresentModal(deleteSpaceModal, 100);
		}
		private void ConfirmSpaceDeletion()
		{
			string id = pendingDeleteSpaceId;
			SpaceManager manager = SpaceManager.Instance;
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
			SpaceManager manager = SpaceManager.Instance;
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

		private VisualElement BuildSpaceRow(Space space, bool active, SpaceManager manager)
		{
			string unavailable = MenuCopy.Get("Map", "maps.unavailable");
			string loadBlocker = manager == null ? unavailable : manager.LoadSpaceBlocker(space.id);
			VisualElement row = new() { name = "space-row" };
			row.AddToClassList(SpaceRowUssClassName);
			row.EnableInClassList(ActiveSpaceRowUssClassName, active);

			Button load = new() { clickable = new PressClickable(() => SpaceManager.Instance?.LoadSpace(space.id)), name = "load-space-button", tooltip = active ? space.name : loadBlocker ?? space.name };
			load.AddToClassList("map-row");
			load.EnableInClassList("option--selected", active);
			Label title = new(space.name) { pickingMode = PickingMode.Ignore }; title.AddToClassList("catalog-space-name"); load.Add(title);
			Label status = new(MenuCopy.Get("Map", active ? "space.active" : "space.presence." + (manager?.GetSpacePresence(space.id) ?? MapPresence.Unknown))) { pickingMode = PickingMode.Ignore };
			status.AddToClassList("catalog-row-status"); load.Add(status);
			row.Add(load);

			Button edit = MapRows.IconButton("edit-space-button", "catalog-edit", MenuCopy.Get("Map", "space.edit"), () => EditSpace(space.id));
			MapRows.SetBlocker(edit, editSpace == null ? unavailable : active ? null : loadBlocker, edit.tooltip);
			row.Add(edit);

			Button delete = MapRows.IconButton("delete-space-button", "catalog-delete", MenuCopy.Get("Map", "space.delete"), () => RequestSpaceDeletion(space.id));
			delete.AddToClassList("destructive");
			MapRows.SetBlocker(delete, manager == null || deleteSpaceModal == null ? unavailable : manager.DeleteSpaceBlocker(space.id), delete.tooltip);
			row.Add(delete);
			if (delete.enabledSelf) row.AddManipulator(new SwipeToReveal(delete, MapRows.SwipedOpenDeleteWidth, () => RequestSpaceDeletion(space.id)));

			MapRows.TrackHover(row, SpaceRowHoveredUssClassName);
			return row;
		}

		private void Rebuild()
		{
			if (!bound) return;
			RefreshSpaceDeletion();
			SpaceManager manager = SpaceManager.Instance;
			Map currentMap = manager?.Map;
			Space currentSpace = manager?.Space;
			List<Space> spaces = manager != null ? new List<Space>(manager.Spaces) : new List<Space>();

			mapList.Clear();
			if (spaces.Count == 0)
			{
				Label empty = new(MenuCopy.Get("Map", operatorMode ? "maps.empty-operator" : currentSpace == null ? "space.searching" : "maps.empty")) { name = "catalog-empty" };
				empty.AddToClassList("body-copy"); mapList.Add(empty);
			}
			foreach (Space space in spaces)
			{
				bool active = space == currentSpace;
				VisualElement group = new() { name = "space-" + space.id, userData = space.id };
				group.AddToClassList("catalog-space");
				group.Add(BuildSpaceRow(space, active, manager));
				VisualElement children = new(); children.AddToClassList("catalog-maps"); group.Add(children);
				List<Map> maps = new(space.maps);
				maps.Sort((a, b) => b.lastUsed.CompareTo(a.lastUsed));
				foreach (Map map in maps) children.Add(mapRows.Build(map, map == currentMap, manager, editMap));
				if (active) children.Add(MapRows.NewMapButton(manager, editMap));
				mapList.Add(group);
			}
			mapList.MakeButtonsActOnPress();
			newSpaceButton.text = MenuCopy.Get("Map", "space.new");
			MapRows.SetBlocker(newSpaceButton, manager == null ? MenuCopy.Get("Map", "maps.unavailable") : manager.NewSpaceBlocker());
		}
	}
}
