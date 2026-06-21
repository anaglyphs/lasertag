using System.Collections.Generic;
using Anaglyph.Input;
using Anaglyph.Netcode;
using Oculus.Haptics;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
	public class WeaponSwitcher : MonoBehaviour
	{
		[SerializeField] private GameObject defaultWeapon;

		private readonly Dictionary<Handedness, GameObject> weapons = new();

		private void Awake()
		{
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

					InstantiateSelected(defaultWeapon, Handedness.Left);
					InstantiateSelected(defaultWeapon, Handedness.Right);
					break;

				case NetcodeState.Disconnected:
					foreach (KeyValuePair<Handedness, GameObject> pair in weapons) Destroy(pair.Value);
					weapons.Clear();

					break;
			}
		}

		private void InstantiateSelected(GameObject prefab, Handedness handedness)
		{
			if (NetcodeManagement.State != NetcodeState.Connected) return;

			if (weapons.TryGetValue(handedness, out GameObject weaponObj))
				Destroy(weaponObj);

			weaponObj = Instantiate(prefab, transform);


			if (weaponObj.TryGetComponent(out HandSubject handSubject)) handSubject.Assign(HandInput.Get(handedness));

			weapons[handedness] = weaponObj;
		}
	}
}