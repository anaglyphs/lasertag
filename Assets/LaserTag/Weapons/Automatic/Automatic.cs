using XRTemplate;
using LaserTag.Logistics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace LaserTag.Weapons
{
	public class Automatic : MonoBehaviour
	{
		[SerializeField] private HandedControllerInput input;

		[SerializeField] private int energyPerShot = 2;
		[SerializeField] private float energyRefill = 2;
		private float energy = 100;

		[SerializeField] private int fixedUpdatesPerFire = 5;
		private int fixedUpdateTilNextFire = 0;

		[SerializeField] private GameObject boltPrefab;
		[SerializeField] private Transform emitFromTransform;
		public UnityEvent onFire = new();

		public void Fire()
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.canFire)
				return;

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, emitFromTransform.position, emitFromTransform.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

			onFire.Invoke();
		}

		private void FixedUpdate()
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.canFire)
				return;

			if (input.TriggerIsDown)
			{
				if (energy <= 0)
					return;

				fixedUpdateTilNextFire -= 1;

				if(fixedUpdateTilNextFire <= 0)
				{
					Fire();
					fixedUpdateTilNextFire = fixedUpdatesPerFire;
					energy -= energyPerShot;
				}
			} else {
				energy += energyRefill;
				fixedUpdateTilNextFire = 0;
			}
		}
	}
}
