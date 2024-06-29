using Anaglyph.XRTemplate;
using Anaglyph.LaserTag.Logistics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.LaserTag.Weapons
{
	public class Automatic : MonoBehaviour
	{
		[SerializeField] private HandedControllerInput input;

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
				fixedUpdateTilNextFire -= 1;

				if(fixedUpdateTilNextFire <= 0)
				{
					Fire();
					fixedUpdateTilNextFire = fixedUpdatesPerFire;
				}
			} else {
				fixedUpdateTilNextFire = 0;
			}
		}
	}
}
