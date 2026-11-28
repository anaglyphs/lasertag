using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode;
using Anaglyph.XR.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Anaglyph.LaserTag.Weapons.Automatic
{
	[RequireComponent(typeof(HandSubject))]
	public class Automatic : MonoBehaviour, IWeapon
	{
		private HandSubject hand;

		[SerializeField] private GameObject boltPrefab = null;
		[SerializeField] private Transform muzzle = null;
		[FormerlySerializedAs("view")] [SerializeField] private WeaponVisual visual = null;
		public GameObject VisualObject => visual.gameObject;
		[SerializeField] private UnityEvent onFire = new();

		[SerializeField] private float fireFrequency = 0.1f;

		private bool triggerDown;
		private bool pressedSinceLastShot;
		private float nextShotTime;

		private void Awake()
		{
			TryGetComponent(out hand);
		}

		private void OnEnable()
		{
			hand.Bind(nameof(OnFire), OnFire);
		}

		private void OnDisable()
		{
			hand.Unbind(nameof(OnFire), OnFire);
			triggerDown = false;
			pressedSinceLastShot = false;
			visual.SetFiring(false);
		}

		public void OnFire(InputAction.CallbackContext context)
		{
			triggerDown = context.ReadValueAsButton();
			pressedSinceLastShot |= triggerDown;
		}

		// shots leave here, once the muzzle holds this frame's hand pose
		private void Update()
		{
			if (!triggerDown && !pressedSinceLastShot)
			{
				visual.SetFiring(false);
				return;
			}

			if (Time.time < nextShotTime)
				return;

			pressedSinceLastShot = false;
			nextShotTime = Time.time + fireFrequency;
			visual.SetFiring(TryFire());
		}

		public void Fire()
		{
			TryFire();
		}

		private bool TryFire()
		{
			if (!MainPlayer.Instance.CanFire)
				return false;

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, muzzle.position, muzzle.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			visual.PlayFire();
			onFire.Invoke();
			return true;
		}
	}
}
