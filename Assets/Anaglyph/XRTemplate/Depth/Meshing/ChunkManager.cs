using System;
using System.Collections.Generic;
using Anaglyph.XRTemplate;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace Anaglyph.DepthKit.Meshing
{
	public class ChunkManager : MonoBehaviour
	{
		private EnvironmentMapper mapper => EnvironmentMapper.Instance;
		
		[SerializeField] private float3 chunkSize = new(5, 5, 5);
		[SerializeField] private GameObject chunkPrefab;
		
		[SerializeField] private float updateFrequency = 0.1f;

		[SerializeField] private Vector3[] frustumMeshTriggerPoints;
		[SerializeField] private Camera mainCamera;
		
		private Dictionary<int3, MeshChunk> chunks = new();
		
		List<int3> updateList = new();
		
		public void OnEnable()
		{
			mainCamera = Camera.main;
			UpdateLoop();
		}

		private async void UpdateLoop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(updateFrequency);
				
				Transform camTrans = mainCamera.transform;

				updateList.Clear();
				foreach (Vector3 local in frustumMeshTriggerPoints)
				{
					float3 global = camTrans.TransformPoint(local);
					int3 coord = PosToChunkCoord(global);
					
					if (!updateList.Contains(coord))
						updateList.Add(coord);
				}

				foreach (int3 coord in updateList)
				{
					bool foundChunk = chunks.TryGetValue(coord, out MeshChunk chunk);

					if (!foundChunk)
						chunk = InstantiateChunk(coord);

					await chunk.Mesh();
				}
			}
		}
		
#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			if (!mainCamera)
				return;
			
			Gizmos.color = new Color(1f, 0.5f, 0f, 1f);
			
			Transform camTrans = mainCamera.transform;
			foreach (Vector3 local in frustumMeshTriggerPoints)
			{
				float3 global = camTrans.TransformPoint(local);
				Gizmos.DrawWireSphere(global, 0.1f);
			}
		}
#endif

		private MeshChunk InstantiateChunk(int3 chunkCoord)
		{
			GameObject g = Instantiate(chunkPrefab);
			g.TryGetComponent(out MeshChunk chunk);

			float connectionPadding = 3 * mapper.MetersPerVoxel;
			chunk.extents = chunkSize + connectionPadding;

			chunk.transform.position = ChunkCoordToPos(chunkCoord);

			chunks.Add(chunkCoord, chunk);

			return chunk;
		}

		private int3 PosToChunkCoord(float3 pos)
		{
			return new int3((pos - chunkSize) / chunkSize);
		}

		private float3 ChunkCoordToPos(int3 chunkCoord)
		{
			return new float3(chunkCoord * chunkSize);
		}
	}
}