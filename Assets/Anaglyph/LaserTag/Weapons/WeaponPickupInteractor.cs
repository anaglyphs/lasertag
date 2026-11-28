using Anaglyph.XR.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.LaserTag.Weapons
{
	public class WeaponPickupInteractor : MonoBehaviour
	{
		[SerializeField] private HandSubject handSubject;

		private const string TriggerAction = "OnFire";
		private const string GripAction = "OnGrip";

		private WeaponPickup hoveredPickup;

		private void OnEnable()
		{
			handSubject.Bind(TriggerAction, OnSelectInput);
			handSubject.Bind(GripAction, OnSelectInput);
		}

		private void OnDisable()
		{
			handSubject.Unbind(TriggerAction, OnSelectInput);
			handSubject.Unbind(GripAction, OnSelectInput);
			SetHovered(null);
		}

		private void OnSelectInput(InputAction.CallbackContext context)
		{
			if (!context.performed || hoveredPickup == null)
				return;

			if (!hoveredPickup.isActiveAndEnabled)
			{
				SetHovered(null);
				return;
			}

			WeaponSwitcher switcher = WeaponSwitcher.Instance;
			if (switcher == null || !switcher.WeaponsActive)
				return;

			switcher.SwitchWeapon(hoveredPickup.weaponPrefab, handSubject.Current.Handedness, hoveredPickup.ShownWeaponPose);
		}

		private void OnTriggerEnter(Collider other)
		{
			if (!other.CompareTag(WeaponPickup.Tag))
				return;

			if (!other.TryGetComponent(out WeaponPickup pickup))
				return;

			if (pickup.selectOnPress)
				SetHovered(pickup);
			else
				WeaponSwitcher.Instance.SwitchWeapon(pickup.weaponPrefab, handSubject.Current.Handedness, pickup.ShownWeaponPose);
		}

		private void OnTriggerExit(Collider other)
		{
			if (hoveredPickup != null && other.gameObject == hoveredPickup.gameObject)
				SetHovered(null);
		}

		private void SetHovered(WeaponPickup pickup)
		{
			if (hoveredPickup == pickup)
				return;

			if (hoveredPickup != null)
				hoveredPickup.RemoveHoveringHand();

			hoveredPickup = pickup;

			if (hoveredPickup != null)
				hoveredPickup.AddHoveringHand();
		}
	}
}
