using Anaglyph.Netcode;
using Anaglyph.XR.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Anaglyph.LaserTag.Weapons.Blaster
{
	[RequireComponent(typeof(HandSubject))]
	public class Blaster : MonoBehaviour, IWeapon
	{
		private HandSubject hand;

		[SerializeField] private GameObject boltPrefab;
		[SerializeField] private Transform muzzle;
		[SerializeField] private WeaponVisual visual;
		public GameObject VisualObject => visual.gameObject;
		public UnityEvent onFire = new();

		// the trigger must fall below this before the next shot. The Press
		// interaction re-arms at 75% of its press point, so a slight
		// relax would otherwise fire again
		[SerializeField, Range(0, 1)] private float rearmThreshold = 0.2f;
		private InputControl heldTrigger;

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
		}

		public void OnFire(InputAction.CallbackContext context)
		{
			if (!context.performed || !context.ReadValueAsButton() || heldTrigger != null)
				return;

			heldTrigger = context.control;
			Fire();
		}

		private void Update()
		{
			if (heldTrigger != null && heldTrigger.EvaluateMagnitude() < rearmThreshold)
				heldTrigger = null;
		}

		public void Fire()
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.CanFire)
				return;

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, muzzle.position, muzzle.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			visual.PlayFire();
			onFire.Invoke();
		}
	}
}
