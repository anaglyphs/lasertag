using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.DepthKit;
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

		private readonly Vector3[] frustumCorners = new Vector3[4];
		private readonly Plane[] frustumPlanes = new Plane[6];
		private static readonly Rect FullRect = new(0, 0, 1, 1);

		private CancellationTokenSource updateLoopCts;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			if (!EnvironmentMapper.Instance) return;

			EnvironmentMapper.Instance.Updated += OnDepthUpdate;
			UpdateLoop();
		}

		private void OnEnable()
		{
			if (didStart) UpdateLoop();
		}

		private void OnDisable()
		{
			updateLoopCts?.Cancel();
		}

		private static readonly Vector4[] ndcCorners =
		{
			new(-1, -1, 1, 1), // bottom-left-far
			new(1, -1, 1, 1), // bottom-right-far
			new(1, 1, 1, 1), // top-right-far
			new(-1, 1, 1, 1) // top-left-far
		};
		
		public static Matrix4x4 WithFiniteFarPlane(Matrix4x4 infiniteProj, float far)
		{
			// Recover the near plane from the infinite projection
			// For an infinite projection: m23 = -2 * near
			float near = -infiniteProj.m23 * 0.5f;

			Matrix4x4 proj = infiniteProj;

			// Replace the Z mapping terms with the finite-far equivalents
			proj.m22 = -(far + near) / (far - near);
			proj.m23 = -(2f * far * near) / (far - near);

			// These are already correct for a standard perspective matrix,
			// but we set them explicitly for clarity.
			proj.m32 = -1f;
			proj.m33 = 0f;

			return proj;
		}

		private static void GetFrustumCorners(Matrix4x4 projInv, Matrix4x4 viewInv, Vector3[] results)
		{
			// Transform each corner from NDC to world space
			for (int i = 0; i < 4; i++)
			{
				Vector4 localCorner = projInv * ndcCorners[i];

				Vector3 farCorner = new(
					localCorner.x / localCorner.w,
					localCorner.y / localCorner.w,
					localCorner.z / localCorner.w
				);

				results[i + 0] = viewInv.MultiplyPoint(farCorner);
			}
		}

		private void OnDepthUpdate()
		{
			DepthKitDriver d = DepthKitDriver.Instance;
			Matrix4x4 proj = d.GetProjMat();
			proj = WithFiniteFarPlane(proj, updateDistance);
			Matrix4x4 projInv = proj.inverse;
			Matrix4x4 view = d.GetViewMat();
			Matrix4x4 viewInv = view.inverse;

			GetFrustumCorners(projInv, viewInv, frustumCorners);

			float3 boxMin = frustumCorners[0];
			float3 boxMax = frustumCorners[0];

			for (int i = 1; i < frustumCorners.Length; i++)
			{
				Vector3 t = frustumCorners[i];
				boxMin = math.min(boxMin, t);
				boxMax = math.max(boxMax, t);
			}

			int3 chunkCheckMin = (int3)math.floor(boxMin / chunkSize);
			int3 chunkCheckMax = (int3)math.floor(boxMax / chunkSize);

			GeometryUtility.CalculateFrustumPlanes(proj * view, frustumPlanes);

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
			updateLoopCts?.Cancel();
			updateLoopCts = new CancellationTokenSource();

			CancellationToken ctkn = updateLoopCts.Token;

			try
			{
				while (enabled)
				{
					if (updateQueue.Count > 0)
					{
						int3 coord = updateQueue.Dequeue();

						bool foundChunk = chunks.TryGetValue(coord, out MeshChunk chunk);
						if (!foundChunk) chunk = InstantiateChunk(coord);

						await chunk.Mesh(ctkn);
					}

					await Awaitable.WaitForSecondsAsync(updateFrequency, ctkn);
				}
			}
			catch (OperationCanceledException _)
			{
			}
		}

		private MeshChunk InstantiateChunk(int3 chunkCoord)
		{
			GameObject g = Instantiate(chunkPrefab, transform);
			g.TryGetComponent(out MeshChunk chunk);

			float connectionPadding = 3 * Mapper.VoxSize;
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