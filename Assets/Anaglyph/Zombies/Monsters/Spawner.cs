using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace Anaglyph.Zombies
{
    public class Spawner : MonoBehaviour
    {
		[Header("Spawn Settings")]
		public GameObject[] prefabsToSpawn;
		public float minRadius = 10f;
		public float maxRadius = 50f;
		public float spawnEverySeconds = 1;
		public Transform playerHead;

		[Header("NavMesh Sampling")]
		public int maxSampleAttempts = 30; // attempts per spawn to find a valid point

		private void Start()
		{
			StartCoroutine(SpawnLoop());
		}

		private IEnumerator SpawnLoop()
		{
			while (true)
			{
				yield return new WaitForSeconds(spawnEverySeconds);

				TrySpawnPrefab();
			}
		}

		void TrySpawnPrefab()
		{
			for (int attempt = 0; attempt < maxSampleAttempts; attempt++)
			{
				Vector2 randCirc = Random.insideUnitCircle;
				Vector3 randDir = new(randCirc.x, 0, randCirc.y);
				Vector3 samplePos = playerHead.position + randDir * Random.Range(minRadius, maxRadius);

				if (NavMesh.SamplePosition(samplePos, out NavMeshHit hit, 2f, NavMesh.AllAreas))
				{
					if (hit.position.y > playerHead.position.y)
						continue;

					if(Vector3.Distance(hit.position, playerHead.position) < minRadius)
						continue;

					SpawnAtPosition(hit.position);
					return;
				}
			}
		}

		void SpawnAtPosition(Vector3 position)
		{
			GameObject prefab = prefabsToSpawn[Random.Range(0, prefabsToSpawn.Length)];
			Instantiate(prefab, position, Quaternion.identity);
		}
	}
}
