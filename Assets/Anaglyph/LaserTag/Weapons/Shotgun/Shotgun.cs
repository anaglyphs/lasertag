using Anaglyph.Lasertag.Logistics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag.Weapons
{
	public class Shotgun : MonoBehaviour
	{
		[SerializeField] private GameObject boltPrefab = null;
		[SerializeField] private Transform emitFromTransform = null;
		public UnityEvent onFire = new();

		[SerializeField] private int fixedUpdatesPerFire = 5;
		private int fixedUpdatesUntilCanFire = 0;

		[SerializeField] private int boltCount = 10;
		[SerializeField] private float angleDeviation = 0.1f;

		public void Fire()
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.canFire || fixedUpdatesUntilCanFire > 0)
				return;

			fixedUpdatesUntilCanFire = fixedUpdatesPerFire;

			for (int i = 0; i < boltCount; i++)
			{

				NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
					boltPrefab, emitFromTransform.position, emitFromTransform.rotation);

				n.transform.rotation *= Quaternion.Euler(
					Random.Range(-angleDeviation, angleDeviation),
					Random.Range(-angleDeviation, angleDeviation),
					Random.Range(-angleDeviation, angleDeviation));

				n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);
			}

			onFire.Invoke();
		}

		private void FixedUpdate()
		{
			if (fixedUpdatesPerFire > 0) {
				fixedUpdatesUntilCanFire -= 1;
			}
		}
	}
}
