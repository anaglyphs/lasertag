using System;
using System.Threading;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

namespace Anaglyph.LaserTag.NPCs
{
	public class MonsterSpawner : MonoBehaviour
	{
		[Header("Spawn Settings")]
		public GameObject[] prefabsToSpawn;
		[Tooltip("Optional override for the point spawns are positioned around. Falls back to the main XR camera, then Camera.main.")]
		public Transform spawnOrigin;
		[FormerlySerializedAs("minRadius")] public float minSpawnRadius = 10f;
		public float spawnEverySeconds = 1;

		[Header("NavMesh Sampling")]
		[Min(1)] public int maxSampleAttempts = 30; // attempts per spawn to find a valid point

		private CancellationTokenSource spawnLoopCancellation;

		private void OnEnable()
		{
			spawnLoopCancellation = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
			SpawnLoop(spawnLoopCancellation);
		}

		private void OnDisable()
		{
			spawnLoopCancellation?.Cancel();
		}

		private async void SpawnLoop(CancellationTokenSource cancellation)
		{
			try
			{
				while (!cancellation.IsCancellationRequested)
				{
					await Awaitable.WaitForSecondsAsync(Mathf.Max(0f, spawnEverySeconds), cancellation.Token);

					if (cancellation.IsCancellationRequested)
						return;

					TrySpawnRandomly();
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				if (spawnLoopCancellation == cancellation)
					spawnLoopCancellation = null;

				cancellation.Dispose();
			}
		}

		private void TrySpawnRandomly()
		{
			if (!TryGetSpawnOrigin(out Transform origin))
				return;
			
			EnvMesher envMesher = EnvMesher.Instance;
			EnvScanner envScanner = EnvScanner.Instance;

			for (int attempt = 0; attempt < Mathf.Max(1, maxSampleAttempts); attempt++)
			{
				int randChunkIndex = UnityEngine.Random.Range(0, envMesher.ChunksList.Count);
				Chunk randChunk = envMesher.ChunksList[randChunkIndex];

				Vector3 randChunkPos = randChunk.transform.position;
				randChunkPos += Vector3.one * envScanner.ChunkWorldSizeDim / 2f;

				if (NavMesh.SamplePosition(randChunkPos, out NavMeshHit hit, envScanner.ChunkWorldSizeDim * 2, NavMesh.AllAreas))
				{
					if (hit.position.y > origin.position.y)
						continue;

					Vector3 offset = hit.position - origin.position;
					offset.y = 0f;
					float distance = offset.magnitude;

					if (distance < minSpawnRadius)
						continue;

					SpawnAtPosition(hit.position);
					break;
				}
			}
		}

		private bool TryGetSpawnOrigin(out Transform origin)
		{
			origin = spawnOrigin;

			if (origin == null && MainXRRig.Instance != null)
				origin = MainXRRig.Instance.camera?.transform;

			if (origin == null)
				origin = Camera.main?.transform;

			return origin != null;
		}

		private bool SpawnAtPosition(Vector3 position)
		{
			if (prefabsToSpawn == null || prefabsToSpawn.Length == 0)
				return false;

			int firstPrefabIndex = UnityEngine.Random.Range(0, prefabsToSpawn.Length);

			for (int offset = 0; offset < prefabsToSpawn.Length; offset++)
			{
				GameObject prefab = prefabsToSpawn[(firstPrefabIndex + offset) % prefabsToSpawn.Length];

				if (prefab == null || !prefab.TryGetComponent(out NetworkObject _))
					continue;

				GameObject instance = Instantiate(prefab, position, Quaternion.identity);
				instance.GetComponent<NetworkObject>().Spawn();
				return true;
			}

			return false;
		}
	}
}
