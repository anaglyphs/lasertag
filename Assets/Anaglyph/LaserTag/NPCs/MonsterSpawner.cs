using System;
using System.Threading;
using Anaglyph.XR;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Anaglyph.LaserTag.NPCs
{
	public class MonsterSpawner : MonoBehaviour
	{
		[Header("Spawn Settings")] public GameObject[] prefabsToSpawn;
		public float minRadius = 10f;
		public float maxRadius = 50f;
		public float spawnEverySeconds = 1;

		[Header("NavMesh Sampling")] public int maxSampleAttempts = 30; // attempts per spawn to find a valid point

		private void OnEnable()
		{
			SpawnLoop();
		}

		private async void SpawnLoop()
		{
			CancellationToken ctkn = destroyCancellationToken;
			
			try
			{
				while (enabled)
				{
					await Awaitable.WaitForSecondsAsync(spawnEverySeconds, ctkn);

					if (!enabled) return;

					TrySpawnPrefab();
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void TrySpawnPrefab()
		{
			Transform playerHead = MainXRRig.Camera.transform;

			for (int attempt = 0; attempt < maxSampleAttempts; attempt++)
			{
				Vector2 randCirc = UnityEngine.Random.insideUnitCircle;
				Vector3 randDir = new(randCirc.x, 0, randCirc.y);
				Vector3 samplePos = playerHead.position + randDir * UnityEngine.Random.Range(minRadius, maxRadius);

				if (NavMesh.SamplePosition(samplePos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
				{
					if (hit.position.y > playerHead.position.y)
						continue;

					if (Vector3.Distance(hit.position, playerHead.position) < minRadius)
						continue;

					SpawnAtPosition(hit.position);
					return;
				}
			}
		}

		private void SpawnAtPosition(Vector3 position)
		{
			GameObject prefab = prefabsToSpawn[UnityEngine.Random.Range(0, prefabsToSpawn.Length)];
			GameObject g = Instantiate(prefab, position, Quaternion.identity);
			g.GetComponent<NetworkObject>().Spawn();
		}
	}
}