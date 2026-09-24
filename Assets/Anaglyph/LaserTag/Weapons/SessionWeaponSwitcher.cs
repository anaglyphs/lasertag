using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public class SessionWeaponSwitcher : MonoBehaviour
	{
		public static SessionWeaponSwitcher Instance { get; private set; }

		[SerializeField] private WeaponDatabase database;
		public WeaponDatabase Database => database;

		private readonly SyncEvent<int> switchEveryone = new("weapons.switch-everyone", EventRoute.ViaAuthority);

		private void Awake()
		{
			Instance = this;
			switchEveryone.Validate = (_, id) => database != null && database.GetWeapon(id) != null;
			switchEveryone.Received += OnSwitchEveryone;
			switchEveryone.Register();
		}

		private void OnDestroy()
		{
			switchEveryone.Received -= OnSwitchEveryone;
			switchEveryone.Unregister();
			if (Instance == this) Instance = null;
		}

		public void SwitchEveryone(int weaponId)
		{
			if (SyncBus.Active) switchEveryone.Raise(weaponId);
		}

		private void OnSwitchEveryone(ulong sender, int weaponId)
		{
			GameObject weapon = database.GetWeapon(weaponId);
			if (weapon == null || WeaponSwitcher.Instance == null) return;
			WeaponSwitcher.Instance.SwitchWeapon(weapon, Handedness.Left);
			WeaponSwitcher.Instance.SwitchWeapon(weapon, Handedness.Right);
		}
	}
}
