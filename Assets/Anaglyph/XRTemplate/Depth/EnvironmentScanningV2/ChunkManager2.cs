using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.DepthKit;
using Anaglyph.XRTemplate;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public class ChunkManager2 : MonoBehaviour
{
	[SerializeField] private GameObject chunkPrefab;
	[SerializeField] private int numMeshWorkers = 2;
	[SerializeField] private int numDecimateWorkers = 1;

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

		// for (int i = 0; i < numDecimateWorkers; i++)
		// 	_ = RunDecimateWorker(workerCancelSrc.Token);
	}

	private async void OnScanUpdate()
	{
		try
		{
			if (busy) return;
			busy = true;

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

					GameObject g = Instantiate(chunkPrefab, newChunkPos, Quaternion.identity);
					chunk = g.GetComponent<Chunk2>();
					chunk.chunkIndex = chunkIndex;
					chunks.Add(chunkIndex, chunk);
				}

				if (!meshQueue.Contains(chunk))
				{
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

		try
		{
			while (!ctkn.IsCancellationRequested)
			{
				await mesherSemaphore.WaitAsync(ctkn);

				if (!meshQueue.TryDequeue(out Chunk2 chunk))
					continue;

				EnvScanner2.ChunkDataReadbackResult req = await scanner.ReadbackChunk(chunk.chunkIndex);

				if (!req.valid) continue;

				ctkn.ThrowIfCancellationRequested();

				// NativeArray<uint> reqData = req.GetData<uint>();
				req.data.Reinterpret<NetMesher2.Voxel>(sizeof(sbyte)).CopyTo(voxelData);

				bool isPopulated = await mesher.CreateMesh(voxelData, chunkSize, 0.1f, chunk.mesh, ctkn);

				ctkn.ThrowIfCancellationRequested();

				// if (!ChunkIsWithinFrustum(coord) && chunk.IsPopulated && !decimateQueue.Contains(coord))
				// {
				// 	decimateQueue.Enqueue(coord);
				// 	decimateSemaphore.Release();
				// }
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			mesher.Dispose();
		}
	}

	// private async Task RunDecimateWorker(CancellationToken ctkn)
	// {
	// 	try
	// 	{
	// 		while (!ctkn.IsCancellationRequested)
	// 		{
	// 			await decimateSemaphore.WaitAsync(ctkn);
	//
	// 			if (!decimateQueue.TryDequeue(out int3 coord))
	// 				continue;
	//
	// 			if (!chunks.TryGetValue(coord, out MeshChunk chunk))
	// 				continue;
	//
	// 			await chunk.Decimate(ctkn);
	// 		}
	// 	}
	// 	catch (OperationCanceledException)
	// 	{
	// 	}
	// }
}