using Anaglyph.Lasertag.Weapons;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class WeaponPickup : MonoBehaviour
	{
		public GameObject weaponPrefab;

		[SerializeField] private Transform viewHolder;
		[SerializeField] private float spinSpeed = 30;
		[SerializeField] private float bobHeight = 0.05f;
		[SerializeField] private float bobFrequency = 0.5f;

		public const string Tag = "Weapon Pickup";

		private Vector3 viewHolderBasePosition;
		private float animationTime;

		private void Awake()
		{
			gameObject.tag = Tag;

			viewHolderBasePosition = viewHolder.localPosition;

			if (weaponPrefab == null)
				return;

			WeaponView weaponView = weaponPrefab.GetComponentInChildren<WeaponView>(true);

			if (weaponView == null)
			{
				Debug.LogError($"{weaponPrefab.name} does not contain a {nameof(WeaponView)}.", this);
				return;
			}

			Instantiate(weaponView.gameObject, viewHolder, false);
		}

		private void Update()
		{
			animationTime += Time.deltaTime;

			viewHolder.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);

			float bobOffset = Mathf.Sin(animationTime * bobFrequency * Mathf.PI * 2) * bobHeight;
			viewHolder.localPosition = viewHolderBasePosition + Vector3.up * bobOffset;
		}
	}
}
