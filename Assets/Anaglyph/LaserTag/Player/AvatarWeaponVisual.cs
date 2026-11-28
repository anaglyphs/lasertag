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
		[SerializeField, Min(0.01f)] private float flyToHandSeconds = 0.25f;

		private NetworkVariable<int> weaponIdSync = new(WeaponDatabase.NoWeapon);

		// hidden while the hand is untracked or weapons are put away
		private NetworkVariable<bool> shownSync = new();
		private NetworkVariable<bool> firingSync = new();

		private GameObject instance;
		private GameObject presentationRoot;
		private PlayerAvatar avatar;
		private AvatarPresentation presentation;
		private WeaponVisual visual;
		private int id = WeaponDatabase.NoWeapon;
		public GameObject EquippedWeapon => database.GetWeapon(id);

		// owner only
		private GameObject selectedPrefab;
		private bool weaponsActive = true;

		private Pose flyStartWorldPose;
		private Pose visualPoseInParent;
		private float flyProgress = 1;

		private void Awake()
		{
			avatar = GetComponentInParent<PlayerAvatar>(true);
			presentation = GetComponentInParent<AvatarPresentation>(true);
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

		private void LateUpdate()
		{
			if (flyProgress >= 1 || visual == null)
				return;

			flyProgress = Mathf.Min(1, flyProgress + Time.deltaTime / flyToHandSeconds);
			float eased = 1 - Mathf.Pow(1 - flyProgress, 3);

			Transform visualTransform = visual.transform;
			Transform parent = visualTransform.parent;
			Vector3 targetPosition = parent.TransformPoint(visualPoseInParent.position);
			Quaternion targetRotation = parent.rotation * visualPoseInParent.rotation;

			visualTransform.SetPositionAndRotation(
				Vector3.Lerp(flyStartWorldPose.position, targetPosition, eased),
				Quaternion.Slerp(flyStartWorldPose.rotation, targetRotation, eased));
		}

		private void StartFlyingIn(Pose startWorldPose)
		{
			visual.transform.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
			visualPoseInParent = new Pose(position, rotation);
			flyStartWorldPose = startWorldPose;
			flyProgress = 0;
			LateUpdate();
		}

		private void Show(int weaponId)
		{
			if (weaponId == id)
				return;

			id = weaponId;
			flyProgress = 1;

			if (visual != null)
				visual.Fired -= OnFired;

			if (instance != null)
				Destroy(instance);

			instance = null;
			visual = null;

			GameObject prefab = IsOwner ? database.GetWeapon(id) : database.GetView(id);

			if (prefab == null)
				return;

			presentationRoot.SetActive(false);
			instance = Instantiate(prefab, presentationRoot.transform, false);
			visual = instance.GetComponentInChildren<WeaponVisual>(true);
			PlaceHandleOnHand(instance.transform, visual != null ? visual.Handle : null);

			if (!IsOwner)
			{
				ApplySyncedState();
				return;
			}

			if (instance.TryGetComponent(out HandSubject handSubject))
				handSubject.Assign(HandInput.Get(handedness));

			presentationRoot.SetActive(avatar.CanInteract && weaponsActive);

			if (visual != null)
			{
				visual.Fired += OnFired;

				if (WeaponSwitcher.Instance.TakeArrivalPose(handedness, out Pose arrivalPose))
					StartFlyingIn(arrivalPose);
			}
		}

		// the hand transform is the grip pose, so the weapon is moved until its handle sits there
		private static void PlaceHandleOnHand(Transform weapon, Transform handle)
		{
			weapon.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
			if (handle == null) return;

			Quaternion handleRotationInWeapon = Quaternion.Inverse(weapon.rotation) * handle.rotation;
			Vector3 handlePositionInWeapon = weapon.InverseTransformPoint(handle.position);
			Quaternion weaponRotation = Quaternion.Inverse(handleRotationInWeapon);
			weapon.SetLocalPositionAndRotation(weaponRotation * -handlePositionInWeapon, weaponRotation);
		}

		// what the owner's own weapon does for itself, applied to everyone else's copy
		private void ApplySyncedState()
		{
			bool shown = IsSpawned && presentation.IsShown && avatar.IsAlive;
			presentationRoot.SetActive(shown && shownSync.Value);
			if (instance == null)
				return;

			if (visual != null)
				visual.SetFiring(shown && firingSync.Value);
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
			if (visual != null)
				visual.PlayFire();
		}
	}
}
