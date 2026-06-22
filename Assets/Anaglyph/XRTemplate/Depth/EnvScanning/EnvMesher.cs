using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meshia.MeshSimplification;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Anaglyph.DepthKit.EnvScanning
{
	/// <summary>
	/// Instantiates and meshes visible chunks from <see cref="EnvScanner"/>
	/// </summary>
	public class EnvMesher : MonoBehaviour
	{
		public static EnvMesher Instance { get; private set; }

		public const string EnvironmentMeshLayerName = "EnvironmentMesh";
		private LayerMask envMeshLayerMask;

		[SerializeField] private GameObject chunkPrefab;
		[SerializeField] private int numMeshWorkers = 2;

		[SerializeField] private int meshSweptVoxelsThreshold = 10;

		[SerializeField] private UniversalRendererData rendererData;

		[Header("Mesh decimation options")] public MeshSimplificationTarget decimationTarget = new()
		{
			Kind = MeshSimplificationTargetKind.ScaledTotalError,
			Value = 0.5f
		};

		public MeshSimplifierOptions decimationOptions = new()
		{
			EnableSmartLink = false,
			MinNormalDot = 0.8f,
			PreserveBorderEdges = true,
			PreserveSurfaceCurvature = false,
			UseBarycentricCoordinateInterpolation = false,
			VertexLinkDistance = 0.0001f,
			VertexLinkMinNormalDot = 0.95f,
			VertexLinkColorDistance = 0.01f,
			VertexLinkUvDistance = 0.001f
		};

		/// <summary>
		/// Invoked for every visible chunk each scan update with its cumulative change sum
		/// </summary>
		public event Action<int, uint> VisibleChunkPolled = delegate { };

		/// <summary>
		/// Invoked when a chunk's mesh has finished meshing and decimating
		/// </summary>
		public event Action<Chunk> ChunkMeshUpdated = delegate { };

		private readonly Dictionary<int, Chunk> chunks = new();
		private readonly ConcurrentQueue<Chunk> meshQueue = new();
		private readonly SemaphoreSlim mesherSemaphore = new(0);
		private CancellationTokenSource workerCancelSrc;

		private bool busy = false;

		public static Vector3 ChunkWorldSize { get; private set; }
		public static Vector3 ChunkWorldSizeHalf { get; private set; }

		private void Awake()
		{
			Instance = this;
			envMeshLayerMask = LayerMask.GetMask(EnvironmentMeshLayerName);

			// hide EnvironmentMesh layer from rendering in normal camera
			SetChunksVisible(false);
		}

		private void Start()
		{
			ChunkWorldSize = EnvScanner.Instance.ChunkWorldSizeDim * Vector3.one;
			ChunkWorldSizeHalf = ChunkWorldSize / 2f;

			EnvScanner.Instance.Cleared += OnClear;

			Begin();
		}

		public void SetChunksVisible(bool visible)
		{
			if (visible)
			{
				rendererData.prepassLayerMask |= envMeshLayerMask;
				rendererData.opaqueLayerMask |= envMeshLayerMask;
				rendererData.transparentLayerMask |= envMeshLayerMask;
			}
			else
			{
				rendererData.prepassLayerMask &= ~envMeshLayerMask;
				rendererData.opaqueLayerMask &= ~envMeshLayerMask;
				rendererData.transparentLayerMask &= ~envMeshLayerMask;
			}
		}

		private void OnEnable()
		{
			if (didStart) Begin();
		}

		private void Begin()
		{
			EnvScanner.Instance.Updated += OnScanUpdate;

			StartWorkers();
		}

		private void OnDisable()
		{
			workerCancelSrc?.Cancel();

			if (EnvScanner.Instance)
				EnvScanner.Instance.Updated -= OnScanUpdate;
		}

		private void OnDestroy()
		{
			if (EnvScanner.Instance)
				EnvScanner.Instance.Cleared -= OnClear;
		}

		private void OnClear()
		{
			workerCancelSrc?.Cancel();

			foreach (Chunk chunk in chunks.Values)
				Destroy(chunk.gameObject);

			chunks.Clear();
			meshQueue.Clear();

			if (enabled)
				StartWorkers();
		}


		private void StartWorkers()
		{
			workerCancelSrc?.Cancel();
			workerCancelSrc = new CancellationTokenSource();

			for (int i = 0; i < numMeshWorkers; i++)
				_ = RunMesherWorker(workerCancelSrc.Token);
		}

		private async void OnScanUpdate()
		{
			if (busy) return;
			busy = true;

			EnvScanner scanner = EnvScanner.Instance;

			try
			{
				EnvScanner.VisibleChunksReadbackResult visResult = await scanner.ReadbackVisibleChunks();

				if (!visResult.valid) return;

				for (int i = 0; i < visResult.count; i++)
				{
					int chunkIndex = visResult.visibleChunks[i];

					Chunk chunk = GetOrCreateChunk(chunkIndex);

					uint changeSum = visResult.changeSums[i];

					VisibleChunkPolled.Invoke(chunkIndex, changeSum);

					// subtraction guards against changeSum + changeSumMeshingThreshold becoming a long
					if (!chunk.dirty &&
					    changeSum - chunk.lastMeshingChangeSum >= (uint)(meshSweptVoxelsThreshold * 254))
					{
						chunk.lastMeshingChangeSum = changeSum;
						chunk.dirty = true;
						meshQueue.Enqueue(chunk);
						mesherSemaphore.Release();
					}
				}
			}
			finally
			{
				busy = false;
			}
		}

		public Chunk GetOrCreateChunk(int chunkIndex)
		{
			if (chunks.TryGetValue(chunkIndex, out Chunk chunk))
				return chunk;

			EnvScanner scanner = EnvScanner.Instance;
			int3 chunkCoord = scanner.ChunkIndexToChunkCoord(chunkIndex);
			float3 newChunkPos = scanner.ChunkCoordToCornerWorldPos(chunkCoord);

			GameObject g = Instantiate(chunkPrefab, newChunkPos, Quaternion.identity, transform);
			g.name = "Chunk " + chunkIndex;
			chunk = g.GetComponent<Chunk>();
			chunk.meshCollider.enabled = false;
			chunk.chunkIndex = chunkIndex;
			chunks.Add(chunkIndex, chunk);

			return chunk;
		}

		private async Task RunMesherWorker(CancellationToken ctkn)
		{
			EnvScanner scanner = EnvScanner.Instance;

			int vpcd = scanner.VoxPerChunkDim;
			int3 chunkSize = new(vpcd, vpcd, vpcd);

			NetMesher mesher = new();

			EnvScanner.ChunkReadbackBuffer readbackBuffer = scanner.CreateChunkReadbackBuffer();

			// meshed into first, so the chunk mesh and collider only
			// update once, after decimation is done
			Mesh scratchMesh = new();
			scratchMesh.MarkDynamic();

			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await mesherSemaphore.WaitAsync(ctkn);

					if (!meshQueue.TryDequeue(out Chunk chunk))
						continue;

					bool readbackSuccess = await scanner.ReadbackChunkInto(chunk.chunkIndex, readbackBuffer);

					if (!readbackSuccess) continue;

					ctkn.ThrowIfCancellationRequested();

					bool isPopulated = await mesher.CreateMesh(readbackBuffer.data, chunkSize, scanner.VoxSize,
						scratchMesh, ctkn);

					ctkn.ThrowIfCancellationRequested();

					if (isPopulated)
					{
						await MeshSimplifier.SimplifyAsync(scratchMesh, decimationTarget, decimationOptions,
							chunk.mesh, ctkn);

						chunk.meshCollider.enabled = chunk.mesh.vertexCount > 0;

						ctkn.ThrowIfCancellationRequested();

						chunk.mesh.RecalculateBounds();
						chunk.meshCollider.sharedMesh = chunk.mesh;
					}
					else
					{
						chunk.mesh.Clear();
					}

					chunk.dirty = false;

					ChunkMeshUpdated.Invoke(chunk);
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				mesher.Dispose();
				readbackBuffer.Dispose();
				DestroyImmediate(scratchMesh);
			}
		}
	}
}