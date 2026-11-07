using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Menu;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// Saved-map entries: pressing the map button loads the map. While any pointer is over the entry,
	/// edit and delete show beside the map button.
	/// </summary>
	public sealed class MapRows
	{
		public const string RowUssClassName = "catalog-map-row";
		public const string UnloadableUssClassName = "catalog-map-row--unloadable";
		public const string HoveredUssClassName = "catalog-map-row--hovered";
		public const string ConfirmDeleteUssClassName = "catalog-confirm-delete";

		private string armedDeleteMapId;

		public VisualElement Build(Map map, bool loaded, SpaceManager manager, Action editMap)
		{
			string id = map.id;
			string unavailable = MenuCopy.Get("Map", "maps.unavailable");
			string loadBlocker = manager == null ? unavailable : manager.LoadMapBlocker(id);
			var row = new VisualElement { name = "map-" + id, userData = id };
			row.AddToClassList(RowUssClassName);
			row.EnableInClassList(UnloadableUssClassName, !loaded && loadBlocker != null);

			var load = new Button { clickable = new PressClickable(() => SpaceManager.Instance?.LoadMap(id)), name = "map-row-button" };
			load.tooltip = loaded ? map.name : loadBlocker ?? map.name;
			load.AddToClassList("map-row");
			var label = new Label(map.name) { pickingMode = PickingMode.Ignore }; label.AddToClassList("catalog-map-name"); load.Add(label);
			if (loaded)
			{
				var status = new Label(MenuCopy.Get("Map", manager.IsChangingMap ? "maps.loading" : "maps.loaded")) { pickingMode = PickingMode.Ignore };
				status.AddToClassList("catalog-row-status"); load.Add(status);
			}
			row.Add(load);

			var edit = IconButton("edit-map-button", "catalog-edit", MenuCopy.Get("Map", "maps.edit"), () => LoadAndEdit(id, editMap));
			SetBlocker(edit, editMap == null ? unavailable : loaded ? null : loadBlocker, edit.tooltip);
			row.Add(edit);

			Button delete = null;
			delete = IconButton("delete-map-button", "catalog-delete", MenuCopy.Get("Map", "maps.delete"), () => Delete(id, delete));
			delete.AddToClassList("destructive");
			SetBlocker(delete, manager == null ? unavailable : manager.DeleteMapBlocker(id), delete.tooltip);
			ShowDeleteArmed(delete, armedDeleteMapId == id);
			row.Add(delete);

			TrackHover(row, onUnhovered: () => Disarm(id, delete));
			return row;
		}

		/// <summary>The entry counts as hovered while any pointer is inside it, so one pointer leaving cannot hide what another is over.</summary>
		private static void TrackHover(VisualElement row, Action onUnhovered)
		{
			var pointersInside = new HashSet<int>();
			row.RegisterCallback<PointerEnterEvent>(enter =>
			{
				if (enter.target != row || !pointersInside.Add(enter.pointerId)) return;
				row.AddToClassList(HoveredUssClassName);
			});
			row.RegisterCallback<PointerLeaveEvent>(leave =>
			{
				if (leave.target != row || !pointersInside.Remove(leave.pointerId) || pointersInside.Count > 0) return;
				row.RemoveFromClassList(HoveredUssClassName);
				onUnhovered();
			});
		}

		private static void LoadAndEdit(string id, Action editMap)
		{
			SpaceManager manager = SpaceManager.Instance;
			if (manager == null) return;
			if (manager.Map?.id != id && !manager.LoadMap(id)) return;
			editMap?.Invoke();
		}

		private void Delete(string id, Button delete)
		{
			if (armedDeleteMapId != id)
			{
				armedDeleteMapId = id;
				ShowDeleteArmed(delete, true);
				return;
			}
			armedDeleteMapId = null;
			SpaceManager.Instance?.DeleteMap(id);
		}

		private void Disarm(string id, Button delete)
		{
			if (armedDeleteMapId != id) return;
			armedDeleteMapId = null;
			ShowDeleteArmed(delete, false);
		}

		private static void ShowDeleteArmed(Button delete, bool armed)
		{
			delete.EnableInClassList(ConfirmDeleteUssClassName, armed);
			delete.text = armed ? MenuCopy.Get("Map", "maps.confirm-delete") : "";
		}

		public static Button IconButton(string name, string icon, string tooltip, Action action)
		{
			var button = new Button { clickable = new PressClickable(action), name = name, tooltip = tooltip };
			button.AddToClassList("catalog-row-action"); button.AddToClassList(icon); return button;
		}

		/// <summary>Disables the button while blocked, with the blocker as its tooltip.</summary>
		public static void SetBlocker(Button button, string blocker, string availableTooltip = "")
		{
			button.tooltip = blocker ?? availableTooltip;
			button.SetEnabled(blocker == null);
		}
	}
}
