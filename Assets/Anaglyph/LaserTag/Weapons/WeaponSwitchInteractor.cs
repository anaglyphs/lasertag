using Anaglyph.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag
{
	public class WeaponSwitchInteractor : MonoBehaviour
	{
		[SerializeField] private HandSubject handSubject;

		private WeaponPickup intersectingPickup;

		private void OnEnable()
		{
			handSubject.Bind(nameof(OnGrip), OnGrip);
		}

		private void OnDisable()
		{
			handSubject.Unbind(nameof(OnGrip), OnGrip);
		}

		private void OnGrip(InputAction.CallbackContext obj)
		{
			if (intersectingPickup == null)
				return;

			WeaponSwitcher.Instance.SwitchWeapon(intersectingPickup.weaponPrefab, handSubject.Current.Handedness);
		}

		private void OnTriggerEnter(Collider other)
		{
			if (!other.CompareTag(WeaponPickup.Tag))
				return;

			if (!other.TryGetComponent(out WeaponPickup potentialPickup))
				return;

			intersectingPickup = potentialPickup;
		}

		private void OnTriggerExit(Collider other)
		{
			if (!other.CompareTag(WeaponPickup.Tag))
				return;

			if (!other.TryGetComponent(out WeaponPickup potentialPickup))
				return;

			if (potentialPickup == intersectingPickup) intersectingPickup = null;
		}
	}
}