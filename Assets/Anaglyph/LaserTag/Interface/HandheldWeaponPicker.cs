using System.Collections.Generic;
using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Weapons;
using Anaglyph.Menu;
using Anaglyph.XR.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// A phone's weapon rack: while the player stands in their own base, every weapon is a tile in one
	/// sideways-scrolling row along the bottom of the screen, dressed as the map editor's touch palette.
	/// A tap arms both hands with it, as the phone fires from either.
	/// </summary>
	public sealed class HandheldWeaponPicker
	{
		private static readonly Handedness[] BothHands = { Handedness.Left, Handedness.Right };

		private readonly VisualElement root;
		private readonly ScrollView grid;
		private readonly Dictionary<Button, GameObject> weaponByTile = new();

		public HandheldWeaponPicker(VisualElement parent, IEnumerable<StyleSheet> paletteStyleSheets)
		{
			root = new VisualElement { name = "handheld-weapon-picker", pickingMode = PickingMode.Ignore };
			root.AddToClassList(HandheldScreenUI.TouchScreenUssClassName);
			root.style.position = Position.Absolute;
			foreach (StyleSheet sheet in paletteStyleSheets)
				root.styleSheets.Add(sheet);
			parent.Add(root);

			VisualElement palette = new() { pickingMode = PickingMode.Ignore };
			palette.AddToClassList("menu-root");
			palette.AddToClassList("game-panel");
			palette.AddToClassList("palette-root");
			root.Add(palette);

			grid = new StyleDirectedScrollView { name = "weapon-grid", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
			grid.AddToClassList("palette-grid");
			palette.Add(grid);

			Display(false);
		}

		// the session's database can arrive after the screen is built
		private void AddTilesOnce()
		{
			if (weaponByTile.Count > 0) return;

			WeaponDatabase database = SessionWeaponSwitcher.Instance ? SessionWeaponSwitcher.Instance.Database : null;
			for (int id = 0; database != null && id < database.Count; id++)
			{
				GameObject weapon = database.GetWeapon(id);
				if (weapon == null) continue;

				Button tile = new(() => Arm(weapon));
				tile.MakeActOnPress();
				tile.AddToClassList("palette-item");
				Label label = new(weapon.name);
				label.AddToClassList("palette-item-label");
				tile.Add(label);
				grid.Add(tile);
				weaponByTile.Add(tile, weapon);
			}
		}

		private static bool IsAtOwnAvailableWeaponRack
		{
			get
			{
				PlayerAvatar local = PlayerAvatar.Local;
				return local != null && local.IsInFriendlyBase && local.OccupiedBase != null && local.OccupiedBase.WeaponRackAvailable;
			}
		}

		/// <summary>Shows it while the player is in their own base and nothing else holds the screen.</summary>
		public void Refresh(bool screenFree)
		{
			AddTilesOnce();
			WeaponSwitcher switcher = WeaponSwitcher.Instance;
			bool shown = screenFree && switcher != null && switcher.WeaponsActive && IsAtOwnAvailableWeaponRack && weaponByTile.Count > 0;
			Display(shown);
			if (!shown) return;

			GameObject held = switcher.GetSelected(Handedness.Right);
			foreach ((Button tile, GameObject weapon) in weaponByTile)
				tile.EnableInClassList("selected", weapon == held);
		}

		/// <summary>As tall as its tiles, along the screen's bottom between the insets.</summary>
		public void Layout(float left, float right, float bottom)
		{
			root.style.left = left;
			root.style.right = right;
			root.style.bottom = bottom;
		}

		private static void Arm(GameObject weapon)
		{
			WeaponSwitcher switcher = WeaponSwitcher.Instance;
			if (switcher == null) return;
			foreach (Handedness hand in BothHands)
				switcher.SwitchWeapon(weapon, hand);
		}

		private void Display(bool shown) => root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
	}
}
