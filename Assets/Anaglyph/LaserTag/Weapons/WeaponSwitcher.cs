using System.Collections.Generic;
using Anaglyph.Input;
using Anaglyph.Lasertag.Weapons;
using Anaglyph.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class WeaponSwitcher : MonoBehaviour
	{
		public static WeaponSwitcher Instance { get; private set; }

		[SerializeField] private GameObject defaultWeapon;

		private readonly Dictionary<Handedness, Held> weapons = new();

		private bool weaponsActive = true;

		// The prefab is kept alongside the instance because that is what identifies the
		// weapon to other players; the view is what they mirror.
		private class Held
		{
			public GameObject prefab;
			public GameObject instance;
			public WeaponView view;
		}

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
					foreach (KeyValuePair<Handedness, Held> pair in weapons) Destroy(pair.Value.instance);
					weapons.Clear();

					break;
			}
		}

		public GameObject GetHeldPrefab(Handedness handedness) =>
			weapons.TryGetValue(handedness, out Held held) ? held.prefab : null;

		public WeaponView GetHeldView(Handedness handedness) =>
			weapons.TryGetValue(handedness, out Held held) ? held.view : null;

		/// <summary>False while the weapon is hidden - untracked hand, or weapons switched off.</summary>
		public bool IsHeldWeaponShown(Handedness handedness) =>
			weapons.TryGetValue(handedness, out Held held) &&
			held.instance != null && held.instance.activeInHierarchy;

		public void SetWeaponsActive(bool b)
		{
			weaponsActive = b;

			foreach (Held held in weapons.Values)
				held.instance.SetActive(weaponsActive);
		}

		public void SwitchWeapon(GameObject prefab, Handedness handedness)
		{
			if (NetcodeManagement.State != NetcodeState.Connected) return;

			if (!weapons.TryGetValue(handedness, out Held held))
				weapons[handedness] = held = new Held();
			else
				Destroy(held.instance);

			GameObject weaponObj = Instantiate(prefab, transform);

			if (weaponObj.TryGetComponent(out HandSubject handSubject)) handSubject.Assign(HandInput.Get(handedness));

			weaponObj.SetActive(weaponsActive);

			held.prefab = prefab;
			held.instance = weaponObj;
			held.view = weaponObj.GetComponentInChildren<WeaponView>(true);
		}
	}
}
