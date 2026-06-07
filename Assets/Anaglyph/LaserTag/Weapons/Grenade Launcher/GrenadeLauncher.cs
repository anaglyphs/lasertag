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

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				projectilePrefab, emitFromTransform.position, emitFromTransform.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			n.GetComponent<Rigidbody>().linearVelocity = emitFromTransform.forward * speed;

			onFire.Invoke();
		}
	}
}