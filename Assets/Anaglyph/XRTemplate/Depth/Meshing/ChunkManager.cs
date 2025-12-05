using System;
using System.Collections.Generic;
using Anaglyph.XRTemplate;
using Unity.AI.Navigation;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.DepthKit.Meshing
{
	public class ChunkManager : MonoBehaviour
	{
		public static ChunkManager Instance { get; private set; }

		private EnvironmentMapper Mapper => EnvironmentMapper.Instance;

		[SerializeField] private float3 chunkSize = new(5, 5, 5);
		[SerializeField] private GameObject chunkPrefab;

		[SerializeField] private float updateFrequency = 0.1f;
		[SerializeField] private float updateDistance = 4f;

		private readonly Dictionary<int3, MeshChunk> chunks = new();
		private readonly Queue<int3> updateQueue = new();

		private Camera mainCamera;

		private readonly Vector3[] frustumCorners = new Vector3[8];
		private readonly Plane[] frustumPlanes = new Plane[6];
		private static readonly Rect FullRect = new(0, 0, 1, 1);
		private const Camera.MonoOrStereoscopicEye Eye = Camera.MonoOrStereoscopicEye.Left;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			if (!XRSettings.enabled)
				return;

			mainCamera = Camera.main;
			mainCamera.CalculateFrustumCorners(FullRect, updateDistance, Eye, frustumCorners);

			UpdateLoop();
		}

		private void OnEnable()
		{
			if (didStart) UpdateLoop();
		}

		private void FixedUpdate()
		{
			Transform camTrans = mainCamera.transform;
			float3 boxMin = camTrans.position;
			float3 boxMax = camTrans.position;

			foreach (Vector3 t in frustumCorners)
			{
				float3 globalCorner = camTrans.TransformPoint(t);
				boxMin = math.min(boxMin, globalCorner);
				boxMax = math.max(boxMax, globalCorner);
			}

			int3 chunkCheckMin = (int3)math.floor(boxMin / chunkSize);
			int3 chunkCheckMax = (int3)math.floor(boxMax / chunkSize);

			GeometryUtility.CalculateFrustumPlanes(mainCamera, frustumPlanes);

			for (int x = chunkCheckMin.x; x <= chunkCheckMax.x; x++)
			for (int y = chunkCheckMin.y; y <= chunkCheckMax.y; y++)
			for (int z = chunkCheckMin.z; z <= chunkCheckMax.z; z++)
			{
				int3 coord = new(x, y, z);

				if (updateQueue.Contains(coord))
					continue;

				float3 min = coord * chunkSize;
				float3 center = min + chunkSize / 2f;
				Bounds b = new(center, chunkSize);

				if (GeometryUtility.TestPlanesAABB(frustumPlanes, b))
				{
					bool foundChunk = chunks.TryGetValue(coord, out MeshChunk chunk);
					if (!foundChunk) chunk = InstantiateChunk(coord);
					chunk.dirty = true;

					updateQueue.Enqueue(coord);
				}
			}
		}

		private async void UpdateLoop()
		{
			while (enabled)
			{
				if (updateQueue.Count > 0)
				{
					int3 coord = updateQueue.Dequeue();

					bool foundChunk = chunks.TryGetValue(coord, out MeshChunk chunk);
					if (!foundChunk) chunk = InstantiateChunk(coord);

					await chunk.Mesh();
				}

				await Awaitable.WaitForSecondsAsync(updateFrequency);
			}
		}

		private MeshChunk InstantiateChunk(int3 chunkCoord)
		{
			GameObject g = Instantiate(chunkPrefab, transform);
			g.TryGetComponent(out MeshChunk chunk);

			float connectionPadding = 2 * Mapper.VoxelSize;
			chunk.extents = chunkSize + connectionPadding;

			chunk.transform.position = ChunkCoordToPos(chunkCoord);

			chunks.Add(chunkCoord, chunk);

			return chunk;
		}

		private int3 PosToChunkCoord(float3 pos)
		{
			return new int3(math.floor(pos / chunkSize));
		}

		private float3 ChunkCoordToPos(int3 chunkCoord)
		{
			return chunkCoord * chunkSize;
		}
	}
}