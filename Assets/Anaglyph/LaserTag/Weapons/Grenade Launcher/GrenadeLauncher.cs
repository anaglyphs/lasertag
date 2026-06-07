using Anaglyph.Lasertag.Logistics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag.Weapons
{
	public class GrenadeLauncher : MonoBehaviour
	{
		[SerializeField] private GameObject projectilePrefab;
		[SerializeField] private float speed;
		[SerializeField] private Transform emitFromTransform;
		public UnityEvent onFire = new();

		private void OnFire(InputAction.CallbackContext context)
		{
			if (context.performed && context.ReadValueAsButton())
				Fire();
		}

		public void Fire()
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.CanFire)
				return;

			// var e = emitFromTransform;
			// NetworkObject.InstantiateAndSpawn(boltPrefab, NetworkManager.Singleton,
			// 	position: e.position, rotation: e.rotation,
			// 	ownerClientId: NetworkManager.Singleton.LocalClientId);

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				projectilePrefab, emitFromTransform.position, emitFromTransform.rotation);

			n.GetComponent<Rigidbody>().linearVelocity = emitFromTransform.forward * speed;

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			onFire.Invoke();
		}
	}
}