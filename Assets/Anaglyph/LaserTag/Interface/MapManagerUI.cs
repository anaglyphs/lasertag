using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// The manual map picker. Required, not optional: optimistic loading will occasionally
	/// pick the wrong map, and a fork is only reachable from here after its parent takes
	/// the auto-load slot. Lives on the same UIDocument as <see cref="GameMenu"/>, driving
	/// the map-manager page; runs after it so button press-handling is already installed.
	/// </summary>
	[DefaultExecutionOrder(101)]
	[RequireComponent(typeof(UIDocument))]
	public class MapManagerUI : MonoBehaviour
	{
		[Tooltip("PC operator mode: list AprilTag maps without probing/filtering by physical space")]
		[SerializeField] private bool operatorMode;

		private Label currentMapLabel;
		private Button editMapButton;
		private Button newMapButton;
		private Button probeButton;
		private Button openPageButton;
		private Button loadButton;
		private Button deleteButton;
		private ScrollView mapList;

		// The list picks a map; the buttons below act on it.
		private string selectedMapId;

		// Deleting is destructive; the first press only arms the button.
		private bool armedDelete;

		private void OnEnable()
		{
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"MapManagerUI requires an enabled UIDocument with a visual tree.");

			currentMapLabel = Require<Label>(root, "current-map-label");
			editMapButton = Require<Button>(root, "edit-map-button");
			newMapButton = Require<Button>(root, "new-map-button");
			probeButton = Require<Button>(root, "probe-maps-button");
			loadButton = Require<Button>(root, "load-map-button");
			deleteButton = Require<Button>(root, "delete-map-button");
			mapList = Require<ScrollView>(root, "map-list");

			SetDisplayed(editMapButton, !operatorMode);
			SetDisplayed(probeButton, !operatorMode);

			// Opening the page is the moment the answer is wanted, and the startup probe may not
			// have run yet or may be describing a room the headset has since left.
			openPageButton = root.Q<Button>("manage-map-button");
			if (openPageButton != null)
				openPageButton.clicked += OnProbeClicked;

			newMapButton.clicked += OnNewMapClicked;
			probeButton.clicked += OnProbeClicked;
			loadButton.clicked += OnLoadClicked;
			deleteButton.clicked += OnDeleteClicked;

			MapStore.Default.Changed += Rebuild;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			LaserTagMapCoordinator.CurrentMapChanged += OnCurrentMapChanged;
			LaserTagMapCoordinator.ProbeResultsChanged += Rebuild;
			LaserTagMapCoordinator.ChangingMapChanged += Rebuild;
			MatchReferee.StateChanged += OnMatchStateChanged;
			LaserTagMapCoordinator.ColocationSettingsChanged += Rebuild;

			Rebuild();
		}

		private void OnDisable()
		{
			LaserTagMapCoordinator.ColocationSettingsChanged -= Rebuild;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			LaserTagMapCoordinator.ChangingMapChanged -= Rebuild;
			LaserTagMapCoordinator.ProbeResultsChanged -= Rebuild;
			LaserTagMapCoordinator.CurrentMapChanged -= OnCurrentMapChanged;

			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MapStore.Default.Changed -= Rebuild;

			if (openPageButton != null)
				openPageButton.clicked -= OnProbeClicked;

			newMapButton.clicked -= OnNewMapClicked;
			probeButton.clicked -= OnProbeClicked;
			loadButton.clicked -= OnLoadClicked;
			deleteButton.clicked -= OnDeleteClicked;
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

		private async void OnProbeClicked()
		{
			if (operatorMode || LaserTagMapCoordinator.Instance == null)
				return;

			// The probe takes tens of seconds, and until it answers the list is showing every saved
			// map because none of them are known to be anywhere. Say so, rather than let that read
			// as the filter being broken.
			probeButton.SetEnabled(false);
			probeButton.text = "Checking…";

			try
			{
				await LaserTagMapCoordinator.Instance.ProbeAllMaps(destroyCancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			finally
			{
				if (probeButton != null)
				{
					probeButton.text = "Check";
					probeButton.SetEnabled(true);
				}
			}
		}

		private void Rebuild()
		{
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			GameMap current = manager != null ? manager.CurrentMap : null;
			bool inSession = manager != null && manager.Phase != MapPhase.Local;

			bool changing = manager != null && manager.IsChangingMap;

			// The hold names the way out: a map whose references are not in this room never
			// finishes aligning, and picking another one is what ends it.
			currentMapLabel.text = current == null ? "No map loaded"
				: changing ? $"Aligning to {current.name} — hold still, or pick another map"
				: $"Current map: {current.name}";

			string newMapBlocker = manager == null ? "No map manager in the scene"
				: manager.DescribeNewMapBlocker();

			newMapButton.tooltip = newMapBlocker ?? string.Empty;
			newMapButton.SetEnabled(newMapBlocker == null);

			mapList.Clear();

			List<GameMap> maps = MapStore.Default.GetByLastUsed();
			int total = maps.Count;

			// A PC operator has no physical-space signal to probe. Its useful catalog is every map
			// that can participate in the AprilTag session it hosts. Headsets keep the spatial-anchor
			// probe filter: only a map actually tested and placed elsewhere is hidden, while an
			// untested one is not known to be anywhere.
			if (operatorMode)
				maps.RemoveAll(m => !m.HasTags);
			else if (manager != null)
				maps.RemoveAll(m => manager.GetMapPresence(m.id) == MapPresence.Elsewhere);

			// Deleted, or hidden as belonging to another room: either way the selection is now
			// naming a row nobody can see, and every control below acts on the selection.
			if (selectedMapId != null && !maps.Exists(m => m.id == selectedMapId))
			{
				selectedMapId = null;
				armedDelete = false;
			}

			if (maps.Count == 0)
			{
				string message = operatorMode
					? "No saved maps contain AprilTags."
					: total == 0
						? "No saved maps yet. Place an object to start one."
						: "No saved maps belong to this room. Place an object to start one.";
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
					text = DescribeMap(map, manager, isCurrent)
				};
				row.AddToClassList("map-row");
				row.EnableInClassList("selected", selectedMapId == id);
				mapList.Add(row);
			}


			// The host may change the session's map between rounds; LaserTagMapCoordinator owns the rules,
			// and reports the one that blocks so the disabled button can say why.
			string blocker = selectedMapId == null ? "No map selected"
				: manager == null ? "No map manager in the scene"
				: manager.DescribeChangeBlocker(selectedMapId);

			loadButton.text = inSession ? "Switch" : "Load";
			loadButton.tooltip = blocker ?? string.Empty;
			loadButton.SetEnabled(blocker == null);

			string deleteBlocker = manager == null ? "No map manager in the scene"
				: manager.DescribeDeleteBlocker(selectedMapId);
			deleteButton.text = armedDelete ? "Really?" : "Delete";
			deleteButton.tooltip = deleteBlocker ?? string.Empty;
			deleteButton.SetEnabled(deleteBlocker == null);
		}

		private static string DescribeMap(GameMap map, LaserTagMapCoordinator manager, bool isCurrent)
		{
			string text = map.name;

			if (map.HasTags)
				text += $"  ⌗{map.tags.Count}";

			text += $"  ·  {DescribeAge(map.lastUsed)}";

			// Only the positive answer is worth saying: a map found in another room is not in the
			// list to be labelled, and an untested one has nothing to report.
			// if (manager != null && manager.GetMapPresence(map.id) == MapPresence.Here)
			// 	text += "  ·  in this room";

			if (isCurrent)
				text += "  ·  loaded";

			return text;
		}

		private static string DescribeAge(long ticks)
		{
			TimeSpan age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);

			if (age.TotalMinutes < 1) return "just now";
			if (age.TotalHours < 1) return $"{(int)age.TotalMinutes}m ago";
			if (age.TotalDays < 1) return $"{(int)age.TotalHours}h ago";
			return $"{(int)age.TotalDays}d ago";
		}

		private static void SetDisplayed(VisualElement element, bool displayed)
		{
			element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
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
