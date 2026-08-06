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

		/// <summary>The visuals-only part of a weapon, for showing in someone else's hand.</summary>
		public WeaponView GetViewPrefab(int id)
		{
			if (id < 0 || id >= weapons.Count)
				return null;

			GameObject weapon = weapons[id];

			if (weapon == null)
				return null;

			return weapon.GetComponentInChildren<WeaponView>(true);
		}
	}
}
