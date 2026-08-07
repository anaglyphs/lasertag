using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.Lasertag.Weapons
{
	/// <summary>
	/// Every weapon players can hold. A weapon's id is its index here, and that id is
	/// what avatars sync to show each other what they're holding.
	/// </summary>
	[CreateAssetMenu(fileName = "Weapon Database", menuName = "Lasertag/Weapon Database")]
	public class WeaponDatabase : ScriptableObject
	{
		public const int NoWeapon = -1;

		[SerializeField] private List<GameObject> weapons = new();

		public int IndexOf(GameObject weaponPrefab)
		{
			if (weaponPrefab == null)
				return NoWeapon;

			int index = weapons.IndexOf(weaponPrefab);

			if (index < 0)
				Debug.LogError($"'{weaponPrefab.name}' is missing from '{name}'. " +
					"Other players cannot see a weapon that isn't listed here.", this);

			return index;
		}

		/// <summary>The whole weapon, for the player actually holding it.</summary>
		public GameObject GetWeapon(int id) =>
			id < 0 || id >= weapons.Count ? null : weapons[id];

		/// <summary>The visuals-only part of it, for showing in someone else's hand.</summary>
		public GameObject GetView(int id)
		{
			GameObject weapon = GetWeapon(id);

			if (weapon == null)
				return null;

			WeaponVisual visual = weapon.GetComponentInChildren<WeaponVisual>(true);

			return visual == null ? null : visual.gameObject;
		}
	}
}
