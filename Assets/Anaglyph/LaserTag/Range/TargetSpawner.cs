using System.Runtime.CompilerServices;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag.Gallery
{
    public class TargetSpawner : NetworkBehaviour
    {
        [SerializeField] GameObject targetPrefab;
        [SerializeField] private Transform[] possibleSpawnPositions;

		public async override void OnNetworkSpawn()
		{
			if (!IsOwner)
				return;

			float spawnProb = Random.Range(0.1f, 0.5f);
			foreach(Transform t in possibleSpawnPositions)
			{
				bool shouldSpawn = Random.Range(0f, 1f) > spawnProb;

				if(shouldSpawn)
				{
					var netObj = NetworkObject.InstantiateAndSpawn(targetPrefab, NetworkManager.Singleton, NetworkManager.ServerClientId, false, false, false, new Vector3(0, 0, 0), Quaternion.identity);

					while (!netObj.IsSpawned)
						await Awaitable.NextFrameAsync();

					netObj.TrySetParent(t, false);

				}
			}
		}
	}
}
