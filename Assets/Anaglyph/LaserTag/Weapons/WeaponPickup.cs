using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class WeaponPickup : MonoBehaviour
	{
		public GameObject weaponPrefab;

		public const string Tag = "Weapon Pickup";

		private void Awake()
		{
			gameObject.tag = Tag;
		}
	}
}