using System;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public class WeaponPickup : MonoBehaviour
	{
		public GameObject weaponPrefab;

		[Tooltip("Touching only highlights it; trigger or grip then takes it.")]
		public bool selectOnPress;

		[Tooltip("The displayed weapon, which the held one flies in from. Optional.")]
		public Transform shownWeapon;

		public Pose? ShownWeaponPose => shownWeapon != null
			? new Pose(shownWeapon.position, shownWeapon.rotation)
			: null;

		public const string Tag = "Weapon Pickup";

		private int hoveringHandCount;

		/// <summary>True while at least one hand touches it.</summary>
		public event Action<bool> HoveredChanged = delegate { };

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

		public void AddHoveringHand()
		{
			if (++hoveringHandCount == 1)
				HoveredChanged.Invoke(true);
		}

		public void RemoveHoveringHand()
		{
			if (hoveringHandCount == 0)
				return;

			if (--hoveringHandCount == 0)
				HoveredChanged.Invoke(false);
		}
	}
}
