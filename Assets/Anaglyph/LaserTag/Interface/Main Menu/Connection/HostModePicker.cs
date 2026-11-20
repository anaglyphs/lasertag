using System;
using System.Collections.Generic;
using Anaglyph.Menu;
using Anaglyph.VariableObjects;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;
using Protocol = Anaglyph.Netcode.NetcodeManagement.Protocol;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// Three cards choosing how this headset hosts. A card the device's network can't use is greyed and
	/// says why; tapping it opens the settings that fix it. The saved choice is kept while unusable,
	/// and hosting falls back to the next usable mode.
	/// </summary>
	internal sealed class HostModePicker
	{
		private const string Table = "ConnectionMenu";
		private static readonly Protocol[] FallbackOrder = { Protocol.LAN, Protocol.Direct, Protocol.UnityService };

		private readonly IntObject savedMode;
		private readonly Label about;
		private readonly Button hostButton;
		private readonly Dictionary<Protocol, Button> cards = new();
		private readonly Dictionary<Protocol, string> unusableReasonKeys = new();

		public Protocol? SelectedMode { get; private set; }

		public HostModePicker(VisualElement root, UIEventBindings bindings, IntObject savedMode, bool directSupported)
		{
			this.savedMode = savedMode;
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

		public void Refresh(bool hasLocalNetwork, bool hasFullInternet, bool bluetoothOn)
		{
			unusableReasonKeys.Clear();
			if (!bluetoothOn) unusableReasonKeys[Protocol.Direct] = "host-mode.blocked.bluetooth";
			if (!hasLocalNetwork) unusableReasonKeys[Protocol.LAN] = "host-mode.blocked.wifi";
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
				card.EnableInClassList("host-mode-card--unusable", !usable);
				card.Q<Label>(className: "host-mode-card__name").text = Copy($"host-mode.{Key(mode)}.name");
				card.Q<Label>(className: "host-mode-card__status").text =
					Copy(usable ? $"host-mode.{Key(mode)}.tagline" : reasonKey);
			}

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

			if (mode == Protocol.Direct)
				ConnectionConnectivityMonitor.OpenBluetoothSettings();
			else
				ConnectionConnectivityMonitor.OpenWifiSettings();
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
