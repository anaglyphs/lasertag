using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.DepthKit;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.DepthKit.Meshing
{
	public class ChunkManager : MonoBehaviour
	{
		public static ChunkManager Instance { get; private set; }

		[SerializeField] private float3 chunkSize = new(5, 5, 5);
		[SerializeField] private float overlap = 0.5f;
		[SerializeField] private GameObject chunkPrefab;

		[SerializeField] private int numMeshWorkers = 2;
		[SerializeField] private int numDecimateWorkers = 1;
		[SerializeField] private float updateDistance = 4f;

		private readonly Dictionary<int3, MeshChunk> chunks = new();

		private readonly ConcurrentQueue<int3> meshQueue = new();
		private readonly SemaphoreSlim mesherSemaphore = new(0);
		private readonly ConcurrentQueue<int3> decimateQueue = new();
		private readonly SemaphoreSlim decimateSemaphore = new(0);
		private CancellationTokenSource cancelSrc;

		private readonly Vector3[] frustumCorners = new Vector3[4];
		private readonly Plane[] frustumPlanes = new Plane[6];

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			if (!EnvironmentMapper.Instance) return;

			EnvironmentMapper.Instance.Updated += OnDepthUpdate;
			EnvironmentMapper.Instance.Cleared += ClearAllChunks;
			StartWorkers();
		}

		private void OnEnable()
		{
			if (didStart) StartWorkers();
		}

		private void OnDisable()
		{
			cancelSrc?.Cancel();
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
			float near = -infiniteProj.m23 * 0.5f;

			Matrix4x4 proj = infiniteProj;

			proj.m22 = -(far + near) / (far - near);
			proj.m23 = -(2f * far * near) / (far - near);

			proj.m32 = -1f;
			proj.m33 = 0f;

			return proj;
		}

		private void Update()
		{
			Matrix4x4 proj = DepthKitDriver.Instance.Proj[0];
			proj = WithFiniteFarPlane(proj, updateDistance);
			Matrix4x4 view = DepthKitDriver.Instance.View[0];
			GeometryUtility.CalculateFrustumPlanes(proj * view, frustumPlanes);
		}

		private void OnDepthUpdate()
		{
			DepthKitDriver d = DepthKitDriver.Instance;

			// depth matrices
			Matrix4x4 proj = d.Proj[0];
			proj = WithFiniteFarPlane(proj, updateDistance);
			Matrix4x4 projInv = proj.inverse;
			Matrix4x4 view = d.View[0];
			Matrix4x4 viewInv = view.inverse;

			// get frustum corners
			for (int i = 0; i < 4; i++)
			{
				Vector4 localCorner = projInv * ndcCorners[i];

				Vector3 farCorner = new(
					localCorner.x / localCorner.w,
					localCorner.y / localCorner.w,
					localCorner.z / localCorner.w
				);

				frustumCorners[i + 0] = viewInv.MultiplyPoint(farCorner);
			}

			float3 boxMin = frustumCorners[0];
			float3 boxMax = frustumCorners[0];

			for (int i = 1; i < frustumCorners.Length; i++)
			{
				Vector3 t = frustumCorners[i];
				boxMin = math.min(boxMin, t);
				boxMax = math.max(boxMax, t);
			}

			int3 chunkCheckMin = (int3)math.floor(boxMin / chunkSize - 1);
			int3 chunkCheckMax = (int3)math.floor(boxMax / chunkSize + 1);

			for (int x = chunkCheckMin.x; x <= chunkCheckMax.x; x++)
			for (int y = chunkCheckMin.y; y <= chunkCheckMax.y; y++)
			for (int z = chunkCheckMin.z; z <= chunkCheckMax.z; z++)
			{
				int3 coord = new(x, y, z);

				if (meshQueue.Contains(coord))
					continue;

				if (CheckChunkWithinViewFrustum(coord))
				{
					bool foundChunk = chunks.TryGetValue(coord, out MeshChunk chunk);
					if (!foundChunk) chunk = InstantiateChunk(coord);
					chunk.dirty = true;
					meshQueue.Enqueue(coord);
					mesherSemaphore.Release();
				}
			}
		}

		private bool CheckChunkWithinViewFrustum(int3 coord)
		{
			float3 min = coord * chunkSize;
			float3 center = min + chunkSize / 2f;
			Bounds b = new(center, chunkSize);

			return GeometryUtility.TestPlanesAABB(frustumPlanes, b);
		}

		private void StartWorkers()
		{
			cancelSrc?.Cancel();
			cancelSrc = new CancellationTokenSource();

			for (int i = 0; i < numMeshWorkers; i++)
				_ = RunMesherWorker(cancelSrc.Token);

			for (int i = 0; i < numDecimateWorkers; i++)
				_ = RunDecimateWorker(cancelSrc.Token);
		}

		private async Task RunMesherWorker(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await mesherSemaphore.WaitAsync(ctkn);

					if (!meshQueue.TryDequeue(out int3 coord))
						continue;

					if (!chunks.TryGetValue(coord, out MeshChunk chunk))
						chunk = InstantiateChunk(coord);

					await chunk.Mesh(ctkn);

					if (!CheckChunkWithinViewFrustum(coord) && chunk.IsPopulated && !decimateQueue.Contains(coord))
					{
						decimateQueue.Enqueue(coord);
						decimateSemaphore.Release();
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private async Task RunDecimateWorker(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await decimateSemaphore.WaitAsync(ctkn);

					if (!decimateQueue.TryDequeue(out int3 coord))
						continue;

					if (!chunks.TryGetValue(coord, out MeshChunk chunk))
						continue;

					await chunk.Decimate(ctkn);
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private MeshChunk InstantiateChunk(int3 chunkCoord)
		{
			GameObject g = Instantiate(chunkPrefab, transform);
			g.TryGetComponent(out MeshChunk chunk);

			chunk.extents = chunkSize + overlap;
			chunk.transform.position = ChunkCoordToPos(chunkCoord);

			chunks.Add(chunkCoord, chunk);

			return chunk;
		}

		private float3 ChunkCoordToPos(int3 chunkCoord)
		{
			return chunkCoord * chunkSize;
		}

		public void ClearAllChunks()
		{
			cancelSrc?.Cancel();
			foreach (MeshChunk chunk in chunks.Values) Destroy(chunk.gameObject);
			chunks.Clear();

			if (enabled) StartWorkers();
		}
	}
}