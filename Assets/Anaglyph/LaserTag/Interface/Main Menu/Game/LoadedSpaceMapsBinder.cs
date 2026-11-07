using System;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>The loaded space's maps, or a notice while the headset recognizes which space it is in.</summary>
	public sealed class LoadedSpaceMapsBinder : IDisposable
	{
		private readonly ScrollView mapList;
		private readonly FlashingLabel recognizingLabel;
		private readonly Action editMap;
		private readonly MapRows mapRows = new();

		public LoadedSpaceMapsBinder(VisualElement section, Action editMap)
		{
			this.editMap = editMap;
			mapList = Require<ScrollView>(section, "map-list");
			recognizingLabel = Require<FlashingLabel>(section, "recognizing-space-label");
			MenuCopy.Changed += Rebuild;
			SpaceManager.SpaceChanged += Rebuild;
			SpaceManager.MapChanged += Rebuild;
			SpaceManager.SpacesChanged += Rebuild;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			MatchReferee.StateChanged += OnMatchStateChanged;
			Rebuild();
		}

		public void Dispose()
		{
			MenuCopy.Changed -= Rebuild;
			SpaceManager.SpaceChanged -= Rebuild;
			SpaceManager.MapChanged -= Rebuild;
			SpaceManager.SpacesChanged -= Rebuild;
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MatchReferee.StateChanged -= OnMatchStateChanged;
			mapList.Clear();
		}

		private void OnNetcodeStateChanged(NetcodeState _) => Rebuild();
		private void OnMatchStateChanged(MatchState _) => Rebuild();

		private void Rebuild()
		{
			SpaceManager manager = SpaceManager.Instance;
			Space space = manager?.Space;
			bool recognizing = space == null;
			recognizingLabel.style.display = recognizing ? DisplayStyle.Flex : DisplayStyle.None;
			recognizingLabel.Flashing = recognizing;
			mapList.style.display = recognizing ? DisplayStyle.None : DisplayStyle.Flex;
			mapList.Clear();
			if (recognizing) return;

			foreach (Map map in space.maps) mapList.Add(mapRows.Build(map, map == manager.Map, manager, editMap));
			mapList.Add(NewMapButton(manager));
			mapList.MakeButtonsActOnPress();
		}

		private static Button NewMapButton(SpaceManager manager)
		{
			var button = new Button { clickable = new PressClickable(() => SpaceManager.Instance?.NewMap()), name = "new-map-button", text = "+" };
			button.AddToClassList("catalog-new-map");
			MapRows.SetBlocker(button, manager.NewMapBlocker(), MenuCopy.Get("Map", "GameMapsPage.new-map-button.text"));
			return button;
		}
	}
}
