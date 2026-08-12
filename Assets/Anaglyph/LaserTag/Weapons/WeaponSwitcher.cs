using System.Collections.Generic;
using Anaglyph.Netcode;
using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	/// <summary>
	/// Which weapon each hand has chosen. The avatar is what actually holds one, so a player
	/// without an avatar has nothing to fire.
	/// </summary>
	public class WeaponSwitcher : MonoBehaviour
	{
		public static WeaponSwitcher Instance { get; private set; }

		[SerializeField] private GameObject defaultWeapon;

		private readonly Dictionary<Handedness, GameObject> selected = new();

		/// <summary>False while weapons are put away, e.g. for the map editor.</summary>
		public bool WeaponsActive { get; private set; } = true;

		private void Awake()
		{
			Instance = this;

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;

			if (Instance == this)
				Instance = null;
		}

		// a weapon picked up in one session shouldn't follow the player into the next
		private void OnNetcodeStateChanged(NetcodeState state)
		{
			if (state == NetcodeState.Disconnected)
				selected.Clear();
		}

		public GameObject GetSelected(Handedness handedness) =>
			selected.TryGetValue(handedness, out GameObject prefab) && prefab != null
				? prefab
				: defaultWeapon;

		public void SwitchWeapon(GameObject prefab, Handedness handedness)
		{
			selected[handedness] = prefab;
		}

		public void SetWeaponsActive(bool active)
		{
			WeaponsActive = active;
		}
	}
}
