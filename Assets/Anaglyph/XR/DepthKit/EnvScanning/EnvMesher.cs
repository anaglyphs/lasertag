using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Meshia.MeshSimplification;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
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
		/// Invoked when a chunk's mesh has finished meshing and decimating.
		/// <see cref="Chunk.voxelSignBits"/> is up to date when this fires.
		/// </summary>
		public event Action<Chunk> ChunkMeshUpdated = delegate { };

		private readonly Dictionary<int, Chunk> chunks = new();
		private readonly List<Chunk> chunksList = new();
		public IReadOnlyList<Chunk> ChunksList => chunksList;
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
				foreach (Chunk chunk in chunksList)
					if (chunk) chunk.RefreshDisplayMesh();
				ChunksVisibleChanged.Invoke(visible);
			}
		}

		private void OnEnable()
		{
			if (didStart) Begin();
		}

		private void Begin()
		{
			EnvScanner.Instance.Updated += OnScanUpdate;
			EnvScanner.Instance.ChunkUploaded += OnChunkUploaded;

			StartWorkers();
		}

		private void OnDisable()
		{
			workerCancelSrc?.Cancel();

			if (EnvScanner.Instance)
			{
				EnvScanner.Instance.Updated -= OnScanUpdate;
				EnvScanner.Instance.ChunkUploaded -= OnChunkUploaded;
			}
		}

		/// <summary>
		/// A chunk fused in whole never passes through visibility, so it is queued for
		/// meshing here. The work token is synthetic; the next visibility readback of the
		/// chunk re-queues it once more against the real odometer, which is harmless.
		/// </summary>
		private void OnChunkUploaded(int chunkIndex)
		{
			Chunk chunk = GetOrCreateChunk(chunkIndex);
			if (chunk.dirty) return;

			chunk.pendingMeshingChangeSum = unchecked(chunk.lastMeshingChangeSum + 1);
			chunk.dirty = true;
			meshQueue.Enqueue(chunk);
			mesherSemaphore.Release();
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

				for (int i = 0; i < visResult.count; i++)
				{
					int chunkIndex = visResult.visibleChunks[i];

					Chunk chunk = GetOrCreateChunk(chunkIndex);

					uint changeSum = visResult.changeSums[i];

					// The GPU change sum is only a cheap dirty signal. The worker decides whether
					// to remesh by comparing voxel signs against the last completed mesh.
					if (!chunk.dirty && changeSum != chunk.lastMeshingChangeSum)
					{
						chunk.pendingMeshingChangeSum = changeSum;
						chunk.dirty = true;
						meshQueue.Enqueue(chunk);
						mesherSemaphore.Release();
					}
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

		/// <summary>
		/// Re-meshes a chunk regardless of how few voxels changed sign, and puts it back
		/// on its grid pose when the new mesh lands. For chunks whose data a pose-graph
		/// solve moved: their mesh was shown moved by a transform until now
		/// </summary>
		public void ForceRemesh(int chunkIndex)
		{
			Chunk chunk = GetOrCreateChunk(chunkIndex);
			chunk.forceRemesh = true;
			if (chunk.dirty) return;

			chunk.pendingMeshingChangeSum = unchecked(chunk.lastMeshingChangeSum + 1);
			chunk.dirty = true;
			meshQueue.Enqueue(chunk);
			mesherSemaphore.Release();
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

		private bool CommitChunkWork(Chunk chunk, uint pendingChangeSum)
		{
			if (!IsCurrentChunkWork(chunk, pendingChangeSum)) return false;

			chunk.lastMeshingChangeSum = pendingChangeSum;
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

		private static void RememberMeshedSurface(Chunk chunk, NativeArray<uint> updatedSignBits)
		{
			if (!chunk.voxelSignBits.IsCreated)
				chunk.voxelSignBits = new NativeArray<uint>(updatedSignBits.Length, Allocator.Persistent);

			chunk.voxelSignBits.CopyFrom(updatedSignBits);
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

			// meshed into first, so the chunk mesh and collider only
			// update once, after decimation is done
			Mesh scratchMesh = new();
			scratchMesh.MarkDynamic();
			Chunk activeChunk = null;
			uint activePendingChangeSum = 0;

			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await mesherSemaphore.WaitAsync(ctkn);

					if (!meshQueue.TryDequeue(out activeChunk))
						continue;

					activePendingChangeSum = activeChunk.pendingMeshingChangeSum;

					// Clear() may have removed this queue entry while a worker was waking.
					if (!IsCurrentChunkWork(activeChunk, activePendingChangeSum))
					{
						activeChunk = null;
						continue;
					}

					bool force = activeChunk.forceRemesh;
					activeChunk.forceRemesh = false;

					bool readbackSuccess = await scanner.ReadbackChunkInto(activeChunk.chunkIndex, readbackBuffer);

					if (!readbackSuccess)
					{
						activeChunk.forceRemesh |= force;
						AbortChunkWork(activeChunk, activePendingChangeSum);
						activeChunk = null;
						continue;
					}

					ctkn.ThrowIfCancellationRequested();

					UpdateVoxelSignBits(readbackBuffer.data, signBitDeadband, activeChunk.voxelSignBits,
						updatedSignBits);

					int flippedVoxels = CountFlippedVoxels(activeChunk.voxelSignBits, updatedSignBits);
					if (!force && flippedVoxels < meshFlippedVoxelsThreshold)
					{
						CommitChunkWork(activeChunk, activePendingChangeSum);
						activeChunk = null;
						continue;
					}

					bool isPopulated = await mesher.CreateMesh(readbackBuffer.data, chunkSize, scanner.VoxSize,
						scratchMesh, ctkn);

					ctkn.ThrowIfCancellationRequested();

					if (isPopulated)
					{
						await MeshSimplifier.SimplifyAsync(scratchMesh, decimationTarget, decimationOptions,
							activeChunk.mesh, ctkn);

						ctkn.ThrowIfCancellationRequested();

						activeChunk.mesh.RecalculateBounds();
						activeChunk.meshIsPopulated = activeChunk.mesh.vertexCount > 0;

						// a MeshCollider only recooks when sharedMesh changes value, so editing
						// the mesh it already holds needs the reference cleared and reassigned
						activeChunk.meshCollider.sharedMesh = null;
						activeChunk.meshCollider.sharedMesh = activeChunk.mesh;
						activeChunk.meshCollider.enabled = activeChunk.meshIsPopulated;
					}
					else
					{
						activeChunk.mesh.Clear();
						activeChunk.meshIsPopulated = false;
						activeChunk.meshCollider.enabled = false;
					}

					if (ChunksVisible) activeChunk.RefreshDisplayMesh();

					// the mesh now shows the resampled data where it lies on the grid
					if (force)
						activeChunk.transform.SetPositionAndRotation(
							scanner.ChunkCoordToCornerWorldPos(scanner.ChunkIndexToChunkCoord(activeChunk.chunkIndex)), Quaternion.identity);

					RememberMeshedSurface(activeChunk, updatedSignBits);

					if (CommitChunkWork(activeChunk, activePendingChangeSum))
						ChunkMeshUpdated.Invoke(activeChunk);

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
				if (activeChunk != null)
					AbortChunkWork(activeChunk, activePendingChangeSum);

				mesher.Dispose();
				readbackBuffer.Dispose();
				updatedSignBits.Dispose();
				DestroyImmediate(scratchMesh);
			}
		}
	}
}
