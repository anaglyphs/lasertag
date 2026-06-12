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
using UnityEngine.SceneManagement;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Mitigates tracking drift between colocated headsets. Samples chunk
	/// meshes received from other players, aligns them against the local
	/// TSDF volume with point-to-TSDF ICP, and nudges the tracking space
	/// by the resulting delta. The host's frame is authoritative: only
	/// clients correct, toward the meshes they receive.
	/// </summary>
	public class DriftCorrector : MonoBehaviour
	{
		[SerializeField] private float solveInterval = 2f;
		[SerializeField] private float correctionLerp = 0.5f;
		[SerializeField] private int maxChunksPerSolve = 4;
		[SerializeField] private int maxPointsPerChunk = 512;
		[SerializeField] private int minTotalPoints = 200;
		[SerializeField] private int solverIterations = 8;

		// local TSDF data scanned longer ago than this may predate recent drift
		[SerializeField] private float maxLocalScanAge = 10f;
		[SerializeField] private float maxRemoteSampleAge = 30f;

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

		private class RemoteChunkSamples
		{
			public float3 chunkCorner;
			public float3[] positions; // chunk-local
			public float3[] normals;
			public float receivedTime;
		}

		private readonly Dictionary<int, RemoteChunkSamples> remoteSamples = new();
		private readonly Dictionary<int, float> localScanTimes = new();
		private readonly List<int> candidates = new();
		private readonly List<int> usedChunks = new();
		private readonly List<RemoteChunkSamples> usedSamples = new();

		private EnvScanner.ChunkReadbackBuffer readbackBuffer;
		private CancellationTokenSource loopCancelSrc;

		private void Start()
		{
			readbackBuffer = EnvScanner.Instance.CreateChunkReadbackBuffer();

			EnvChunkSync.RemoteMeshApplied += OnRemoteMeshApplied;
			ChunkManager.Instance.VisibleChunkPolled += OnVisibleChunkPolled;
			EnvScanner.Instance.Cleared += OnScanCleared;

			SolveLoop();
		}

		private void OnDestroy()
		{
			loopCancelSrc?.Cancel();

			EnvChunkSync.RemoteMeshApplied -= OnRemoteMeshApplied;
			if (ChunkManager.Instance)
				ChunkManager.Instance.VisibleChunkPolled -= OnVisibleChunkPolled;
			if (EnvScanner.Instance)
				EnvScanner.Instance.Cleared -= OnScanCleared;

			if (readbackBuffer.gpuBuffer != null)
				readbackBuffer.Dispose();
		}

		private static bool IsAuthority()
		{
			NetworkManager net = NetworkManager.Singleton;
			return net == null || !net.IsConnectedClient || net.IsServer;
		}

		private void OnVisibleChunkPolled(int chunkIndex, uint changeSum)
		{
			localScanTimes[chunkIndex] = Time.time;
		}

		private void OnScanCleared()
		{
			localScanTimes.Clear();
			remoteSamples.Clear();
		}

		private void OnRemoteMeshApplied(Chunk chunk)
		{
			if (IsAuthority()) return;
			if (!chunk.HasRemoteMesh) return;

			Mesh mesh = chunk.RemoteMesh;
			Vector3[] verts = mesh.vertices;
			Vector3[] norms = mesh.normals;

			if (verts.Length < 3 || norms.Length != verts.Length)
				return;

			int count = Mathf.Min(maxPointsPerChunk, verts.Length);
			float step = verts.Length / (float)count;

			float3[] positions = new float3[count];
			float3[] normals = new float3[count];

			for (int i = 0; i < count; i++)
			{
				int src = (int)(i * step);
				positions[i] = verts[src];
				normals[i] = math.normalizesafe(norms[src], math.up());
			}

			remoteSamples[chunk.chunkIndex] = new RemoteChunkSamples
			{
				chunkCorner = chunk.transform.position,
				positions = positions,
				normals = normals,
				receivedTime = Time.time
			};
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

					ExpireStaleSamples();

					if (!ColocationManager.IsColocated || IsAuthority())
						continue;

					await TrySolve(ctkn);
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void ExpireStaleSamples()
		{
			candidates.Clear();

			foreach (KeyValuePair<int, RemoteChunkSamples> pair in remoteSamples)
				if (Time.time - pair.Value.receivedTime > maxRemoteSampleAge)
					candidates.Add(pair.Key);

			foreach (int chunkIndex in candidates)
				remoteSamples.Remove(chunkIndex);
		}

		private async Awaitable TrySolve(CancellationToken ctkn)
		{
			EnvScanner scanner = EnvScanner.Instance;

			// candidate chunks have remote samples AND fresh local TSDF data
			candidates.Clear();
			foreach (KeyValuePair<int, RemoteChunkSamples> pair in remoteSamples)
				if (localScanTimes.TryGetValue(pair.Key, out float scanTime) &&
				    Time.time - scanTime <= maxLocalScanAge)
					candidates.Add(pair.Key);

			if (candidates.Count == 0) return;

			candidates.Sort((a, b) =>
				remoteSamples[b].receivedTime.CompareTo(remoteSamples[a].receivedTime));

			int vpcd = scanner.VoxPerChunkDim;
			int voxPerChunk = vpcd * vpcd * vpcd;

			NativeArray<sbyte> volumes = new(maxChunksPerSolve * voxPerChunk, Allocator.Persistent);
			NativeArray<float3> corners = new(maxChunksPerSolve, Allocator.Persistent);

			NativeArray<float3> points = default;
			NativeArray<float3> normals = default;
			NativeArray<int> pointSlots = default;
			NativeArray<float4x4> result = default;
			NativeArray<float> stats = default;

			JobHandle jobHandle = default;

			try
			{
				usedChunks.Clear();
				usedSamples.Clear();
				int totalPoints = 0;

				foreach (int chunkIndex in candidates)
				{
					if (usedChunks.Count == maxChunksPerSolve) break;

					// remoteSamples can be mutated during awaits; snapshot the
					// batch up front and tolerate entries disappearing
					if (!remoteSamples.TryGetValue(chunkIndex, out RemoteChunkSamples samples))
						continue;

					bool success = await scanner.ReadbackChunkInto(chunkIndex, readbackBuffer);
					ctkn.ThrowIfCancellationRequested();
					if (!success) continue;

					int slot = usedChunks.Count;
					readbackBuffer.data.Reinterpret<sbyte>(1)
						.CopyTo(volumes.GetSubArray(slot * voxPerChunk, voxPerChunk));
					corners[slot] = samples.chunkCorner;

					usedChunks.Add(chunkIndex);
					usedSamples.Add(samples);
					totalPoints += samples.positions.Length;
				}

				// not enough data yet; keep samples around for the next attempt
				if (totalPoints < minTotalPoints) return;

				points = new NativeArray<float3>(totalPoints, Allocator.Persistent);
				normals = new NativeArray<float3>(totalPoints, Allocator.Persistent);
				pointSlots = new NativeArray<int>(totalPoints, Allocator.Persistent);
				result = new NativeArray<float4x4>(1, Allocator.Persistent);
				stats = new NativeArray<float>(TsdfAlignment.StatCount, Allocator.Persistent);

				int p = 0;
				for (int slot = 0; slot < usedSamples.Count; slot++)
				{
					RemoteChunkSamples samples = usedSamples[slot];

					for (int i = 0; i < samples.positions.Length; i++)
					{
						points[p] = samples.chunkCorner + samples.positions[i];
						normals[p] = samples.normals[i];
						pointSlots[p] = slot;
						p++;
					}
				}

				TsdfAlignment.AlignJob job = new()
				{
					volumes = volumes,
					chunkCorners = corners,
					points = points,
					normals = normals,
					pointSlots = pointSlots,
					voxPerChunkDim = vpcd,
					voxSize = scanner.VoxSize,
					truncationBand = scanner.DistanceTruncationBand,
					iterations = solverIterations,
					maxBandFrac = 0.95f,
					minNormalAgreement = 0.5f,
					result = result,
					stats = stats
				};

				jobHandle = job.Schedule();
				while (!jobHandle.IsCompleted)
					await Awaitable.NextFrameAsync(ctkn);
				jobHandle.Complete();

				// consume the batches this solve used, but keep any fresher
				// batch that replaced one of them mid-solve
				for (int slot = 0; slot < usedChunks.Count; slot++)
					if (remoteSamples.TryGetValue(usedChunks[slot], out RemoteChunkSamples current) &&
					    ReferenceEquals(current, usedSamples[slot]))
						remoteSamples.Remove(usedChunks[slot]);

				EvaluateAndApply(result[0], stats);
			}
			finally
			{
				jobHandle.Complete();

				volumes.Dispose();
				corners.Dispose();
				if (points.IsCreated) points.Dispose();
				if (normals.IsCreated) normals.Dispose();
				if (pointSlots.IsCreated) pointSlots.Dispose();
				if (result.IsCreated) result.Dispose();
				if (stats.IsCreated) stats.Dispose();
			}
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
			// surfaces, so the tracking space moves by its inverse

#if UNITY_EDITOR
			// XR Simulation renders depth from the session-space device pose
			// while DepthKitDriver attributes it to the world-space camera, so
			// the simulated environment rides the tracking space and rig
			// corrections cancel themselves out. Move the environment instead.
			if (TryCorrectSimulatedEnvironment(delta))
			{
				LogDebug($"applied sim environment correction: {transMag * 1000f:F1}mm {yawDeg:F3}deg");
				return;
			}
#else
			MainXRRig.Instance.AlignSpace(delta, Matrix4x4.identity, correctionLerp);
			
			LogDebug($"applied correction: {transMag * 1000f:F1}mm {yawDeg:F3}deg");
#endif
		}

#if UNITY_EDITOR
		// runtime scene name used by ARFoundation's SimulationSceneManager
		private const string SimEnvironmentScenePrefix = "Simulated Environment Scene";

		private bool TryCorrectSimulatedEnvironment(float4x4 delta)
		{
			Scene envScene = default;

			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (scene.isLoaded && scene.name.StartsWith(SimEnvironmentScenePrefix))
				{
					envScene = scene;
					break;
				}
			}

			if (!envScene.IsValid())
				return false;

			// the same partial correction the tracking space would receive
			Matrix4x4 deltaInv = math.inverse(delta);
			Vector3 partialPos = Vector3.Lerp(Vector3.zero, deltaInv.GetPosition(), correctionLerp);
			Quaternion partialRot = Quaternion.Slerp(Quaternion.identity, deltaInv.rotation, correctionLerp);
			Matrix4x4 partial = Matrix4x4.TRS(partialPos, partialRot, Vector3.one);

			// the environment is perceived through the tracking space
			// transform, so conjugate the correction into session space
			Matrix4x4 space = MainXRRig.TrackingSpace.localToWorldMatrix;
			Matrix4x4 m = space.inverse * partial * space;

			foreach (GameObject root in envScene.GetRootGameObjects())
			{
				Transform t = root.transform;
				t.SetPositionAndRotation(
					m.MultiplyPoint3x4(t.position),
					m.rotation * t.rotation);
			}

			return true;
		}
#endif

		private static void LogDebug(string str)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.unityLogger.Log($"[{nameof(DriftCorrector)}] {str}");
#endif
		}
	}
}