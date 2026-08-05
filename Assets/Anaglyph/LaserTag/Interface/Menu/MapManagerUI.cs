using Anaglyph.Netcode;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
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
		private Label currentMapLabel;
		private Button newMapButton;
		private Button probeButton;
		private ScrollView mapList;

		// Deleting is destructive; the first press only arms the button.
		private string armedDeleteId;

		private void OnEnable()
		{
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"MapManagerUI requires an enabled UIDocument with a visual tree.");

			currentMapLabel = Require<Label>(root, "current-map-label");
			newMapButton = Require<Button>(root, "new-map-button");
			probeButton = Require<Button>(root, "probe-maps-button");
			mapList = Require<ScrollView>(root, "map-list");

			newMapButton.clicked += OnNewMapClicked;
			probeButton.clicked += OnProbeClicked;

			MapStore.Changed += Rebuild;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;

			if (MapManager.Instance != null)
			{
				MapManager.Instance.CurrentMapChanged += OnCurrentMapChanged;
				MapManager.Instance.ProbeResultsChanged += Rebuild;
				MapManager.Instance.ChangingMapChanged += Rebuild;
			}

			Rebuild();
		}

		private void OnDisable()
		{
			if (MapManager.Instance != null)
			{
				MapManager.Instance.ChangingMapChanged -= Rebuild;
				MapManager.Instance.ProbeResultsChanged -= Rebuild;
				MapManager.Instance.CurrentMapChanged -= OnCurrentMapChanged;
			}

			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MapStore.Changed -= Rebuild;

			newMapButton.clicked -= OnNewMapClicked;
			probeButton.clicked -= OnProbeClicked;
		}

		private void OnCurrentMapChanged(GameMap _) => Rebuild();
		private void OnNetcodeStateChanged(NetcodeState _) => Rebuild();

		private void OnNewMapClicked()
		{
			if (SyncBus.Active || MapManager.Instance == null)
				return;

			// A blank slate: the next placed object (or registered tag) creates the map.
			MapManager.Instance.UnloadCurrentMap();
		}

		private async void OnProbeClicked()
		{
			if (MapManager.Instance == null)
				return;

			probeButton.SetEnabled(false);

			try
			{
				await MapManager.Instance.ProbeAllMaps(destroyCancellationToken);
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
				probeButton?.SetEnabled(true);
			}
		}

		private void Rebuild()
		{
			MapManager manager = MapManager.Instance;
			GameMap current = manager != null ? manager.CurrentMap : null;
			bool inSession = SyncBus.Active;

			bool changing = manager != null && manager.IsChangingMap;

			currentMapLabel.text = current == null ? "No map loaded"
				: changing ? $"Changing to {current.name} — hold still…"
				: $"Current map: {current.name}";

			newMapButton.SetEnabled(!inSession);

			mapList.Clear();

			List<GameMap> maps = MapStore.GetByLastUsed();

			if (maps.Count == 0)
			{
				Label empty = new("No saved maps yet. Place an object to start one.");
				empty.AddToClassList("body-copy");
				mapList.Add(empty);
				return;
			}

			foreach (GameMap map in maps)
				mapList.Add(BuildRow(map, current, manager, inSession));
		}

		private VisualElement BuildRow(GameMap map, GameMap current, MapManager manager,
			bool inSession)
		{
			bool isCurrent = current != null && current.id == map.id;

			VisualElement row = new();
			row.style.flexDirection = FlexDirection.Row;
			row.style.alignItems = Align.Center;

			Label label = new(DescribeMap(map, manager, isCurrent));
			label.AddToClassList("body-copy");
			label.style.flexGrow = 1;
			row.Add(label);

			// The host may change the session's map between rounds; MapManager owns the rules,
			// and reports the one that blocks so the disabled button can say why.
			string blocker = manager != null
				? manager.DescribeChangeBlocker(map.id)
				: "No map manager in the scene";

			Button loadButton = new(() =>
			{
				if (MapManager.Instance != null && MapManager.Instance.ChangeMap(map.id))
					armedDeleteId = null;
			}) { text = inSession ? "Switch" : "Load", tooltip = blocker ?? string.Empty };
			loadButton.SetEnabled(blocker == null);
			row.Add(loadButton);

			Button deleteButton = new() { text = armedDeleteId == map.id ? "Really?" : "Delete" };
			deleteButton.clicked += () =>
			{
				if (armedDeleteId != map.id)
				{
					armedDeleteId = map.id;
					Rebuild();
					return;
				}

				armedDeleteId = null;
				MapManager.Instance?.DeleteMap(map.id);
			};
			deleteButton.SetEnabled(!inSession || !isCurrent);
			row.Add(deleteButton);

			return row;
		}

		private static string DescribeMap(GameMap map, MapManager manager, bool isCurrent)
		{
			string text = map.name;

			if (map.HasTags)
				text += $"  ⌗{map.tags.Count}";

			text += $"  ·  {DescribeAge(map.lastUsed)}";

			if (manager != null &&
			    manager.ProbeResults.TryGetValue(map.id, out int localized) && localized > 0)
				text += "  ·  in this room";

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
