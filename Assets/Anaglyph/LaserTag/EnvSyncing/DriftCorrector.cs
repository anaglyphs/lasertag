using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.DepthKit.EnvScanning;
using Anaglyph.DriftCorrection;
using Anaglyph.XRTemplate;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Mitigates tracking drift between colocated headsets. Samples chunk
	/// meshes received from other players and aligns them against the local
	/// TSDF volume with point-to-TSDF ICP, then nudges the tracking space
	/// by the resulting delta. Chunks are picked spread across the map so
	/// the solve gets long lever arms and diverse geometry at a fixed cost.
	/// The host's frame is authoritative: only clients correct.
	/// </summary>
	public class DriftCorrector : MonoBehaviour
	{
		[SerializeField] private float solveInterval = 2f;
		[SerializeField] private float correctionLerp = 0.5f;

		[Header("Sampling")]
		// chunks per solve, chosen spread apart; each costs one readback
		[SerializeField] private int maxChunksPerSolve = 12;
		// total remote-mesh samples, spread across the chosen chunks
		[SerializeField] private int sourcePointBudget = 4096;
		[SerializeField] private int maxPointsPerChunk = 512;
		[SerializeField] private int minTotalPoints = 200;

		[Header("Solver")]
		[SerializeField] private int solverIterations = 10;
		// fraction of matches kept by residual; the rest are outliers
		[SerializeField] [Range(0f, 1f)] private float trimFraction = 0.75f;
		// normal agreement anneals from lenient (capture) to strict (fit)
		[SerializeField] [Range(0f, 1f)] private float minNormalAgreement = 0.5f;
		[SerializeField] [Range(0f, 1f)] private float finalNormalAgreement = 0.8f;

		[Header("Solution gates")] [SerializeField]
		private float minInlierFraction = 0.3f;

		[SerializeField] private float maxRmsMeters = 0.04f;
		[SerializeField] private float minHorizNormalEig = 0.05f;
		[SerializeField] private float minVertNormalFrac = 0.05f;
		[SerializeField] private float maxCorrectionMeters = 0.3f;
		[SerializeField] private float maxCorrectionDegrees = 3f;

		[Header("Correction deadband")] [SerializeField]
		private float minCorrectionMeters = 0.005f;

		[SerializeField] private float minCorrectionDegrees = 0.05f;

		// chunk indices that have received a remote mesh
		private readonly HashSet<int> remoteChunks = new();
		private readonly List<int> candidates = new();
		private readonly List<int> eligible = new();
		private readonly List<int> usedChunks = new();
		private readonly List<float3> sourcePositions = new();
		private readonly List<float3> sourceNormals = new();

		// grow-only scratch buffers for alloc-free mesh reads
		private NativeArray<Vector3> vertBuffer;
		private NativeArray<Vector3> normBuffer;

		private EnvScanner.ChunkReadbackBuffer readbackBuffer;
		private CancellationTokenSource loopCancelSrc;

		private void Start()
		{
			readbackBuffer = EnvScanner.Instance.CreateChunkReadbackBuffer();

			EnvChunkSync.RemoteMeshApplied += OnRemoteMeshApplied;
			EnvScanner.Instance.Cleared += OnScanCleared;

			SolveLoop();
		}

		private void OnDestroy()
		{
			loopCancelSrc?.Cancel();

			EnvChunkSync.RemoteMeshApplied -= OnRemoteMeshApplied;
			if (EnvScanner.Instance)
				EnvScanner.Instance.Cleared -= OnScanCleared;

			if (vertBuffer.IsCreated) vertBuffer.Dispose();
			if (normBuffer.IsCreated) normBuffer.Dispose();

			if (readbackBuffer.gpuBuffer != null)
				readbackBuffer.Dispose();
		}

		private static bool IsAuthority()
		{
			NetworkManager net = NetworkManager.Singleton;
			return net == null || !net.IsConnectedClient || net.IsServer;
		}

		private void OnRemoteMeshApplied(Chunk chunk)
		{
			remoteChunks.Add(chunk.chunkIndex);
		}

		private void OnScanCleared()
		{
			remoteChunks.Clear();
		}

		private async void SolveLoop()
		{
			loopCancelSrc?.Cancel();
			loopCancelSrc = new CancellationTokenSource();
			CancellationToken ctkn = loopCancelSrc.Token;

			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.WaitForSecondsAsync(solveInterval, ctkn);
					ctkn.ThrowIfCancellationRequested();

					if (!ColocationManager.IsColocated || IsAuthority())
						continue;

					await TrySolve(ctkn);
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private async Awaitable TrySolve(CancellationToken ctkn)
		{
			EnvScanner scanner = EnvScanner.Instance;

			// eligible chunks have a remote mesh AND locally scanned data.
			// a local mesh implies the chunk's TSDF volume is populated
			candidates.Clear();
			candidates.AddRange(remoteChunks);
			eligible.Clear();

			foreach (int chunkIndex in candidates)
			{
				Chunk chunk = ChunkManager.Instance.GetOrCreateChunk(chunkIndex);

				if (!chunk.HasRemoteMesh)
				{
					// released since registration
					remoteChunks.Remove(chunkIndex);
					continue;
				}

				if (!chunk.mesh || chunk.mesh.vertexCount < 3) continue;

				eligible.Add(chunkIndex);
			}

			if (eligible.Count == 0) return;

			SelectSpreadChunks(scanner);

			// sample remote meshes synchronously, before any awaits
			int srcPerChunk = Mathf.Clamp(sourcePointBudget / usedChunks.Count, 8, maxPointsPerChunk);

			sourcePositions.Clear();
			sourceNormals.Clear();

			foreach (int chunkIndex in usedChunks)
			{
				Chunk chunk = ChunkManager.Instance.GetOrCreateChunk(chunkIndex);
				SampleMesh(chunk.RemoteMesh, chunk.transform.position, srcPerChunk,
					sourcePositions, sourceNormals);
			}

			// not enough overlap yet; try again next cycle
			if (sourcePositions.Count < minTotalPoints)
				return;

			int sourceCount = sourcePositions.Count;
			int vpcd = scanner.VoxPerChunkDim;
			int voxPerChunk = vpcd * vpcd * vpcd;

			// cleared on allocation: slots whose readback fails stay zero,
			// which the solver's gradient check rejects
			NativeArray<sbyte> volumes = new(usedChunks.Count * voxPerChunk, Allocator.Persistent);
			NativeArray<float3> corners = new(usedChunks.Count, Allocator.Persistent);
			NativeArray<float3> srcPoints = new(sourceCount, Allocator.Persistent);
			NativeArray<float3> srcNormals = new(sourceCount, Allocator.Persistent);
			NativeArray<float4x4> result = new(1, Allocator.Persistent);
			NativeArray<float> stats = new(TsdfAlignment.StatCount, Allocator.Persistent);

			JobHandle jobHandle = default;

			try
			{
				for (int i = 0; i < sourceCount; i++)
				{
					srcPoints[i] = sourcePositions[i];
					srcNormals[i] = sourceNormals[i];
				}

				for (int slot = 0; slot < usedChunks.Count; slot++)
				{
					int chunkIndex = usedChunks[slot];
					corners[slot] = scanner.ChunkCoordToCornerWorldPos(
						scanner.ChunkIndexToChunkCoord(chunkIndex));

					bool success = await scanner.ReadbackChunkInto(chunkIndex, readbackBuffer);
					ctkn.ThrowIfCancellationRequested();
					if (!success) continue;

					readbackBuffer.data.Reinterpret<sbyte>(1)
						.CopyTo(volumes.GetSubArray(slot * voxPerChunk, voxPerChunk));
				}

				TsdfAlignment.AlignJob job = new()
				{
					volumes = volumes,
					chunkCorners = corners,
					points = srcPoints,
					normals = srcNormals,
					voxPerChunkDim = vpcd,
					voxSize = scanner.VoxSize,
					truncationBand = scanner.DistanceTruncationBand,
					iterations = solverIterations,
					maxBandFrac = 0.95f,
					minNormalAgreement = minNormalAgreement,
					finalNormalAgreement = finalNormalAgreement,
					trimFraction = trimFraction,
					result = result,
					stats = stats
				};

				jobHandle = job.Schedule();
				while (!jobHandle.IsCompleted)
					await Awaitable.NextFrameAsync(ctkn);
				jobHandle.Complete();

				EvaluateAndApply(result[0], stats);
			}
			finally
			{
				jobHandle.Complete();

				volumes.Dispose();
				corners.Dispose();
				srcPoints.Dispose();
				srcNormals.Dispose();
				result.Dispose();
				stats.Dispose();
			}
		}

		// greedy farthest-point selection spreads chunks across the map
		// for long lever arms and diverse geometry
		private void SelectSpreadChunks(EnvScanner scanner)
		{
			usedChunks.Clear();

			if (eligible.Count <= maxChunksPerSolve)
			{
				usedChunks.AddRange(eligible);
				return;
			}

			usedChunks.Add(eligible[0]);

			while (usedChunks.Count < maxChunksPerSolve)
			{
				int best = -1;
				float bestMinDistSqr = -1f;

				foreach (int candidate in eligible)
				{
					if (usedChunks.Contains(candidate)) continue;

					float3 corner = scanner.ChunkCoordToCornerWorldPos(
						scanner.ChunkIndexToChunkCoord(candidate));

					float minDistSqr = float.MaxValue;
					foreach (int used in usedChunks)
					{
						float3 usedCorner = scanner.ChunkCoordToCornerWorldPos(
							scanner.ChunkIndexToChunkCoord(used));
						minDistSqr = math.min(minDistSqr, math.distancesq(corner, usedCorner));
					}

					if (minDistSqr > bestMinDistSqr)
					{
						bestMinDistSqr = minDistSqr;
						best = candidate;
					}
				}

				usedChunks.Add(best);
			}
		}

		private bool SampleMesh(Mesh mesh, float3 corner, int sampleCount,
			List<float3> positions, List<float3> normals)
		{
			if (!mesh) return false;

			int vertCount = mesh.vertexCount;
			if (vertCount < 3) return false;

			using Mesh.MeshDataArray dataArray = Mesh.AcquireReadOnlyMeshData(mesh);
			Mesh.MeshData data = dataArray[0];

			if (!data.HasVertexAttribute(VertexAttribute.Normal))
				return false;

			EnsureBuffers(vertCount);
			NativeArray<Vector3> verts = vertBuffer.GetSubArray(0, vertCount);
			NativeArray<Vector3> norms = normBuffer.GetSubArray(0, vertCount);
			data.GetVertices(verts);
			data.GetNormals(norms);

			int count = Mathf.Min(sampleCount, vertCount);
			float step = vertCount / (float)count;

			for (int i = 0; i < count; i++)
			{
				int src = (int)(i * step);
				positions.Add(corner + (float3)verts[src]);
				normals.Add(math.normalizesafe(norms[src], math.up()));
			}

			return true;
		}

		private void EnsureBuffers(int count)
		{
			if (vertBuffer.IsCreated && vertBuffer.Length >= count) return;

			if (vertBuffer.IsCreated)
			{
				vertBuffer.Dispose();
				normBuffer.Dispose();
			}

			int size = Mathf.NextPowerOfTwo(count);
			vertBuffer = new NativeArray<Vector3>(size, Allocator.Persistent);
			normBuffer = new NativeArray<Vector3>(size, Allocator.Persistent);
		}

		private void EvaluateAndApply(float4x4 delta, NativeArray<float> stats)
		{
			float inlierFrac = stats[TsdfAlignment.StatInlierFrac];
			float rms = stats[TsdfAlignment.StatRmsMeters];
			float horizEig = stats[TsdfAlignment.StatHorizNormalMinEig];
			float vertFrac = stats[TsdfAlignment.StatVertNormalFrac];

			float3 trans = new(
				stats[TsdfAlignment.StatTransX],
				stats[TsdfAlignment.StatTransY],
				stats[TsdfAlignment.StatTransZ]);

			float transMag = math.length(trans);
			float yawDeg = math.degrees(math.abs(stats[TsdfAlignment.StatYawRad]));

			bool wellConstrained =
				inlierFrac >= minInlierFraction &&
				rms <= maxRmsMeters &&
				horizEig >= minHorizNormalEig &&
				vertFrac >= minVertNormalFrac;

			bool plausible =
				transMag <= maxCorrectionMeters &&
				yawDeg <= maxCorrectionDegrees;

			LogDebug($"solve: trans {transMag * 1000f:F1}mm yaw {yawDeg:F3}deg " +
			         $"inliers {inlierFrac:P0} rms {rms * 1000f:F1}mm " +
			         $"horizEig {horizEig:F3} vertFrac {vertFrac:F3} " +
			         $"constrained {wellConstrained} plausible {plausible}");

			if (!wellConstrained || !plausible)
				return;

			if (transMag < minCorrectionMeters && yawDeg < minCorrectionDegrees)
				return;

			// delta maps remote (shared-frame) points onto local drifted
			// surfaces, so the tracking space moves by its inverse. already
			// scanned chunks are world-anchored, so rescanning through the
			// corrected rig converges the local map onto the remote map.
			// in-editor, SimEnvironmentRigFollower keeps the simulated
			// environment anchored to the tracking space through this move
			MainXRRig.Instance.AlignSpace(delta, Matrix4x4.identity, correctionLerp);

			LogDebug($"applied correction: {transMag * 1000f:F1}mm {yawDeg:F3}deg");
		}

		private static void LogDebug(string str)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.unityLogger.Log($"[{nameof(DriftCorrector)}] {str}");
#endif
		}
	}
}
