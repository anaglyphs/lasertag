using Anaglyph.LaserTag.Weapons;
using Anaglyph.XR.Input;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Player
{
	/// <summary>
	/// One hand's weapon. Every player spawns the same weapon into the same hand from the
	/// same synced id - the owner gets the working weapon, everyone else gets its visuals.
	/// Weapons are present only while the player can interact. Bolts are networked in their
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
		private GameObject presentationRoot;
		private PlayerAvatar avatar;
		private WeaponVisual visual;
		private WandSweepTrail sweepTrail;
		private int id = WeaponDatabase.NoWeapon;
		public GameObject EquippedWeapon => database.GetWeapon(id);

		// owner only
		private GameObject selectedPrefab;
		private bool weaponsActive = true;

		private void Awake()
		{
			avatar = GetComponentInParent<PlayerAvatar>(true);
			// Tracking controls the weapon instance itself. A separate parent lets presence
			// hide it without a tracking callback accidentally making it active again.
			presentationRoot = new GameObject("Weapon Presentation");
			presentationRoot.transform.SetParent(transform, false);
			presentationRoot.SetActive(false);
		}

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
			presentationRoot.SetActive(false);
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
			if (!IsSpawned) return;
			if (!IsOwner) { ApplySyncedState(); return; }

			WeaponSwitcher switcher = WeaponSwitcher.Instance;

			if (switcher == null) { presentationRoot.SetActive(false); return; }

			GameObject prefab = switcher.GetSelected(handedness);

			if (prefab != selectedPrefab)
			{
				selectedPrefab = prefab;
				Show(database.IndexOf(prefab));
			}

			weaponsActive = switcher.WeaponsActive;
			presentationRoot.SetActive(avatar.CanInteract && weaponsActive);

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
			if (sweepTrail != null)
				sweepTrail.Emitted -= OnSweepEmitted;

			if (instance != null)
				Destroy(instance);

			instance = null;
			visual = null;
			sweepTrail = null;

			GameObject prefab = IsOwner ? database.GetWeapon(id) : database.GetView(id);

			if (prefab == null)
				return;

			presentationRoot.SetActive(false);
			instance = Instantiate(prefab, presentationRoot.transform, false);
			instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
			visual = instance.GetComponentInChildren<WeaponVisual>(true);
			sweepTrail = instance.GetComponentInChildren<WandSweepTrail>(true);
			if (sweepTrail != null)
			{
				sweepTrail.OwnerClientId = OwnerClientId;
				if (IsOwner) sweepTrail.Emitted += OnSweepEmitted;
			}

			if (!IsOwner)
			{
				ApplySyncedState();
				return;
			}

			if (instance.TryGetComponent(out HandSubject handSubject))
				handSubject.Assign(HandInput.Get(handedness));

			presentationRoot.SetActive(avatar.CanInteract && weaponsActive);

			if (visual != null)
				visual.Fired += OnFired;
		}

		// what the owner's own weapon does for itself, applied to everyone else's copy
		private void ApplySyncedState()
		{
			presentationRoot.SetActive(IsSpawned && avatar.CanInteract && shownSync.Value);
			if (instance == null)
				return;

			if (visual != null)
				visual.SetFiring(avatar.CanInteract && firingSync.Value);
		}

		private void OnWeaponIdChanged(int previous, int current) => Show(current);
		private void OnShownChanged(bool previous, bool current) => ApplySyncedState();
		private void OnFiringChanged(bool previous, bool current) => ApplySyncedState();

		private void OnFired()
		{
			PlayFireRpc();
		}

		private void OnSweepEmitted(WandSweepTrail.Segment segment)
		{
			if (IsSpawned) SweepRpc(id, segment);
		}

		[Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
		private void SweepRpc(int weaponId, WandSweepTrail.Segment segment)
		{
			if (weaponId == id && avatar.CanInteract && sweepTrail != null)
				sweepTrail.Add(segment);
		}

		// Cosmetic one-shot - a dropped muzzle flash beats holding up the stream for it.
		[Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
		private void PlayFireRpc()
		{
			if (visual != null)
				visual.PlayFire();
		}
	}
}
