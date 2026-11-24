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
	/// joins the two headsets through each tile. A tile the device's network can't use is outlined, and
	/// tapping it raises an alert saying why. The saved choice is kept while unusable, and hosting falls
	/// back to the next usable mode.
	/// </summary>
	internal sealed class HostModePicker
	{
		private const string Table = "ConnectionMenu";
		private const string WifiOffReasonKey = "host-mode.blocked.wifi-off";
		private const string BluetoothOffReasonKey = "host-mode.blocked.bluetooth";
		private static readonly Protocol[] FallbackOrder = { Protocol.LAN, Protocol.Direct, Protocol.UnityService };
		private static readonly Protocol[] ShortestPathFirst = { Protocol.Direct, Protocol.LAN, Protocol.UnityService };
		private const float SelectedPathWidth = 6;
		private const float UnselectedPathWidth = 2;
		private const float DashLength = 5;
		private const float PathCornerCut = 20;

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
			Painter2D painter = context.painter2D;
			painter.lineJoin = LineJoin.Miter;
			Rect left = leftHeadset.layout;
			Rect right = rightHeadset.layout;

			foreach (bool drawingSelected in new[] { false, true })
			{
				int pathsBelow = 0;
				foreach (Protocol mode in ShortestPathFirst)
				{
					if (!cards.TryGetValue(mode, out Button card)) continue;
					int stepsUp = pathsBelow++;
					if ((mode == SelectedMode) != drawingSelected) continue;

					float y = card.layout.center.y;
					Vector2[] path;
					if (stepsUp == 0)
						path = new[] { new Vector2(left.xMax, y), new Vector2(right.xMin, y) };
					else
					{
						// higher paths leave the headsets further out, so they never share a line
						float inset = left.width * (cards.Count - stepsUp) / cards.Count;
						float leftX = left.xMin + inset;
						float rightX = right.xMax - inset;
						float cut = Mathf.Min(PathCornerCut, left.yMin - y);
						path = new[]
						{
							new Vector2(leftX, left.yMin), new Vector2(leftX, y + cut), new Vector2(leftX + cut, y),
							new Vector2(rightX - cut, y), new Vector2(rightX, y + cut), new Vector2(rightX, right.yMin)
						};
					}

					painter.strokeColor = drawingSelected ? Color.white : diagram.resolvedStyle.color;
					painter.lineWidth = drawingSelected ? SelectedPathWidth : UnselectedPathWidth;
					painter.BeginPath();
					if (drawingSelected) TraceSolid(painter, path);
					else TraceDashed(painter, path);
					painter.Stroke();
				}
			}
		}

		private static void TraceSolid(Painter2D painter, Vector2[] path)
		{
			painter.MoveTo(path[0]);
			for (int i = 1; i < path.Length; i++)
				painter.LineTo(path[i]);
		}

		private static void TraceDashed(Painter2D painter, Vector2[] path)
		{
			bool penDown = true;
			float untilPenToggles = DashLength;
			for (int i = 0; i < path.Length - 1; i++)
			{
				Vector2 from = path[i];
				Vector2 to = path[i + 1];
				float length = Vector2.Distance(from, to);
				Vector2 direction = (to - from) / length;
				float travelled = 0;

				if (penDown) painter.MoveTo(from);
				while (travelled + untilPenToggles < length)
				{
					travelled += untilPenToggles;
					Vector2 point = from + direction * travelled;
					if (penDown) painter.LineTo(point);
					else painter.MoveTo(point);
					penDown = !penDown;
					untilPenToggles = DashLength;
				}

				untilPenToggles -= length - travelled;
				if (penDown) painter.LineTo(to);
			}
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
