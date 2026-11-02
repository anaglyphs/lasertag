using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.Debugging;
using Meshia.MeshSimplification;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace Anaglyph.XR.DepthKit.EnvScanning
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

		[FormerlySerializedAs("meshSweptVoxelsThreshold")]
		[SerializeField] private int meshFlippedVoxelsThreshold = 10;

		// how far a voxel's distance must move past zero before Chunk.voxelSignBits accepts
		// that the surface crossed it. sized to swallow depth sensor noise, not real motion
		[SerializeField] private float voxelSignDeadbandMeters = 0.04f;

		// the GPU change sum moves with every step of sensor noise, so nearly every visible
		// chunk "changes" each scan. a chunk is only read back once its sum has moved this
		// far since it was last checked, or once recheckSeconds have passed with any change,
		// so small changes that add up are still meshed eventually
		[SerializeField] private uint minChangeSumToCheck = 512;
		[SerializeField] private float recheckSeconds = 2f;

		// a queued chunk waiting this long is taken before more changed ones, so the chunks
		// in view, re-queued with big changes every scan, can't starve the rest
		[SerializeField] private float maxQueueWaitSeconds = 1f;

		[SerializeField] private UniversalRendererData rendererData;

		// hide, per camera, the chunks the scanned surfaces hide from it; see ChunkOcclusionCuller
		[SerializeField] private bool occlusionCulling = true;
		// meters the culling frustum is pushed out by, so it never falls inside what is drawn
		[SerializeField] private float occlusionFrustumMargin = 0.25f;
		private ChunkOcclusionCuller occlusionCuller;
		public ChunkOcclusionCuller OcclusionCuller => occlusionCuller;

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
		/// Invoked when a chunk's mesh has finished meshing, decimating and cooking, and
		/// <see cref="Chunk.mesh"/> is the new one. <see cref="Chunk.voxelSignBits"/> is up to
		/// date when this fires.
		/// </summary>
		public event Action<Chunk> ChunkMeshUpdated = delegate { };

		private readonly Dictionary<int, Chunk> chunks = new();
		private readonly List<Chunk> chunksList = new();
		public IReadOnlyList<Chunk> ChunksList => chunksList;
		// the workers run on the main thread between awaits, so a plain list will do
		private readonly List<Chunk> meshQueue = new();
		// chunks with change visibility saw but didn't queue: below the threshold, or busy.
		// visibility only reports chunks in view, so without this a chunk left behind keeps
		// the change unmeshed until it is seen again
		private readonly List<Chunk> recheckList = new();
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

		private static readonly int debugMeshOpacityID = Shader.PropertyToID("agDebugMeshOpacity");
		private static float debugMeshOpacity = 0.2f;

		/// <summary>
		/// Passthrough compositor alpha of the debug meshes (the chunks and anything else
		/// drawn with the debug mesh shaders). They stay opaque in-scene; this only sets
		/// how much passthrough shows beneath them.
		/// </summary>
		public static float DebugMeshOpacity
		{
			get => debugMeshOpacity;
			set
			{
				debugMeshOpacity = Mathf.Clamp01(value);
				Shader.SetGlobalFloat(debugMeshOpacityID, debugMeshOpacity);
			}
		}

		// globals do not survive entering play mode, so it is pushed again each run
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void InitDebugMeshOpacity() => DebugMeshOpacity = debugMeshOpacity;

		/// <summary>Whether the chunk meshes are drawn (the debug mesh draw mode).</summary>
		public bool ChunksVisible { get; private set; }
		public event Action<bool> ChunksVisibleChanged = delegate { };

		public void SetChunksVisible(bool visible)
		{
			bool changed = ChunksVisible != visible;
			ChunksVisible = visible;
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

			if (changed)
			{
				ChunksVisibleChanged.Invoke(visible);
			}
		}

		private void OnEnable()
		{
			if (didStart) Begin();
		}

		private void Update()
		{
			RecheckHeldChunks();

			if (!AnaglyphDebugging.DebugMode) return;
			foreach (Chunk chunk in chunksList)
				chunk.DrawDebug();
		}

		private void HoldForRecheck(Chunk chunk, uint changeSum)
		{
			chunk.seenChangeSum = changeSum;
			if (chunk.awaitingRecheck) return;
			chunk.awaitingRecheck = true;
			recheckList.Add(chunk);
		}

		private void RecheckHeldChunks()
		{
			float now = Time.unscaledTime;
			for (int i = recheckList.Count - 1; i >= 0; i--)
			{
				Chunk chunk = recheckList[i];

				// a worker has it; what it read may predate the change seen, so wait for its commit
				if (chunk && chunk.dirty) continue;

				if (chunk && chunk.seenChangeSum != chunk.lastMeshingChangeSum)
				{
					if (now - chunk.lastQueuedTime < recheckSeconds) continue;
					QueueForMeshing(chunk, chunk.seenChangeSum,
						unchecked(chunk.seenChangeSum - chunk.lastMeshingChangeSum));
				}

				if (chunk) chunk.awaitingRecheck = false;
				recheckList[i] = recheckList[^1];
				recheckList.RemoveAt(recheckList.Count - 1);
			}
		}

		private void Begin()
		{
			EnvScanner.Instance.Updated += OnScanUpdate;
			EnvScanner.Instance.ChunkUploaded += OnChunkUploaded;

			if (occlusionCulling)
				occlusionCuller ??= new ChunkOcclusionCuller(this, envMeshLayerMask, occlusionFrustumMargin);

			StartWorkers();
		}

		private void OnDisable()
		{
			workerCancelSrc?.Cancel();

			occlusionCuller?.Dispose();
			occlusionCuller = null;

			if (EnvScanner.Instance)
			{
				EnvScanner.Instance.Updated -= OnScanUpdate;
				EnvScanner.Instance.ChunkUploaded -= OnChunkUploaded;
			}
		}

		/// <summary>
		/// A chunk fused in whole never passes through visibility, so it is queued for
		/// meshing here, ahead of everything else: it is map the headset has never shown.
		/// The work token is synthetic; the worker records the odometer the chunk's data
		/// was read with, so visibility doesn't queue it again for the same change.
		/// </summary>
		private void OnChunkUploaded(int chunkIndex)
		{
			Chunk chunk = GetOrCreateChunk(chunkIndex);
			if (chunk.dirty) return;

			QueueForMeshing(chunk, unchecked(chunk.lastMeshingChangeSum + 1), uint.MaxValue);
		}

		private void QueueForMeshing(Chunk chunk, uint workToken, uint priority)
		{
			chunk.pendingMeshingChangeSum = workToken;
			chunk.meshPriority = priority;
			chunk.dirty = true;
			chunk.lastQueuedTime = Time.unscaledTime;
			meshQueue.Add(chunk);
			mesherSemaphore.Release();
		}

		// the chunk whose odometer moved the most: a moved wall ahead of noise. once anything
		// has waited too long, the one that waited longest
		private bool TryTakeMostChanged(out Chunk chunk)
		{
			chunk = null;
			int best = -1, oldest = -1;
			for (int i = 0; i < meshQueue.Count; i++)
			{
				if (best < 0 || meshQueue[i].meshPriority > meshQueue[best].meshPriority)
					best = i;
				if (oldest < 0 || meshQueue[i].lastQueuedTime < meshQueue[oldest].lastQueuedTime)
					oldest = i;
			}

			if (best < 0) return false;

			if (Time.unscaledTime - meshQueue[oldest].lastQueuedTime > maxQueueWaitSeconds)
				best = oldest;

			chunk = meshQueue[best];
			meshQueue[best] = meshQueue[^1];
			meshQueue.RemoveAt(meshQueue.Count - 1);
			return true;
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
			chunksList.Clear();
			meshQueue.Clear();
			recheckList.Clear();

			if (enabled)
				StartWorkers();
		}

		// frames (local and remote) can land faster than a readback round-trips. one
		// missed update is caught up after the readback in flight, so a chunk only a
		// remote frame touched still gets meshed
		private bool updatePending;

		private async void OnScanUpdate()
		{
			if (busy)
			{
				updatePending = true;
				return;
			}

			busy = true;
			CancellationToken ctkn = workerCancelSrc.Token;

			EnvScanner scanner = EnvScanner.Instance;

			try
			{
				EnvScanner.VisibleChunksReadbackResult visResult = await scanner.ReadbackVisibleChunks();
				ctkn.ThrowIfCancellationRequested();

				if (!visResult.valid) return;

				float now = Time.unscaledTime;
				for (int i = 0; i < visResult.count; i++)
				{
					int chunkIndex = visResult.visibleChunks[i];

					Chunk chunk = GetOrCreateChunk(chunkIndex);

					uint changeSum = visResult.changeSums[i];

					// The GPU change sum is only a cheap dirty signal. The worker decides whether
					// to remesh by comparing voxel signs against the last completed mesh.
					if (changeSum == chunk.lastMeshingChangeSum) continue;

					// the odometer wraps; unsigned subtraction still gives the distance moved
					uint changed = unchecked(changeSum - chunk.lastMeshingChangeSum);
					if (chunk.dirty || (changed < minChangeSumToCheck && now - chunk.lastQueuedTime < recheckSeconds))
					{
						HoldForRecheck(chunk, changeSum);
						continue;
					}

					QueueForMeshing(chunk, changeSum, changed);
				}
			}
			catch (OperationCanceledException)
			{
				updatePending = false;
			}
			finally
			{
				busy = false;
			}

			if (updatePending && this && enabled)
			{
				updatePending = false;
				OnScanUpdate();
			}
		}

		public bool TryGetChunk(int chunkIndex, out Chunk chunk)
		{
			return chunks.TryGetValue(chunkIndex, out chunk);
		}

		public Chunk GetOrCreateChunk(int chunkIndex)
		{
			if (TryGetChunk(chunkIndex, out Chunk chunk))
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
			chunksList.Add(chunk);

			return chunk;
		}

		private void StartWorkers()
		{
			workerCancelSrc?.Cancel();
			workerCancelSrc = new CancellationTokenSource();

			for (int i = 0; i < numMeshWorkers; i++)
				_ = RunMesherWorker(workerCancelSrc.Token);
		}

		private bool IsCurrentChunkWork(Chunk chunk, uint pendingChangeSum)
		{
			return chunk != null &&
			       chunks.TryGetValue(chunk.chunkIndex, out Chunk currentChunk) &&
			       ReferenceEquals(currentChunk, chunk) &&
			       chunk.dirty &&
			       chunk.pendingMeshingChangeSum == pendingChangeSum;
		}

		// meshedChangeSum: the odometer the meshed data was read with. Change after it isn't
		// in the mesh, so visibility queues the chunk again for it
		private bool CommitChunkWork(Chunk chunk, uint pendingChangeSum, uint meshedChangeSum)
		{
			if (!IsCurrentChunkWork(chunk, pendingChangeSum)) return false;

			chunk.lastMeshingChangeSum = meshedChangeSum;
			chunk.dirty = false;
			return true;
		}

		private void AbortChunkWork(Chunk chunk, uint pendingChangeSum)
		{
			if (!IsCurrentChunkWork(chunk, pendingChangeSum)) return;

			// The completed change sum stays untouched, so the next visibility readback
			// schedules this chunk again even if the GPU signal has not changed.
			chunk.pendingMeshingChangeSum = chunk.lastMeshingChangeSum;
			chunk.dirty = false;
		}

		private static void UpdateVoxelSignBits(NativeArray<EnvScanner.Voxel> voxels, sbyte deadband,
			NativeArray<uint> previousSignBits, NativeArray<uint> updatedSignBits)
		{
			for (int i = 0; i < updatedSignBits.Length; i++)
				updatedSignBits[i] = previousSignBits.IsCreated ? previousSignBits[i] : 0u;

			new VoxelSignBitsJob
			{
				voxels = voxels,
				deadband = deadband,
				signBits = updatedSignBits
			}.Run();
		}

		private static int CountFlippedVoxels(NativeArray<uint> previousSignBits,
			NativeArray<uint> updatedSignBits)
		{
			int flippedVoxels = 0;

			for (int i = 0; i < updatedSignBits.Length; i++)
			{
				uint previousBits = previousSignBits.IsCreated ? previousSignBits[i] : 0u;
				flippedVoxels += math.countbits(updatedSignBits[i] ^ previousBits);
			}

			return flippedVoxels;
		}

		// portalComponents < 0: the portals weren't found, so the chunk is taken as open
		private static void RememberMeshedSurface(Chunk chunk, NativeArray<uint> updatedSignBits,
			NativeArray<uint> portalMasks, int portalComponents)
		{
			if (!chunk.voxelSignBits.IsCreated)
				chunk.voxelSignBits = new NativeArray<uint>(updatedSignBits.Length, Allocator.Persistent);

			chunk.voxelSignBits.CopyFrom(updatedSignBits);
			chunk.portalRevision++;

			if (portalComponents < 0)
			{
				chunk.portalMasks = ChunkPortals.Open;
				chunk.portalComponents = 1;
				return;
			}

			int words = portalComponents * ChunkPortals.ComponentWords;
			if (chunk.portalMasks == ChunkPortals.Open || chunk.portalMasks.Length != words)
				chunk.portalMasks = new uint[words];
			NativeArray<uint>.Copy(portalMasks, chunk.portalMasks, words);
			chunk.portalComponents = portalComponents;
		}

		private const int VoxelsPerSignWord = 32;

		/// <summary>
		/// Schmitt trigger per voxel: a bit only sets once the voxel reads solidly behind the
		/// surface and only clears once it reads solidly in front, so values hovering around
		/// zero — every voxel the surface actually passes through — hold their last state
		/// </summary>
		[BurstCompile]
		private struct VoxelSignBitsJob : IJob
		{
			[ReadOnly] public NativeArray<EnvScanner.Voxel> voxels;
			public sbyte deadband;
			public NativeArray<uint> signBits;

			public void Execute()
			{
				for (int word = 0; word < signBits.Length; word++)
				{
					uint bits = signBits[word];
					int voxelBase = word * VoxelsPerSignWord;

					for (int bit = 0; bit < VoxelsPerSignWord; bit++)
					{
						sbyte value = voxels[voxelBase + bit].value;

						if (value < -deadband)
							bits |= 1u << bit;
						else if (value > deadband)
							bits &= ~(1u << bit);
					}

					signBits[word] = bits;
				}
			}
		}

		private struct BakeColliderJob : IJob
		{
			public EntityId mesh;
			public MeshColliderCookingOptions options;

			public void Execute() => Physics.BakeMesh(mesh, false, options);
		}

		// jobs are left to finish whatever happens: they read buffers the worker disposes
		private static async Awaitable Finish(JobHandle handle)
		{
			JobHandle.ScheduleBatchedJobs();
			// ReSharper disable once MethodSupportsCancellation
			while (!handle.IsCompleted) await Awaitable.NextFrameAsync();
			handle.Complete();
		}

		/// <summary>
		/// Readback → surface nets → decimation → collider cook, each stage a chain of jobs
		/// awaited by polling, so the main thread only schedules, applies the decimated
		/// mesh and swaps it in
		/// </summary>
		private async Task RunMesherWorker(CancellationToken ctkn)
		{
			EnvScanner scanner = EnvScanner.Instance;

			int vpcd = scanner.VoxPerChunkDim;
			int3 chunkSize = new(vpcd, vpcd, vpcd);

			NetMesher mesher = new();

			sbyte signBitDeadband = (sbyte)math.clamp(
				voxelSignDeadbandMeters / scanner.DistanceTruncationBand * sbyte.MaxValue, 1, sbyte.MaxValue - 1);

			EnvScanner.ChunkReadbackBuffer readbackBuffer = scanner.CreateChunkReadbackBuffer();
			NativeArray<uint> updatedSignBits = new(readbackBuffer.data.Length / VoxelsPerSignWord,
				Allocator.Persistent);
			NativeArray<uint> portalMasks = new(ChunkPortals.MaxComponents * ChunkPortals.ComponentWords,
				Allocator.Persistent);
			NativeArray<int> portalComponents = new(1, Allocator.Persistent);

			// the chunk meshes have none, but the simplifier's API takes them
			NativeList<BlendShapeData> noBlendShapes = new(Allocator.Persistent);
			NativeList<BlendShapeData> simplifiedBlendShapes = new(Allocator.Persistent);

			Chunk activeChunk = null;
			uint activePendingChangeSum = 0;
			// completed before anything it reads is disposed
			JobHandle inFlight = default;
			Mesh.MeshDataArray surface = default, simplified = default;
			bool surfaceAllocated = false, simplifiedAllocated = false;

			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await mesherSemaphore.WaitAsync(ctkn);

					if (!TryTakeMostChanged(out activeChunk))
						continue;

					activePendingChangeSum = activeChunk.pendingMeshingChangeSum;

					// Clear() may have removed this queue entry while a worker was waking.
					if (!IsCurrentChunkWork(activeChunk, activePendingChangeSum))
					{
						activeChunk = null;
						continue;
					}

					bool readbackSuccess = await scanner.ReadbackChunkInto(activeChunk.chunkIndex, readbackBuffer);

					if (!readbackSuccess)
					{
						AbortChunkWork(activeChunk, activePendingChangeSum);
						activeChunk = null;
						continue;
					}

					ctkn.ThrowIfCancellationRequested();

					uint meshedChangeSum = readbackBuffer.ChangeSum;

					UpdateVoxelSignBits(readbackBuffer.data, signBitDeadband, activeChunk.voxelSignBits,
						updatedSignBits);

					int flippedVoxels = CountFlippedVoxels(activeChunk.voxelSignBits, updatedSignBits);
					if (flippedVoxels < meshFlippedVoxelsThreshold)
					{
						CommitChunkWork(activeChunk, activePendingChangeSum, meshedChangeSum);
						activeChunk = null;
						continue;
					}

					surface = Mesh.AllocateWritableMeshData(1);
					surfaceAllocated = true;
					inFlight = mesher.Schedule(readbackBuffer.data, chunkSize, scanner.VoxSize, surface[0]);
					// the portals only feed the culler
					bool findPortals = occlusionCulling;
					if (findPortals)
					{
						JobHandle portalsJob = new ChunkPortals.ComponentsJob
						{
							solidBits = updatedSignBits,
							masks = portalMasks,
							count = portalComponents
						}.Schedule();
						inFlight = JobHandle.CombineDependencies(inFlight, portalsJob);
					}
					await Finish(inFlight);
					ctkn.ThrowIfCancellationRequested();

					Mesh target = activeChunk.backMesh;
					bool isPopulated = mesher.IndexCount > 0;

					if (isPopulated)
					{
						simplified = Mesh.AllocateWritableMeshData(1);
						simplifiedAllocated = true;
						simplifiedBlendShapes.Clear();

						MeshSimplifier simplifier = new(Allocator.Persistent);
						JobHandle load = simplifier.ScheduleLoadMeshData(surface[0], decimationOptions);
						JobHandle simplify = simplifier.ScheduleSimplify(surface[0], noBlendShapes, decimationTarget, load);
						JobHandle write = simplifier.ScheduleWriteMeshData(surface[0], noBlendShapes, simplified[0],
							simplifiedBlendShapes, simplify);
						inFlight = simplifier.Dispose(write);
						await Finish(inFlight);
						ctkn.ThrowIfCancellationRequested();

						simplifiedAllocated = false;
						Mesh.ApplyAndDisposeWritableMeshData(simplified, target,
							MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
						isPopulated = target.vertexCount > 0;
						// the submesh carries the surface's bounds, computed in its job; the
						// mesh's own bounds aren't derived from them
						if (isPopulated) target.bounds = target.GetSubMesh(0).bounds;
					}

					surfaceAllocated = false;
					surface.Dispose();

					if (isPopulated)
					{
						// cooked here rather than on the main thread when the collider takes it
						activeChunk.colliderBake = new BakeColliderJob
						{
							mesh = target.GetEntityId(),
							options = activeChunk.meshCollider.cookingOptions
						}.Schedule();
						inFlight = activeChunk.colliderBake;
						await Finish(inFlight);
						ctkn.ThrowIfCancellationRequested();
					}
					else target.Clear();

					RememberMeshedSurface(activeChunk, updatedSignBits, portalMasks,
						findPortals ? portalComponents[0] : -1);

					if (CommitChunkWork(activeChunk, activePendingChangeSum, meshedChangeSum))
					{
						activeChunk.Present(isPopulated);
						ChunkMeshUpdated.Invoke(activeChunk);
					}

					activeChunk = null;
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			finally
			{
				inFlight.Complete();

				if (activeChunk != null)
					AbortChunkWork(activeChunk, activePendingChangeSum);

				if (surfaceAllocated) surface.Dispose();
				if (simplifiedAllocated) simplified.Dispose();
				mesher.Dispose();
				readbackBuffer.Dispose();
				updatedSignBits.Dispose();
				portalMasks.Dispose();
				portalComponents.Dispose();
				noBlendShapes.Dispose();
				simplifiedBlendShapes.Dispose();
			}
		}
	}
}
