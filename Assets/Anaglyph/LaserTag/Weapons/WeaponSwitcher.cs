using System.Collections.Generic;
using Anaglyph.Input;
using Anaglyph.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class WeaponSwitcher : MonoBehaviour
	{
		public static WeaponSwitcher Instance { get; private set; }

		[SerializeField] private GameObject defaultWeapon;

		private readonly Dictionary<Handedness, GameObject> weapons = new();

		private bool weaponsActive = true;

		private void Awake()
		{
			Instance = this;

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			switch (state)
			{
				case NetcodeState.Connected:

					SwitchWeapon(defaultWeapon, Handedness.Left);
					SwitchWeapon(defaultWeapon, Handedness.Right);
					break;

				case NetcodeState.Disconnected:
					foreach (KeyValuePair<Handedness, GameObject> pair in weapons) Destroy(pair.Value);
					weapons.Clear();

					break;
			}
		}

		public void SetWeaponsActive(bool b)
		{
			weaponsActive = b;

			foreach (GameObject weaponObj in weapons.Values)
				weaponObj.SetActive(weaponsActive);
		}

		public void SwitchWeapon(GameObject prefab, Handedness handedness)
		{
			if (NetcodeManagement.State != NetcodeState.Connected) return;

			if (weapons.TryGetValue(handedness, out GameObject weaponObj))
				Destroy(weaponObj);

			weaponObj = Instantiate(prefab, transform);

			if (weaponObj.TryGetComponent(out HandSubject handSubject)) handSubject.Assign(HandInput.Get(handedness));

			weapons[handedness] = weaponObj;

			weaponObj.SetActive(weaponsActive);
		}
	}
}