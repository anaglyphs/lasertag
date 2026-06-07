using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meshia.MeshSimplification;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.DepthKit.EnvScanning
{
	/// <summary>
	/// Instantiates and meshes visible chunks from <see cref="EnvScanner"/>
	/// </summary>
	public class ChunkManager : MonoBehaviour
	{
		public static ChunkManager Instance { get; private set; }

		[SerializeField] private GameObject chunkPrefab;
		[SerializeField] private int numMeshWorkers = 2;
		[SerializeField] private int numDecimateWorkers = 1;

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

		private Dictionary<int, Chunk> chunks = new();

		private readonly ConcurrentQueue<Chunk> meshQueue = new();
		private readonly SemaphoreSlim mesherSemaphore = new(0);
		private readonly ConcurrentQueue<Chunk> decimateQueue = new();
		private readonly SemaphoreSlim decimateSemaphore = new(0);
		private CancellationTokenSource workerCancelSrc;

		private bool busy = false;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			Begin();
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

		private void StartWorkers()
		{
			workerCancelSrc?.Cancel();
			workerCancelSrc = new CancellationTokenSource();

			for (int i = 0; i < numMeshWorkers; i++)
				_ = RunMesherWorker(workerCancelSrc.Token);

			for (int i = 0; i < numDecimateWorkers; i++)
				_ = RunDecimateWorker(workerCancelSrc.Token);
		}

		private async void OnScanUpdate()
		{
			if (busy) return;
			busy = true;

			try
			{
				EnvScanner scanner = EnvScanner.Instance;
				EnvScanner.VisibleChunksReadbackResult visResult = await scanner.ReadbackVisibleChunks();

				if (!visResult.valid) return;

				for (int i = 0; i < visResult.count; i++)
				{
					int chunkIndex = visResult.visibleChunks[i];

					bool gotChunk = chunks.TryGetValue(chunkIndex, out Chunk chunk);

					if (!gotChunk)
					{
						int3 chunkCoord = scanner.ChunkIndexToChunkCoord(chunkIndex);
						float3 newChunkPos = scanner.ChunkCoordToCornerWorldPos(chunkCoord);

						GameObject g = Instantiate(chunkPrefab, newChunkPos, Quaternion.identity, transform);
						g.name = "Chunk " + chunkIndex;
						chunk = g.GetComponent<Chunk>();
						chunk.meshCollider.enabled = false;
						chunk.chunkIndex = chunkIndex;
						chunks.Add(chunkIndex, chunk);
					}

					if (!chunk.dirty)
					{
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

		private async Task RunMesherWorker(CancellationToken ctkn)
		{
			EnvScanner scanner = EnvScanner.Instance;

			int vpcd = scanner.VoxPerChunkDim;
			int vpc = vpcd * vpcd * vpcd;
			int3 chunkSize = new(vpcd, vpcd, vpcd);

			NetMesher mesher = new();
			NativeArray<NetMesher.Voxel> voxelData = new(vpc, Allocator.Persistent);

			ComputeBuffer readbackBuffer = scanner.CreateChunkReadbackBuffer();

			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await mesherSemaphore.WaitAsync(ctkn);

					if (!meshQueue.TryDequeue(out Chunk chunk))
						continue;

					EnvScanner.ChunkDataReadbackResult req =
						await scanner.ReadbackChunk(chunk.chunkIndex, readbackBuffer);

					if (!req.valid) continue;

					ctkn.ThrowIfCancellationRequested();

					req.data.Reinterpret<NetMesher.Voxel>(sizeof(sbyte)).CopyTo(voxelData);

					bool isPopulated = await mesher.CreateMesh(voxelData, chunkSize, scanner.VoxSize, chunk.mesh, ctkn);

					chunk.dirty = false;
					chunk.undecimated = true;

					ctkn.ThrowIfCancellationRequested();

					if (isPopulated && chunk.undecimated)
					{
						chunk.undecimated = true;
						chunk.meshCollider.enabled = true;
						decimateQueue.Enqueue(chunk);
						decimateSemaphore.Release();
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
			finally
			{
				mesher.Dispose();
				readbackBuffer.Dispose();
			}
		}

		private async Task RunDecimateWorker(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await decimateSemaphore.WaitAsync(ctkn);

					if (!decimateQueue.TryDequeue(out Chunk c)) continue;

					if (c.dirty) continue;

					await MeshSimplifier.SimplifyAsync(c.mesh, decimationTarget, decimationOptions, c.mesh, ctkn);

					c.undecimated = false;
				}
			}
			catch (OperationCanceledException)
			{
			}
		}
	}
}