using System;
using Anaglyph.LaserTag.Maps;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>Binds the current map's name independently of page layout or editing tools.</summary>
	public sealed class MapNameBinder : IDisposable
	{
		private readonly TextField field;
		private readonly Label note;
		private string boundMapId;

		public MapNameBinder(VisualElement root)
		{
			field = Require<TextField>(root, "map-name-field");
			note = Require<Label>(root, "map-name-note");
			field.RegisterCallback<FocusOutEvent>(OnCommitted);
			MenuCopy.Changed += Refresh;
		}

		public void Dispose()
		{
			field.UnregisterCallback<FocusOutEvent>(OnCommitted);
			MenuCopy.Changed -= Refresh;
		}

		private void OnCommitted(FocusOutEvent _)
		{
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (manager == null || manager.CurrentMap?.id != boundMapId || !manager.RenameMap(field.value))
				Refresh();
		}

		public void Refresh()
		{
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			string blocker = manager != null ? manager.DescribeRenameBlocker()
				: MenuCopy.Get("Map", "maps.unavailable");
			field.SetEnabled(blocker == null);
			note.text = blocker ?? "";
			note.style.display = blocker == null ? DisplayStyle.None : DisplayStyle.Flex;
			if (manager == null) return;

			// A refresh must not replace a name the user is still typing.
			if (field.panel?.focusController?.focusedElement is VisualElement focused &&
			    (focused == field || field.Contains(focused))) return;
			GameMap map = manager.CurrentMap; boundMapId = map?.id;
			field.SetValueWithoutNotify(map != null ? map.name : "");
		}
	}
}
