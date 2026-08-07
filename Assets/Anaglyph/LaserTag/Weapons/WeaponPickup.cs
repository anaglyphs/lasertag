using Anaglyph.Lasertag.Weapons;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class WeaponPickup : MonoBehaviour
	{
		public GameObject weaponPrefab;

		public const string Tag = "Weapon Pickup";

		private Vector3 viewHolderBasePosition;
		private float animationTime;

		private void Awake()
		{
			gameObject.tag = Tag;

			if (weaponPrefab == null)
				return;

			WeaponVisual weaponVisual = weaponPrefab.GetComponentInChildren<WeaponVisual>(true);

			if (weaponVisual == null)
			{
				Debug.LogError($"{weaponPrefab.name} does not contain a {nameof(WeaponVisual)}.", this);
				return;
			}
		}
	}
}
