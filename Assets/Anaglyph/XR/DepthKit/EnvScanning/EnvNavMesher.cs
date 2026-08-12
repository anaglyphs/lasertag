using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AI;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// Builds and incrementally updates a runtime NavMesh from <see cref="EnvMesher"/> chunk meshes
	/// </summary>
	public class EnvNavMesher : MonoBehaviour
	{
		public static EnvNavMesher Instance { get; private set; }

		[SerializeField] private int agentTypeID;
		// [SerializeField] private float agentRadius;
		// [SerializeField] private float agentHeight;
		// [SerializeField] private float agentSlope;
		// [SerializeField] private float agentClimb;
		// [SerializeField] private float ledgeDropHeight;
		// [SerializeField] private float maxJumpAcrossDistance;
		
		[SerializeField] private int navMeshArea;

		// A bake left to its own devices takes every job worker, and physics blocks waiting for a
		// free worker before it can schedule its own jobs — which reads as a PxScene.simulate spike
		[SerializeField] private int maxJobWorkers = 1;

		// How long the loop idles before looking for scan changes again
		[SerializeField] private float secondsBetweenChecks = 1f;

		// Slack around the tiles an update rebuilds, so they land strictly inside the bounds
		[SerializeField] private float boundsPadding = 1f;

		// Widest region a single update may rebuild, in tiles per axis. Chunks scanned far apart
		// are spread over several updates instead of merged into one region spanning the space
		// between them
		[SerializeField] private int maxUpdateTilesPerAxis = 4;

		/// <summary>
		/// Invoked after each NavMesh update completes
		/// </summary>
		public event Action Updated = delegate { };

		public NavMeshData Data { get; private set; }
		public bool IsBaking { get; private set; }

		// The source list has to stay complete and in a stable order between updates for Unity to
		// work out which parts of the NavMesh changed, so a chunk keeps its slot for its lifetime
		private readonly List<NavMeshBuildSource> sources = new();
		private readonly Dictionary<int, int> chunkSourceIndices = new();

		private readonly HashSet<int> dirtyChunks = new();
		private readonly List<int> dirtyChunkDrain = new();

		private NavMeshDataInstance dataInstance;
		private CancellationTokenSource loopCancelSrc;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			EnvScanner.Instance.Cleared += OnClear;

			Begin();
		}

		private void OnEnable()
		{
			if (didStart) Begin();
		}

		private void Begin()
		{
			EnvMesher.Instance.ChunkMeshUpdated += OnChunkMeshUpdated;

			// meshing that happened while disabled was never reported, and a chunk first meshed
			// back then isn't in the source list at all, so every chunk is taken in from scratch
			foreach (Chunk chunk in EnvMesher.Instance.Chunks)
				MarkChunkDirty(chunk);

			CreateData();
			StartUpdateLoop();
		}

		private void OnDisable()
		{
			loopCancelSrc?.Cancel();
			CancelBake();

			if (EnvMesher.Instance)
				EnvMesher.Instance.ChunkMeshUpdated -= OnChunkMeshUpdated;
		}

		private void OnDestroy()
		{
			if (EnvScanner.Instance)
				EnvScanner.Instance.Cleared -= OnClear;

			DestroyData();
		}

		private void OnClear()
		{
			loopCancelSrc?.Cancel();
			CancelBake();

			// the chunks these point at are about to be destroyed
			sources.Clear();
			chunkSourceIndices.Clear();
			dirtyChunks.Clear();

			DestroyData();

			if (!enabled) return;

			CreateData();
			StartUpdateLoop();
		}

		private void CreateData()
		{
			if (Data != null) return;

			Data = new NavMeshData(agentTypeID) { name = "Environment NavMesh" };
			dataInstance = NavMesh.AddNavMeshData(Data);
		}

		private void DestroyData()
		{
			if (Data == null) return;

			if (dataInstance.valid)
				dataInstance.Remove();

			Destroy(Data);
			Data = null;
		}

		private void CancelBake()
		{
			if (!IsBaking) return;

			NavMeshBuilder.Cancel(Data);
			IsBaking = false;
		}

		private void OnChunkMeshUpdated(Chunk chunk) => MarkChunkDirty(chunk);

		/// <summary>
		/// Queues a chunk into the next NavMesh update. Locally meshed chunks are picked up
		/// automatically — meshes applied from anywhere else have to be marked through here.
		/// </summary>
		public void MarkChunkDirty(Chunk chunk)
		{
			if (!chunkSourceIndices.TryGetValue(chunk.chunkIndex, out int sourceIndex))
			{
				sourceIndex = sources.Count;
				chunkSourceIndices.Add(chunk.chunkIndex, sourceIndex);
				sources.Add(default);
			}

			// a chunk that meshed to nothing keeps its slot, but as a zero-size box: the
			// builder rejects an empty mesh and warns about it on every bake
			sources[sourceIndex] = chunk.meshIsPopulated
				? new NavMeshBuildSource
				{
					shape = NavMeshBuildSourceShape.Mesh,
					sourceObject = chunk.mesh,
					transform = chunk.transform.localToWorldMatrix,
					area = navMeshArea
				}
				: new NavMeshBuildSource
				{
					shape = NavMeshBuildSourceShape.Box,
					size = Vector3.zero,
					transform = chunk.transform.localToWorldMatrix,
					area = navMeshArea
				};

			dirtyChunks.Add(chunk.chunkIndex);
		}

		private void StartUpdateLoop()
		{
			loopCancelSrc?.Cancel();
			loopCancelSrc = new CancellationTokenSource();

			_ = RunUpdateLoop(loopCancelSrc.Token);
		}

		private async Awaitable RunUpdateLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					// walking dirties chunks faster than one bake a second can take them in, and a
					// backlog only ever grows, so bakes run back to back until the scan is caught up
					if (dirtyChunks.Count == 0)
					{
						await Awaitable.WaitForSecondsAsync(secondsBetweenChecks, ctkn);
						continue;
					}

					await Bake(ctkn);

					await Awaitable.NextFrameAsync(ctkn);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		private async Awaitable Bake(CancellationToken ctkn)
		{
			NavMeshBuildSettings settings = BuildSettings();

			if (!TryTakeDirtyBounds(settings, out Bounds bounds)) return;

			IsBaking = true;

			try
			{
				AsyncOperation bake = NavMeshBuilder.UpdateNavMeshDataAsync(Data, settings, sources, bounds);

				// do NOT cancel until the bake is done — Data is destroyed on cancellation
				// ReSharper disable once MethodSupportsCancellation
				while (!bake.isDone) await Awaitable.NextFrameAsync();
			}
			finally
			{
				IsBaking = false;
			}

			ctkn.ThrowIfCancellationRequested();

			Updated.Invoke();
		}

		/// <summary>
		/// Bakes at the resolution the scan was captured at, one tile to a chunk. The builder can
		/// only resolve what the chunk meshes carry, so a finer voxel buys nothing; sizing a tile
		/// to a chunk lines Unity's tile grid up with the chunk grid, both being laid out from the
		/// world origin, and a rescanned chunk then rebuilds the one tile it fills rather than the
		/// four it would straddle.
		/// </summary>
		private NavMeshBuildSettings BuildSettings()
		{
			NavMeshBuildSettings settings = NavMesh.GetSettingsByID(agentTypeID);
			settings.maxJobWorkers = (uint)Mathf.Max(1, maxJobWorkers);

			float chunkSize = EnvMesher.ChunkWorldSize.x;
			int voxelsPerChunk = Mathf.Max(1, Mathf.RoundToInt(chunkSize / EnvScanner.Instance.VoxSize));

			settings.overrideVoxelSize = true;
			settings.voxelSize = chunkSize / voxelsPerChunk;
			settings.overrideTileSize = true;
			settings.tileSize = voxelsPerChunk;

			// tiles the bounds don't reach keep the geometry they were baked with, so an
			// update only has to describe the region that actually changed
			settings.preserveTilesOutsideBounds = true;

			return settings;
		}

		/// <summary>
		/// Drains as many dirty chunks as fit within <see cref="maxUpdateTilesPerAxis"/> of each
		/// other into one region, leaving the rest for the following update
		/// </summary>
		private bool TryTakeDirtyBounds(NavMeshBuildSettings settings, out Bounds bounds)
		{
			float tileWorldSize = settings.tileSize * settings.voxelSize;

			bounds = default;
			bool hasBounds = false;

			dirtyChunkDrain.Clear();
			dirtyChunkDrain.AddRange(dirtyChunks);

			foreach (int chunkIndex in dirtyChunkDrain)
			{
				if (!EnvMesher.Instance.TryGetChunk(chunkIndex, out Chunk chunk))
				{
					dirtyChunks.Remove(chunkIndex);
					continue;
				}

				Bounds chunkBounds = SnapToTiles(GetChunkBounds(chunk), tileWorldSize);

				if (hasBounds)
				{
					Bounds merged = bounds;
					merged.Encapsulate(chunkBounds);

					if (TilesAcross(merged.size.x, tileWorldSize) > maxUpdateTilesPerAxis ||
					    TilesAcross(merged.size.z, tileWorldSize) > maxUpdateTilesPerAxis)
						continue;

					bounds = merged;
				}
				else
				{
					bounds = chunkBounds;
					hasBounds = true;
				}

				dirtyChunks.Remove(chunkIndex);
			}

			if (!hasBounds) return false;

			EncapsulateColumnHeights(ref bounds);

			bounds.Expand(boundsPadding * 2f);

			// bounds are local to the NavMeshData, which is never moved off the world origin
			return true;
		}

		private static Bounds GetChunkBounds(Chunk chunk)
		{
			return new Bounds(chunk.transform.position + EnvMesher.ChunkWorldSizeHalf,
				EnvMesher.ChunkWorldSize);
		}

		/// <summary>
		/// Grows bounds out to whole tiles of the grid Unity lays out from the NavMeshData
		/// origin. A tile is only rebuilt when it lies entirely within the update bounds, so
		/// anything short of this leaves the tiles a chunk sits in untouched.
		/// </summary>
		private static Bounds SnapToTiles(Bounds bounds, float tileWorldSize)
		{
			Vector3 min = bounds.min;
			Vector3 max = bounds.max;

			min.x = Mathf.Floor(min.x / tileWorldSize) * tileWorldSize;
			min.z = Mathf.Floor(min.z / tileWorldSize) * tileWorldSize;
			max.x = Mathf.Ceil(max.x / tileWorldSize) * tileWorldSize;
			max.z = Mathf.Ceil(max.z / tileWorldSize) * tileWorldSize;

			Bounds snapped = new();
			snapped.SetMinMax(min, max);

			return snapped;
		}

		private static int TilesAcross(float size, float tileWorldSize)
		{
			return Mathf.RoundToInt(size / tileWorldSize);
		}

		/// <summary>
		/// Stretches bounds vertically over every chunk standing in the same tiles. A tile spans
		/// the full height of the space it covers, so rebuilding one from bounds that reach only
		/// part of that height drops the navmesh of everything scanned above or below.
		/// </summary>
		private void EncapsulateColumnHeights(ref Bounds bounds)
		{
			Vector3 min = bounds.min;
			Vector3 max = bounds.max;

			foreach (int chunkIndex in chunkSourceIndices.Keys)
			{
				if (!EnvMesher.Instance.TryGetChunk(chunkIndex, out Chunk chunk))
					continue;

				Bounds chunkBounds = GetChunkBounds(chunk);

				if (chunkBounds.max.x <= min.x || chunkBounds.min.x >= max.x) continue;
				if (chunkBounds.max.z <= min.z || chunkBounds.min.z >= max.z) continue;

				min.y = Mathf.Min(min.y, chunkBounds.min.y);
				max.y = Mathf.Max(max.y, chunkBounds.max.y);
			}

			bounds.SetMinMax(min, max);
		}
	}
}
