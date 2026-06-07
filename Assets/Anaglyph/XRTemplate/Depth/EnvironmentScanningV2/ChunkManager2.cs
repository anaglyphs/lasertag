using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meshia.MeshSimplification;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.DepthKit.EnvScanningV2
{
	/// <summary>
	/// Instantiates and meshes visible chunks from <see cref="EnvScanner2"/>
	/// </summary>
	public class ChunkManager2 : MonoBehaviour
	{
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

		private Dictionary<int, Chunk2> chunks = new();

		private readonly ConcurrentQueue<Chunk2> meshQueue = new();
		private readonly SemaphoreSlim mesherSemaphore = new(0);
		private readonly ConcurrentQueue<Chunk2> decimateQueue = new();
		private readonly SemaphoreSlim decimateSemaphore = new(0);
		private CancellationTokenSource workerCancelSrc;

		private bool busy = false;

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
			EnvScanner2.Instance.Updated += OnScanUpdate;
			StartWorkers();
		}

		private void OnDisable()
		{
			workerCancelSrc?.Cancel();

			if (EnvScanner2.Instance)
				EnvScanner2.Instance.Updated -= OnScanUpdate;
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
				EnvScanner2 scanner = EnvScanner2.Instance;
				EnvScanner2.VisibleChunksReadbackResult visResult = await scanner.ReadbackVisibleChunks();

				if (!visResult.valid) return;

				for (int i = 0; i < visResult.count; i++)
				{
					int chunkIndex = visResult.visibleChunks[i];

					bool gotChunk = chunks.TryGetValue(chunkIndex, out Chunk2 chunk);

					if (!gotChunk)
					{
						int3 chunkCoord = scanner.ChunkIndexToChunkCoord(chunkIndex);
						float3 newChunkPos = scanner.ChunkCoordToCornerWorldPos(chunkCoord);

						GameObject g = Instantiate(chunkPrefab, newChunkPos, Quaternion.identity, transform);
						g.name = "Chunk " + chunkIndex;
						chunk = g.GetComponent<Chunk2>();
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
			EnvScanner2 scanner = EnvScanner2.Instance;

			int vpcd = scanner.VoxPerChunkDim;
			int vpc = vpcd * vpcd * vpcd;
			int3 chunkSize = new(vpcd, vpcd, vpcd);

			NetMesher2 mesher = new();
			NativeArray<NetMesher2.Voxel> voxelData = new(vpc, Allocator.Persistent);

			ComputeBuffer readbackBuffer = scanner.CreateChunkReadbackBuffer();

			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await mesherSemaphore.WaitAsync(ctkn);

					if (!meshQueue.TryDequeue(out Chunk2 chunk))
						continue;

					EnvScanner2.ChunkDataReadbackResult req =
						await scanner.ReadbackChunk(chunk.chunkIndex, readbackBuffer);

					if (!req.valid) continue;

					ctkn.ThrowIfCancellationRequested();

					req.data.Reinterpret<NetMesher2.Voxel>(sizeof(sbyte)).CopyTo(voxelData);

					bool isPopulated = await mesher.CreateMesh(voxelData, chunkSize, scanner.VoxSize, chunk.mesh, ctkn);

					chunk.dirty = false;
					chunk.undecimated = true;

					ctkn.ThrowIfCancellationRequested();

					if (isPopulated && chunk.undecimated)
					{
						chunk.undecimated = true;
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

					if (!decimateQueue.TryDequeue(out Chunk2 c))
						continue;

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