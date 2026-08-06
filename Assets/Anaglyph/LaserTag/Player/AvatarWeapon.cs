using Anaglyph.Input;
using Anaglyph.Lasertag.Weapons;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag.Networking
{
	/// <summary>
	/// Shows what one of this avatar's hands is holding to everyone else. Only visuals
	/// travel - the owner fires its own weapon, and bolts are networked in their own right.
	/// </summary>
	public class AvatarWeapon : NetworkBehaviour
	{
		[SerializeField] private Handedness handedness;
		[SerializeField] private WeaponDatabase database;

		private NetworkVariable<int> weaponIdSync = new(WeaponDatabase.NoWeapon);

		// hidden while the hand is untracked or weapons are switched off
		private NetworkVariable<bool> shownSync = new();
		private NetworkVariable<bool> firingSync = new();

		// owner: the local weapon being relayed
		private GameObject relayedPrefab;
		private WeaponView relayedView;
		private int relayedId = WeaponDatabase.NoWeapon;

		// everyone else: the visuals spawned for it
		private WeaponView view;
		private int viewId = WeaponDatabase.NoWeapon;

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
				return;

			weaponIdSync.OnValueChanged += OnWeaponIdChanged;
			shownSync.OnValueChanged += OnShownChanged;
			firingSync.OnValueChanged += OnFiringChanged;

			Refresh();
		}

		public override void OnNetworkDespawn()
		{
			weaponIdSync.OnValueChanged -= OnWeaponIdChanged;
			shownSync.OnValueChanged -= OnShownChanged;
			firingSync.OnValueChanged -= OnFiringChanged;

			Relay(null);
		}

		private void Update()
		{
			if (!IsSpawned || !IsOwner)
				return;

			WeaponSwitcher switcher = WeaponSwitcher.Instance;

			if (switcher == null)
				return;

			GameObject prefab = switcher.GetHeldPrefab(handedness);

			if (prefab != relayedPrefab)
			{
				relayedPrefab = prefab;
				relayedId = database.IndexOf(prefab);
			}

			Relay(switcher.GetHeldView(handedness));

			weaponIdSync.Value = relayedId;
			shownSync.Value = switcher.IsHeldWeaponShown(handedness);
			firingSync.Value = relayedView != null && relayedView.IsFiring;
		}

		private void Relay(WeaponView weaponView)
		{
			if (weaponView == relayedView)
				return;

			if (relayedView != null)
				relayedView.Fired -= OnRelayedViewFired;

			relayedView = weaponView;

			if (relayedView != null)
				relayedView.Fired += OnRelayedViewFired;
		}

		private void OnRelayedViewFired()
		{
			PlayFireRpc();
		}

		// Cosmetic one-shot - a dropped muzzle flash beats holding up the stream for it.
		[Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
		private void PlayFireRpc()
		{
			if (view != null && view.isActiveAndEnabled)
				view.PlayFire();
		}

		private void OnWeaponIdChanged(int previous, int current) => Refresh();
		private void OnShownChanged(bool previous, bool current) => Refresh();
		private void OnFiringChanged(bool previous, bool current) => Refresh();

		private void Refresh()
		{
			if (viewId != weaponIdSync.Value)
			{
				viewId = weaponIdSync.Value;

				if (view != null)
					Destroy(view.gameObject);

				WeaponView viewPrefab = database.GetViewPrefab(viewId);

				view = viewPrefab == null ? null : Instantiate(viewPrefab, transform, false);

				if (view != null)
					view.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
			}

			if (view == null)
				return;

			view.gameObject.SetActive(shownSync.Value);
			view.SetFiring(firingSync.Value);
		}
	}
}
