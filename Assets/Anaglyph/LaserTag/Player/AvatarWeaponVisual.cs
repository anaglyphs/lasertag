using Anaglyph.Input;
using Anaglyph.Lasertag.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag.Networking
{
	/// <summary>
	/// One hand's weapon. Every player spawns the same weapon into the same hand from the
	/// same synced id - the owner gets the working weapon, everyone else gets its visuals.
	/// A hand only exists to fire from once the avatar does. Bolts are networked in their
	/// own right; only the presentation travels through this.
	/// </summary>
	public class AvatarWeaponVisual : NetworkBehaviour
	{
		[SerializeField] private Handedness handedness;
		[SerializeField] private WeaponDatabase database;

		private NetworkVariable<int> weaponIdSync = new(WeaponDatabase.NoWeapon);

		// hidden while the hand is untracked or weapons are put away
		private NetworkVariable<bool> shownSync = new();
		private NetworkVariable<bool> firingSync = new();

		private GameObject instance;
		private WeaponVisual visual;
		private int id = WeaponDatabase.NoWeapon;

		// owner only
		private GameObject selectedPrefab;
		private bool weaponsActive = true;

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
				return;

			weaponIdSync.OnValueChanged += OnWeaponIdChanged;
			shownSync.OnValueChanged += OnShownChanged;
			firingSync.OnValueChanged += OnFiringChanged;

			Show(weaponIdSync.Value);
		}

		public override void OnNetworkDespawn()
		{
			if (IsOwner)
			{
				Show(WeaponDatabase.NoWeapon);
				return;
			}

			weaponIdSync.OnValueChanged -= OnWeaponIdChanged;
			shownSync.OnValueChanged -= OnShownChanged;
			firingSync.OnValueChanged -= OnFiringChanged;
		}

		private void Update()
		{
			if (!IsSpawned || !IsOwner)
				return;

			WeaponSwitcher switcher = WeaponSwitcher.Instance;

			if (switcher == null)
				return;

			GameObject prefab = switcher.GetSelected(handedness);

			if (prefab != selectedPrefab)
			{
				selectedPrefab = prefab;
				Show(database.IndexOf(prefab));
			}

			// only on change - DeactivateUntracked owns this flag the rest of the time
			if (switcher.WeaponsActive != weaponsActive)
			{
				weaponsActive = switcher.WeaponsActive;

				if (instance != null)
					instance.SetActive(weaponsActive);
			}

			weaponIdSync.Value = id;
			// the visual, not the weapon - that is the object everyone else instantiates,
			// and it also goes away on its own when a peripheral replaces it
			shownSync.Value = visual != null && visual.gameObject.activeInHierarchy;
			firingSync.Value = visual != null && visual.IsFiring;
		}

		private void Show(int weaponId)
		{
			if (weaponId == id)
				return;

			id = weaponId;

			if (visual != null)
				visual.Fired -= OnFired;

			if (instance != null)
				Destroy(instance);

			instance = null;
			visual = null;

			GameObject prefab = IsOwner ? database.GetWeapon(id) : database.GetView(id);

			if (prefab == null)
				return;

			instance = Instantiate(prefab, transform, false);
			instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
			visual = instance.GetComponentInChildren<WeaponVisual>(true);

			if (!IsOwner)
			{
				ApplySyncedState();
				return;
			}

			if (instance.TryGetComponent(out HandSubject handSubject))
				handSubject.Assign(HandInput.Get(handedness));

			instance.SetActive(weaponsActive);

			if (visual != null)
				visual.Fired += OnFired;
		}

		// what the owner's own weapon does for itself, applied to everyone else's copy
		private void ApplySyncedState()
		{
			if (instance == null)
				return;

			instance.SetActive(shownSync.Value);

			if (visual != null)
				visual.SetFiring(firingSync.Value);
		}

		private void OnWeaponIdChanged(int previous, int current) => Show(current);
		private void OnShownChanged(bool previous, bool current) => ApplySyncedState();
		private void OnFiringChanged(bool previous, bool current) => ApplySyncedState();

		private void OnFired()
		{
			PlayFireRpc();
		}

		// Cosmetic one-shot - a dropped muzzle flash beats holding up the stream for it.
		[Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
		private void PlayFireRpc()
		{
			if (visual != null && visual.isActiveAndEnabled)
				visual.PlayFire();
		}
	}
}
