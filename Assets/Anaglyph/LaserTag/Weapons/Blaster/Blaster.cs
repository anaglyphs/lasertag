using System;
using Anaglyph.Input;
using Anaglyph.Lasertag.Logistics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag.Weapons
{
	public class Blaster : MonoBehaviour, IWeapon
	{
		private HandSubject hand;
		[SerializeField] private string fireAction = "Activate";

		[SerializeField] private GameObject boltPrefab;
		[SerializeField] private Transform emitFromTransform;
		public UnityEvent onFire = new();

		private void Awake()
		{
			TryGetComponent(out hand);
			hand.Bind(nameof(OnFire), OnFire);
		}

		private void OnEnable()
		{
			hand.Bind(fireAction, OnFire);
		}

		private void OnDisable()
		{
			hand.Unbind(fireAction, OnFire);
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

			// var e = emitFromTransform;
			// NetworkObject.InstantiateAndSpawn(boltPrefab, NetworkManager.Singleton,
			// 	position: e.position, rotation: e.rotation,
			// 	ownerClientId: NetworkManager.Singleton.LocalClientId);

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, emitFromTransform.position, emitFromTransform.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			onFire.Invoke();
		}
	}
}