using System;
using Anaglyph.Menu;
using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Netcode;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>Shared map catalog and actions, bound explicitly by the headset or operator menu.</summary>
	public sealed class MapPickerBinder : IDisposable
	{
		private readonly Func<GameMap, bool> includeMap;
		private readonly Func<int, string> getEmptyStateKey;
		private bool bound;

		/// <param name="includeMap">Whether a saved map belongs in this host's catalog.</param>
		/// <param name="getEmptyStateKey">Game table key for an empty catalog, given the unfiltered saved count.</param>
		public MapPickerBinder(Func<GameMap, bool> includeMap, Func<int, string> getEmptyStateKey)
		{
			this.includeMap = includeMap ?? throw new ArgumentNullException(nameof(includeMap));
			this.getEmptyStateKey = getEmptyStateKey ?? throw new ArgumentNullException(nameof(getEmptyStateKey));
		}

		private Label currentMapLabel;
		private Button newMapButton;
		private Button loadButton;
		private Button deleteButton;
		private ScrollView mapList;

		// The list picks a map; the buttons below act on it.
		private string selectedMapId;

		// Deleting is destructive; the first press only arms the button.
		private bool armedDelete;

		public void Bind(VisualElement root)
		{
			Dispose();
			root.MakeButtonsActOnPress();
			currentMapLabel = Require<Label>(root, "current-map-label");
			newMapButton = Require<Button>(root, "new-map-button");
			loadButton = Require<Button>(root, "load-map-button");
			deleteButton = Require<Button>(root, "delete-map-button");
			mapList = Require<ScrollView>(root, "map-list");

			bound = true;
			newMapButton.clicked += OnNewMapClicked;
			loadButton.clicked += OnLoadClicked;
			deleteButton.clicked += OnDeleteClicked;

			MenuCopy.Changed += Rebuild;
			MapStore.Default.Changed += Rebuild;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			LaserTagMapCoordinator.CurrentMapChanged += OnCurrentMapChanged;
			LaserTagMapCoordinator.ProbeResultsChanged += Rebuild;
			LaserTagMapCoordinator.ChangingMapChanged += Rebuild;
			MatchReferee.StateChanged += OnMatchStateChanged;
			LaserTagMapCoordinator.ColocationSettingsChanged += Rebuild;

			Rebuild();
		}

		public void Dispose()
		{
			if (!bound) return;
			bound = false;
			MenuCopy.Changed -= Rebuild;
			LaserTagMapCoordinator.ColocationSettingsChanged -= Rebuild;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			LaserTagMapCoordinator.ChangingMapChanged -= Rebuild;
			LaserTagMapCoordinator.ProbeResultsChanged -= Rebuild;
			LaserTagMapCoordinator.CurrentMapChanged -= OnCurrentMapChanged;

			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MapStore.Default.Changed -= Rebuild;

			newMapButton.clicked -= OnNewMapClicked;
			loadButton.clicked -= OnLoadClicked;
			deleteButton.clicked -= OnDeleteClicked;
			mapList.Clear();
			currentMapLabel = null;
			newMapButton = loadButton = deleteButton = null;
			mapList = null;
		}

		private void Select(string mapId)
		{
			if (selectedMapId == mapId)
				return;

			selectedMapId = mapId;
			armedDelete = false;
			Rebuild();
		}

		private void OnLoadClicked()
		{
			if (selectedMapId != null && LaserTagMapCoordinator.Instance != null)
				LaserTagMapCoordinator.Instance.ChangeMap(selectedMapId);
		}

		private void OnDeleteClicked()
		{
			if (selectedMapId == null)
				return;

			if (!armedDelete)
			{
				armedDelete = true;
				Rebuild();
				return;
			}

			armedDelete = false;
			LaserTagMapCoordinator.Instance?.DeleteMap(selectedMapId);
		}

		private void OnCurrentMapChanged(GameMap _) => Rebuild();
		private void OnMatchStateChanged(MatchState _) => Rebuild();
		private void OnNetcodeStateChanged(NetcodeState _) => Rebuild();
		private void OnNewMapClicked()
		{
			LaserTagMapCoordinator.Instance?.NewMap();
		}

		private void Rebuild()
		{
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			GameMap current = manager != null ? manager.CurrentMap : null;
			bool inSession = manager != null && manager.Phase != MapPhase.Local;

			bool changing = manager != null && manager.IsChangingMap;

			// The hold names the way out: a map whose references are not in this room never
			// finishes aligning, and picking another one is what ends it.
			currentMapLabel.text = current == null ? MenuCopy.Get("Game", "maps.none")
				: changing ? MenuCopy.Format("Game", "maps.aligning", current.name)
				: MenuCopy.Format("Game", "maps.current", current.name);

			string newMapBlocker = manager == null ? MenuCopy.Get("Game", "maps.unavailable")
				: manager.DescribeNewMapBlocker();

			newMapButton.tooltip = newMapBlocker ?? string.Empty;
			newMapButton.SetEnabled(newMapBlocker == null);

			mapList.Clear();

			List<GameMap> maps = MapStore.Default.GetByLastUsed();
			int total = maps.Count;

			maps.RemoveAll(map => !includeMap(map));

			// Deleted, or hidden as belonging to another room: either way the selection is now
			// naming a row nobody can see, and every control below acts on the selection.
			if (selectedMapId != null && !maps.Exists(m => m.id == selectedMapId))
			{
				selectedMapId = null;
				armedDelete = false;
			}

			if (maps.Count == 0)
			{
				string message = MenuCopy.Get("Game", getEmptyStateKey(total));
				Label empty = new(message);
				empty.AddToClassList("body-copy");
				mapList.Add(empty);
			}

			foreach (GameMap map in maps)
			{
				bool isCurrent = current != null && current.id == map.id;
				string id = map.id;

				Button row = new(() => Select(id))
				{
					text = DescribeMap(map, isCurrent)
				};
				row.AddToClassList("map-row");
				row.EnableInClassList("selected", selectedMapId == id);
				mapList.Add(row);
			}


			// The host may change the session's map between rounds; LaserTagMapCoordinator owns the rules,
			// and reports the one that blocks so the disabled button can say why.
			string blocker = selectedMapId == null ? MenuCopy.Get("Game", "maps.select")
				: manager == null ? MenuCopy.Get("Game", "maps.unavailable")
				: manager.DescribeChangeBlocker(selectedMapId);

			loadButton.text = inSession ? MenuCopy.Get("Game", "maps.switch") : MenuCopy.Get("Game", "maps.load");
			loadButton.tooltip = blocker ?? string.Empty;
			loadButton.SetEnabled(blocker == null);

			string deleteBlocker = manager == null ? MenuCopy.Get("Game", "maps.unavailable")
				: manager.DescribeDeleteBlocker(selectedMapId);
			deleteButton.text = armedDelete ? MenuCopy.Get("Game", "maps.confirm-delete") : MenuCopy.Get("Game", "maps.delete");
			deleteButton.tooltip = deleteBlocker ?? string.Empty;
			deleteButton.SetEnabled(deleteBlocker == null);
		}

		private static string DescribeMap(GameMap map, bool isCurrent)
		{
			string age = DescribeAge(map.lastUsed);
			string key = map.HasTags
				? (isCurrent ? "maps.row-tags-loaded" : "maps.row-tags")
				: (isCurrent ? "maps.row-loaded" : "maps.row");
			return MenuCopy.Format("Game", key, map.name, age, map.tags.Count);
		}

		private static string DescribeAge(long ticks)
		{
			TimeSpan age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);

			if (age.TotalMinutes < 1) return MenuCopy.Get("Game", "age.now");
			if (age.TotalHours < 1) return MenuCopy.Format("Game", "age.minutes", (int)age.TotalMinutes);
			if (age.TotalDays < 1) return MenuCopy.Format("Game", "age.hours", (int)age.TotalHours);
			return MenuCopy.Format("Game", "age.days", (int)age.TotalDays);
		}
	}
}
