using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	public enum EnvPathResult
	{
		/// <summary>No search covers the point (outside the scanned box, or none has finished).</summary>
		Unknown,
		/// <summary>The scanned empty space holds no route within the length limit.</summary>
		Unreachable,
		Found
	}

	/// <summary>
	/// Shortest routes through the scanned empty space from one origin to everywhere, over
	/// the <see cref="ChunkPortalStore"/> graph: a route enters each chunk component at the
	/// centre of the face area it shares with the one before. Unscanned chunks are open.
	/// A Burst job rebuilds the graph and searches whenever the portals change or the
	/// origin moves; queries read the last finished search
	/// </summary>
	public sealed class EnvPathField : IDisposable
	{
		// past this many chunk cells in the box, nothing is searched
		private const int MaxCells = 1 << 16;

		private static readonly ProfilerMarker QueryMarker = new("EnvPathField.FindPath");

		private readonly EnvMesher mesher;
		private readonly float maxLength;
		private readonly float originMoveToRefresh;
		private readonly float minSecondsBetweenSearches;

		private SearchResult front = new(), back = new();
		private Scratch scratch = new();
		private JobHandle search;
		private bool searching;

		private int searchedRevision = -1;
		private float3 searchedOrigin;
		private float lastSearchTime = float.NegativeInfinity;

		private readonly int envMeshLayerMask = LayerMask.GetMask(EnvMesher.EnvironmentMeshLayerName);
		// a route's points from the origin out: where it enters each component, and its length there
		private readonly List<Vector3> routePoints = new();
		private readonly List<float> routeLengths = new();

		private NativeArray<uint> floodMasks = new(ChunkPortals.ComponentWords, Allocator.Persistent);
		private NativeArray<int> floodFound = new(1, Allocator.Persistent);

		/// <summary>Where the last finished search started.</summary>
		public Vector3 Origin => front.origin;

		public EnvPathField(EnvMesher mesher, float maxLength, float originMoveToRefresh = 0.25f,
			float minSecondsBetweenSearches = 0.1f)
		{
			this.mesher = mesher;
			this.maxLength = maxLength;
			this.originMoveToRefresh = originMoveToRefresh;
			this.minSecondsBetweenSearches = minSecondsBetweenSearches;
		}

		public void Dispose()
		{
			search.Complete();
			front.Dispose();
			back.Dispose();
			scratch.Dispose();
			floodMasks.Dispose();
			floodFound.Dispose();
		}

		/// <summary>Call every frame: picks up a finished search and starts one when due.</summary>
		public void Update(Vector3 origin)
		{
			if (searching)
			{
				if (!search.IsCompleted) return;
				search.Complete();
				searching = false;
				(front, back) = (back, front);
			}

			EnvScanner scanner = EnvScanner.Instance;
			if (!mesher || !scanner) return;

			ChunkPortalStore portals = mesher.Portals;
			bool due = portals.Revision != searchedRevision ||
			           math.distance(origin, searchedOrigin) > originMoveToRefresh;
			if (!due || Time.unscaledTime - lastSearchTime < minSecondsBetweenSearches) return;

			back.origin = origin;
			search = new SearchJob
			{
				slotOfChunk = portals.SlotOfChunk,
				masks = portals.Masks,
				componentCounts = portals.ComponentCounts,
				chunkOfSlot = portals.ChunkOfSlot,
				tableDims = scanner.ChunkTableDims,
				chunkSize = scanner.ChunkWorldSizeDim,
				voxSize = scanner.VoxSize,
				originOffset = scanner.OriginOffset,
				origin = origin,
				originComponent = ComponentAt(scanner, origin),
				maxLength = maxLength,
				box = back.box,
				nodeBase = back.nodeBase,
				dist = back.dist,
				parent = back.parent,
				arrival = back.arrival,
				edges = scratch.edges,
				edgeStart = scratch.edgeStart,
				sortedEdges = scratch.sortedEdges,
				heap = scratch.heap
			}.Schedule();
			portals.AddReader(search);
			JobHandle.ScheduleBatchedJobs();

			searching = true;
			searchedRevision = portals.Revision;
			searchedOrigin = origin;
			lastSearchTime = Time.unscaledTime;
		}

		/// <summary>
		/// The shortest route from <see cref="Origin"/> to <paramref name="target"/>: its length,
		/// and the furthest point along it the origin sees past the scanned surfaces (the
		/// target itself when nothing stands between them)
		/// </summary>
		public EnvPathResult FindPath(Vector3 target, out float length, out Vector3 firstTurn)
		{
			using var _ = QueryMarker.Auto();

			length = 0;
			firstTurn = target;

			EnvScanner scanner = EnvScanner.Instance;
			if (!mesher || !scanner || !front.box.IsCreated) return EnvPathResult.Unknown;

			int3 boxMin = front.box[0], boxDims = front.box[1];
			int3 local = scanner.WorldPosToChunkCoord(target) - boxMin;
			if (math.any(local < 0) || math.any(local >= boxDims)) return EnvPathResult.Unknown;

			int cell = local.x + boxDims.x * (local.y + boxDims.y * local.z);
			int firstNode = front.nodeBase[cell];
			int count = front.nodeBase[cell + 1] - firstNode;

			// the target's own component; a solid voxel, or one whose components changed
			// since the search, tries them all
			int component = ComponentAt(scanner, target);
			int from = 0, to = count;
			if (component >= 0 && component < count) (from, to) = (component, component + 1);

			float3 t = target;
			int best = -1;
			float bestLength = float.PositiveInfinity;
			for (int k = from; k < to; k++)
			{
				int node = firstNode + k;
				if (float.IsPositiveInfinity(front.dist[node])) continue;
				float l = front.dist[node] + math.distance(front.arrival[node], t);
				if (l >= bestLength) continue;
				bestLength = l;
				best = node;
			}

			if (best < 0 || bestLength > maxLength) return EnvPathResult.Unreachable;

			routePoints.Clear();
			routeLengths.Clear();
			for (int step = best; front.parent[step] >= 0; step = front.parent[step])
			{
				routePoints.Add(front.arrival[step]);
				routeLengths.Add(front.dist[step]);
			}

			// waypoints sit on chunk faces, so the route zigzags; cut straight to the last one in sight
			Vector3 origin = front.origin;
			length = bestLength;
			if (InSight(origin, target))
			{
				length = Vector3.Distance(origin, target);
				return EnvPathResult.Found;
			}

			if (routePoints.Count > 0) firstTurn = routePoints[^1];
			for (int i = 0; i < routePoints.Count; i++)
			{
				if (!InSight(origin, routePoints[i])) continue;
				firstTurn = routePoints[i];
				length = Vector3.Distance(origin, firstTurn) + bestLength - routeLengths[i];
				break;
			}

			return EnvPathResult.Found;
		}

		private bool InSight(Vector3 from, Vector3 to) =>
			!Physics.Linecast(from, to, envMeshLayerMask, QueryTriggerInteraction.Ignore);

		// the component of its chunk a point is in: 0 for an unscanned chunk, -1 when the
		// point reads as solid or its pocket reaches no face
		private int ComponentAt(EnvScanner scanner, float3 pos)
		{
			int3 coord = scanner.WorldPosToChunkCoord(pos);
			int chunkIndex = scanner.ChunkCoordToChunkIndex(coord);
			if (chunkIndex < 0) return 0;

			ChunkPortalStore portals = mesher.Portals;
			int slot = portals.SlotOf(chunkIndex);
			if (slot == ChunkPortalStore.OpenSlot) return 0;
			if (!mesher.TryGetChunk(chunkIndex, out Chunk chunk) || !chunk.voxelSignBits.IsCreated) return -1;

			float3 corner = scanner.ChunkCoordToCornerWorldPos(coord);
			int3 voxel = math.clamp((int3)math.round((pos - corner) / scanner.VoxSize), 1, ChunkPortals.Span);

			new ChunkPortals.ContainingComponentJob
			{
				solidBits = chunk.voxelSignBits,
				voxel = voxel,
				masks = floodMasks,
				found = floodFound
			}.Run();

			if (floodFound[0] == 0) return -1;
			return ChunkPortals.MatchComponent(floodMasks, portals.Masks, ChunkPortalStore.MaskOffset(slot, 0),
				portals.ComponentCount(slot));
		}

		private sealed class SearchResult : IDisposable
		{
			public Vector3 origin;

			// box min, box dims
			public NativeArray<int3> box = new(2, Allocator.Persistent);

			// per cell, its first node; one past the last cell, the node count
			public NativeList<int> nodeBase = new(Allocator.Persistent);
			public NativeList<float> dist = new(Allocator.Persistent);
			public NativeList<int> parent = new(Allocator.Persistent);
			public NativeList<float3> arrival = new(Allocator.Persistent);

			public void Dispose()
			{
				box.Dispose();
				nodeBase.Dispose();
				dist.Dispose();
				parent.Dispose();
				arrival.Dispose();
			}
		}

		private sealed class Scratch : IDisposable
		{
			public NativeList<Edge> edges = new(Allocator.Persistent);
			public NativeList<int> edgeStart = new(Allocator.Persistent);
			public NativeList<Edge> sortedEdges = new(Allocator.Persistent);
			public NativeList<HeapItem> heap = new(Allocator.Persistent);

			public void Dispose()
			{
				edges.Dispose();
				edgeStart.Dispose();
				sortedEdges.Dispose();
				heap.Dispose();
			}
		}

		private struct Edge
		{
			public int from, to;
			public float3 waypoint;
		}

		private struct HeapItem
		{
			public float dist;
			public int node;
		}

		[BurstCompile]
		private struct SearchJob : IJob
		{
			[ReadOnly] public NativeHashMap<int, int> slotOfChunk;
			[ReadOnly] public NativeArray<uint> masks;
			[ReadOnly] public NativeArray<int> componentCounts;
			[ReadOnly] public NativeArray<int> chunkOfSlot;

			public int3 tableDims;
			public float chunkSize;
			public float voxSize;
			public float3 originOffset;

			public float3 origin;
			public int originComponent;
			public float maxLength;

			public NativeArray<int3> box;
			public NativeList<int> nodeBase;
			public NativeList<float> dist;
			public NativeList<int> parent;
			public NativeList<float3> arrival;

			public NativeList<Edge> edges;
			public NativeList<int> edgeStart;
			public NativeList<Edge> sortedEdges;
			public NativeList<HeapItem> heap;

			public void Execute()
			{
				int3 originCoord = WorldToCoord(origin);
				int3 min = originCoord, max = originCoord;
				for (int slot = 0; slot < chunkOfSlot.Length; slot++)
				{
					int chunkIndex = chunkOfSlot[slot];
					if (chunkIndex < 0) continue;
					int3 c = IndexToCoord(chunkIndex);
					min = math.min(min, c);
					max = math.max(max, c);
				}

				int3 dims = max - min + 1;
				box[0] = min;
				if ((long)dims.x * dims.y * dims.z > MaxCells) dims = 0;
				box[1] = dims;
				int cells = dims.x * dims.y * dims.z;

				nodeBase.ResizeUninitialized(cells + 1);
				int nodes = 0;
				for (int cell = 0; cell < cells; cell++)
				{
					nodeBase[cell] = nodes;
					nodes += componentCounts[SlotOf(min + CellToLocal(cell, dims))];
				}
				nodeBase[cells] = nodes;

				BuildEdges(min, dims, cells);
				Search(originCoord - min, dims, nodes);
			}

			private void BuildEdges(int3 min, int3 dims, int cells)
			{
				edges.Clear();
				for (int cell = 0; cell < cells; cell++)
				{
					int3 local = CellToLocal(cell, dims);
					int3 coord = min + local;
					int slot = SlotOf(coord);
					int count = componentCounts[slot];

					// each shared face once, from its lower side: +X, +Y, +Z
					for (int axis = 0; axis < 3; axis++)
					{
						int face = axis * 2 + 1;
						int3 nLocal = local + ChunkPortals.FaceSteps[face];
						if (nLocal[axis] >= dims[axis]) continue;

						int3 nCoord = min + nLocal;
						int nSlot = SlotOf(nCoord);
						int nCount = componentCounts[nSlot];
						int nCell = LocalToCell(nLocal, dims);
						float3 faceCorner = CoordToCorner(nCoord);

						for (int i = 0; i < count; i++)
						{
							int a = ChunkPortalStore.MaskOffset(slot, i) + face * ChunkPortals.Span;
							if (!ChunkPortals.Any(masks, a)) continue;

							for (int j = 0; j < nCount; j++)
							{
								int b = ChunkPortalStore.MaskOffset(nSlot, j) + (face ^ 1) * ChunkPortals.Span;
								if (!ChunkPortals.SharedCentroid(masks, a, masks, b, out float2 rowAndBit)) continue;

								float3 waypoint = FacePoint(faceCorner, axis, rowAndBit);
								int u = nodeBase[cell] + i, v = nodeBase[nCell] + j;
								edges.Add(new Edge { from = u, to = v, waypoint = waypoint });
								edges.Add(new Edge { from = v, to = u, waypoint = waypoint });
							}
						}
					}
				}
			}

			// a face's (row, bit) mean to world: X faces are rows of z, bits of y; Y faces
			// rows of z, bits of x; Z faces rows of y, bits of x. Mask index i is voxel i + 1,
			// and the face sits between the lower chunk's last voxel and the upper's first
			private float3 FacePoint(float3 upperCorner, int axis, float2 rowAndBit)
			{
				float3 voxel = axis switch
				{
					0 => new float3(0.5f, rowAndBit.y + 1, rowAndBit.x + 1),
					1 => new float3(rowAndBit.y + 1, 0.5f, rowAndBit.x + 1),
					_ => new float3(rowAndBit.y + 1, rowAndBit.x + 1, 0.5f)
				};
				return upperCorner + voxel * voxSize;
			}

			private void Search(int3 originLocal, int3 dims, int nodes)
			{
				dist.ResizeUninitialized(nodes);
				parent.ResizeUninitialized(nodes);
				arrival.ResizeUninitialized(nodes);
				for (int n = 0; n < nodes; n++)
				{
					dist[n] = float.PositiveInfinity;
					parent[n] = -1;
				}

				if (nodes == 0) return;

				// edges grouped by their first node
				edgeStart.ResizeUninitialized(nodes + 1);
				for (int n = 0; n <= nodes; n++) edgeStart[n] = 0;
				for (int e = 0; e < edges.Length; e++) edgeStart[edges[e].from + 1]++;
				for (int n = 0; n < nodes; n++) edgeStart[n + 1] += edgeStart[n];
				sortedEdges.ResizeUninitialized(edges.Length);
				NativeArray<int> fill = new(nodes, Allocator.Temp);
				for (int e = 0; e < edges.Length; e++)
				{
					Edge edge = edges[e];
					sortedEdges[edgeStart[edge.from] + fill[edge.from]++] = edge;
				}

				heap.Clear();
				int originCell = LocalToCell(originLocal, dims);
				int first = nodeBase[originCell], count = nodeBase[originCell + 1] - first;
				for (int k = 0; k < count; k++)
				{
					if (originComponent >= 0 && originComponent < count && k != originComponent) continue;
					dist[first + k] = 0;
					arrival[first + k] = origin;
					Push(new HeapItem { dist = 0, node = first + k });
				}

				while (heap.Length > 0)
				{
					HeapItem item = Pop();
					int u = item.node;
					if (item.dist > dist[u]) continue;

					float3 at = arrival[u];
					for (int e = edgeStart[u]; e < edgeStart[u + 1]; e++)
					{
						Edge edge = sortedEdges[e];
						float d = item.dist + math.distance(at, edge.waypoint);
						if (d > maxLength || d >= dist[edge.to]) continue;

						dist[edge.to] = d;
						parent[edge.to] = u;
						arrival[edge.to] = edge.waypoint;
						Push(new HeapItem { dist = d, node = edge.to });
					}
				}
			}

			private void Push(HeapItem item)
			{
				heap.Add(item);
				int i = heap.Length - 1;
				while (i > 0)
				{
					int up = (i - 1) / 2;
					if (heap[up].dist <= item.dist) break;
					heap[i] = heap[up];
					i = up;
				}

				heap[i] = item;
			}

			private HeapItem Pop()
			{
				HeapItem top = heap[0];
				HeapItem last = heap[heap.Length - 1];
				heap.RemoveAt(heap.Length - 1);
				int count = heap.Length;
				if (count == 0) return top;

				int i = 0;
				while (true)
				{
					int child = i * 2 + 1;
					if (child >= count) break;
					if (child + 1 < count && heap[child + 1].dist < heap[child].dist) child++;
					if (last.dist <= heap[child].dist) break;
					heap[i] = heap[child];
					i = child;
				}

				heap[i] = last;
				return top;
			}

			private int SlotOf(int3 coord)
			{
				if (math.any(coord < 0) || math.any(coord >= tableDims)) return ChunkPortalStore.OpenSlot;
				int index = coord.x + coord.y * tableDims.x + coord.z * tableDims.x * tableDims.y;
				return slotOfChunk.TryGetValue(index, out int slot) ? slot : ChunkPortalStore.OpenSlot;
			}

			// mirror EnvScanner's chunk addressing
			private int3 IndexToCoord(int index) => new(
				index % tableDims.x,
				index / tableDims.x % tableDims.y,
				index / (tableDims.x * tableDims.y));

			private int3 WorldToCoord(float3 world) =>
				(int3)math.floor((world - originOffset) / chunkSize) + tableDims / 2;

			private float3 CoordToCorner(int3 coord) =>
				(float3)(coord - tableDims / 2) * chunkSize - voxSize + originOffset;

			private static int3 CellToLocal(int cell, int3 dims) =>
				new(cell % dims.x, cell / dims.x % dims.y, cell / (dims.x * dims.y));

			private static int LocalToCell(int3 local, int3 dims) => local.x + dims.x * (local.y + dims.y * local.z);
		}
	}
}
