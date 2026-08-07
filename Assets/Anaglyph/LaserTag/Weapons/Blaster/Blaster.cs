using Anaglyph.Input;
using Anaglyph.Lasertag.Logistics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Anaglyph.Lasertag.Weapons
{
	[RequireComponent(typeof(HandSubject))]
	public class Blaster : MonoBehaviour, IWeapon
	{
		private HandSubject hand;

		[SerializeField] private GameObject boltPrefab;
		[FormerlySerializedAs("view")] [SerializeField] private WeaponVisual visual;
		public GameObject VisualObject => visual.gameObject;
		public UnityEvent onFire = new();

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
			if (context.performed && context.ReadValueAsButton())
				Fire();
		}

		public void Fire()
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.CanFire)
				return;

			Transform muzzle = visual.Muzzle;
			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, muzzle.position, muzzle.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			visual.PlayFire();
			onFire.Invoke();
		}
	}
}
