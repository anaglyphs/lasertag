using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public class WeaponPickupInteractor : MonoBehaviour
	{
		[SerializeField] private HandSubject handSubject;

		// private WeaponPickup intersectingPickup;

		// private void OnEnable()
		// {
		// 	handSubject.Bind(nameof(OnGrip), OnGrip);
		// }
		//
		// private void OnDisable()
		// {
		// 	handSubject.Unbind(nameof(OnGrip), OnGrip);
		// }

		// private void OnGrip(InputAction.CallbackContext obj)
		// {
		// 	if (intersectingPickup == null)
		// 		return;
		//
		// 	WeaponSwitcher.Instance.SwitchWeapon(intersectingPickup.weaponPrefab, handSubject.Current.Handedness);
		// }

		private void OnTriggerEnter(Collider other)
		{
			if (!other.CompareTag(WeaponPickup.Tag))
				return;

			if (!other.TryGetComponent(out WeaponPickup pickup))
				return;

			// intersectingPickup = pickup;
			
			WeaponSwitcher.Instance.SwitchWeapon(pickup.weaponPrefab, handSubject.Current.Handedness);
		}
		//
		// private void OnTriggerExit(Collider other)
		// {
		// 	if (!other.CompareTag(WeaponPickup.Tag))
		// 		return;
		//
		// 	if (!other.TryGetComponent(out WeaponPickup potentialPickup))
		// 		return;
		//
		// 	if (potentialPickup == intersectingPickup) intersectingPickup = null;
		// }
	}
}