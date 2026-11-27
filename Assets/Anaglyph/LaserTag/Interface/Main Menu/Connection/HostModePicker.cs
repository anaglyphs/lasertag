using System;
using System.Collections.Generic;
using Anaglyph.Menu;
using Anaglyph.VariableObjects;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;
using Protocol = Anaglyph.Netcode.NetcodeManagement.Protocol;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// Three tiles choosing how this headset hosts, stacked by how far the connection travels: a path
	/// joins the two headsets through the selected tile. A tile the device's network can't use is outlined, and
	/// tapping it raises an alert saying why. The saved choice is kept while unusable, and hosting falls
	/// back to the next usable mode.
	/// </summary>
	internal sealed class HostModePicker
	{
		private const string Table = "ConnectionMenu";
		private const string WifiOffReasonKey = "host-mode.blocked.wifi-off";
		private const string BluetoothOffReasonKey = "host-mode.blocked.bluetooth";
		private static readonly Protocol[] FallbackOrder = { Protocol.LAN, Protocol.Direct, Protocol.UnityService };
		private const float PathWidth = 6;
		private const float GapBetweenRelayPathAndPerson = 6;
		private const float RelayPathCornerCut = 20;

		private readonly IntObject savedMode;
		private readonly Label about;
		private readonly Button hostButton;
		private readonly VisualElement diagram;
		private readonly VisualElement leftHeadset;
		private readonly VisualElement rightHeadset;
		private readonly Action<MenuError> showAlert;
		private readonly Dictionary<Protocol, Button> cards = new();
		private readonly Dictionary<Protocol, string> unusableReasonKeys = new();

		public Protocol? SelectedMode { get; private set; }

		public HostModePicker(VisualElement root, UIEventBindings bindings, IntObject savedMode, bool directSupported,
			Action<MenuError> showAlert)
		{
			this.savedMode = savedMode;
			this.showAlert = showAlert;
			diagram = Require<VisualElement>(root, "host-mode-diagram");
			leftHeadset = Require<VisualElement>(root, "host-mode-headset-left");
			rightHeadset = Require<VisualElement>(root, "host-mode-headset-right");
			diagram.generateVisualContent += DrawPaths;
			leftHeadset.RegisterCallback<GeometryChangedEvent>(_ => diagram.MarkDirtyRepaint());
			about = Require<Label>(root, "host-mode-about");
			hostButton = Require<Button>(root, "host-button");

			AddCard(root, bindings, Protocol.Direct, "host-mode-direct");
			AddCard(root, bindings, Protocol.LAN, "host-mode-lan");
			AddCard(root, bindings, Protocol.UnityService, "host-mode-relay");

			if (!directSupported)
			{
				cards[Protocol.Direct].style.display = DisplayStyle.None;
				cards.Remove(Protocol.Direct);
			}
		}

		private void AddCard(VisualElement root, UIEventBindings bindings, Protocol mode, string name)
		{
			Button card = Require<Button>(root, name);
			cards[mode] = card;
			bindings.Click(card, () => OnCardClicked(mode));
		}

		public void Refresh(bool wifiOn, bool hasLocalNetwork, bool hasFullInternet, bool bluetoothOn)
		{
			unusableReasonKeys.Clear();
			if (!wifiOn) unusableReasonKeys[Protocol.Direct] = WifiOffReasonKey;
			else if (!bluetoothOn) unusableReasonKeys[Protocol.Direct] = BluetoothOffReasonKey;
			if (!hasLocalNetwork) unusableReasonKeys[Protocol.LAN] = wifiOn ? "host-mode.blocked.wifi" : WifiOffReasonKey;
			if (!hasFullInternet) unusableReasonKeys[Protocol.UnityService] = "host-mode.blocked.internet";

			SelectedMode = null;
			Protocol preferred = Enum.IsDefined(typeof(Protocol), savedMode.Value)
				? (Protocol)savedMode.Value
				: Protocol.LAN;
			if (IsUsable(preferred))
				SelectedMode = preferred;
			else
				foreach (Protocol fallback in FallbackOrder)
					if (IsUsable(fallback))
					{
						SelectedMode = fallback;
						break;
					}

			foreach ((Protocol mode, Button card) in cards)
			{
				bool usable = !unusableReasonKeys.TryGetValue(mode, out string reasonKey);
				card.EnableInClassList("option--selected", mode == SelectedMode);
				card.EnableInClassList("host-mode-tile--unusable", !usable);
				card.Q<Label>(className: "host-mode-tile__name").text = Copy($"host-mode.{Key(mode)}.name");
				card.Q<Label>(className: "host-mode-tile__status").text = Copy($"host-mode.{Key(mode)}.tagline");
			}

			diagram.MarkDirtyRepaint();

			about.text = Copy(SelectedMode is { } selected ? $"host-mode.{Key(selected)}.about" : "host-mode.none");
			hostButton.SetEnabled(SelectedMode != null);
		}

		private bool IsUsable(Protocol mode) => cards.ContainsKey(mode) && !unusableReasonKeys.ContainsKey(mode);

		private void OnCardClicked(Protocol mode)
		{
			if (IsUsable(mode))
			{
				savedMode.Value = (int)mode;
				return;
			}

			string reasonKey = unusableReasonKeys[mode];
			showAlert(new MenuError(MenuErrorArea.Connection, reasonKey, reasonKey + ".details", Table));
		}

		private void DrawPaths(MeshGenerationContext context)
		{
			if (SelectedMode is not { } selected || !cards.TryGetValue(selected, out Button card)) return;

			Rect left = leftHeadset.layout;
			Rect right = rightHeadset.layout;
			float tileY = card.layout.center.y;

			Vector2[] path;
			if (selected == Protocol.UnityService)
			{
				float pathEndY = left.yMin - GapBetweenRelayPathAndPerson;
				float cut = Mathf.Min(RelayPathCornerCut, pathEndY - tileY);
				path = new[]
				{
					new Vector2(left.center.x, pathEndY), new Vector2(left.center.x, tileY + cut),
					new Vector2(left.center.x + cut, tileY), new Vector2(right.center.x - cut, tileY),
					new Vector2(right.center.x, tileY + cut), new Vector2(right.center.x, pathEndY)
				};
			}
			else
			{
				// climbs at 45 degrees to the tile's row, so the lowest tile's path is one straight line
				float personY = left.center.y;
				float climb = personY - tileY;
				path = new[]
				{
					new Vector2(left.xMax, personY), new Vector2(left.xMax + climb, tileY),
					new Vector2(right.xMin - climb, tileY), new Vector2(right.xMin, personY)
				};
			}

			Painter2D painter = context.painter2D;
			painter.strokeColor = Color.white;
			painter.lineWidth = PathWidth;
			painter.lineJoin = LineJoin.Miter;
			painter.BeginPath();
			painter.MoveTo(path[0]);
			for (int i = 1; i < path.Length; i++)
				if (path[i] != path[i - 1])
					painter.LineTo(path[i]);
			painter.Stroke();
		}

		private static string Key(Protocol mode) => mode switch
		{
			Protocol.Direct => "direct",
			Protocol.UnityService => "relay",
			_ => "lan"
		};

		private static string Copy(string key) => MenuCopy.Get(Table, key);
	}
}
