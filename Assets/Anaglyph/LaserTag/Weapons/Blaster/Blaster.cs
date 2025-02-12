using Anaglyph.Lasertag.Logistics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag.Weapons
{
	public class Blaster : MonoBehaviour
	{
		[SerializeField] private GameObject boltPrefab;
		[SerializeField] private Transform emitFromTransform;
		public UnityEvent onFire = new();

		private void OnFire(InputValue value)
		{
			if(value.isPressed)
				Fire();
		}

		public void Fire()
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.canFire)
				return;

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, emitFromTransform.position, emitFromTransform.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			onFire.Invoke();
		}
	}
}
