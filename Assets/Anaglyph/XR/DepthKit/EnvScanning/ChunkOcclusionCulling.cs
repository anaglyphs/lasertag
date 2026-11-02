using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// The pockets of empty space inside a chunk that reach its faces, and exactly where
	/// each reaches them: per component, a 30×30 bitmask per face of the voxels it
	/// holds there. Two components in neighbouring chunks are joined when their masks on
	/// the shared face overlap. Faces are numbered -X, +X, -Y, +Y, -Z, +Z, so a face's
	/// opposite is <c>face ^ 1</c>. Face masks are rows of z (bits y) on X faces, rows of
	/// z (bits x) on Y faces and rows of y (bits x) on Z faces, the same on both sides
	/// of a face. Space never observed counts as empty: holes in the scan only ever make
	/// a chunk more open
	/// </summary>
	public static class ChunkPortals
	{
		public const int FaceCount = 6;

		/// <summary>Voxels a chunk owns per axis; the outer layer is the neighbours' apron.</summary>
		public const int Span = 30;

		public const int Dim = Span + 2;
		public const uint RowMask = (1u << Span) - 1;

		/// <summary>Words per component: a mask per face.</summary>
		public const int ComponentWords = FaceCount * Span;

		/// <summary>Past this many, the rest of a chunk's empty space is one component.</summary>
		public const int MaxComponents = 16;

		public static readonly int3[] FaceSteps =
		{
			new(-1, 0, 0), new(1, 0, 0),
			new(0, -1, 0), new(0, 1, 0),
			new(0, 0, -1), new(0, 0, 1)
		};

		/// <summary>One component touching every voxel of every face: a chunk nothing is known to seal.</summary>
		public static readonly uint[] Open = CreateOpen();

		private static uint[] CreateOpen()
		{
			uint[] open = new uint[ComponentWords];
			Array.Fill(open, RowMask);
			return open;
		}

		// the owned voxels of a row (y + z * Dim), as bits of x, for rows 1..Span on y and z
		private const uint Interior = RowMask << 1;
		private const int Last = Span;

		private static void LoadEmpty(NativeArray<uint> solidBits, NativeArray<uint> empty)
		{
			for (int r = 0; r < Dim * Dim; r++) empty[r] = 0;
			for (int z = 1; z <= Last; z++)
			for (int y = 1; y <= Last; y++)
			{
				int r = y + z * Dim;
				empty[r] = ~solidBits[r] & Interior;
			}
		}

		// 6-connected: a surface one voxel thick seals. Rows grow along x by shifting to
		// a fixed point and take from their four neighbouring rows; sweeps alternate
		// direction so long paths settle in few passes
		private static void Flood(NativeArray<uint> empty, NativeArray<uint> fill)
		{
			const int rows = Span * Span;
			bool changed = true;
			bool forward = true;
			while (changed)
			{
				changed = false;
				for (int i = 0; i < rows; i++)
				{
					int j = forward ? i : rows - 1 - i;
					int r = 1 + j % Span + (1 + j / Span) * Dim;

					uint v = fill[r];
					uint grow = (v | fill[r - 1] | fill[r + 1] | fill[r - Dim] | fill[r + Dim]) & empty[r];
					if (grow == 0) continue;

					uint prev;
					do
					{
						prev = grow;
						grow = (grow | (grow << 1) | (grow >> 1)) & empty[r];
					} while (grow != prev);

					if (grow == v) continue;
					fill[r] = grow;
					changed = true;
				}

				forward = !forward;
			}
		}

		private static void ExtractFaces(NativeArray<uint> fill, NativeArray<uint> masks, int offset)
		{
			for (int i = 0; i < Span; i++)
			{
				int a = i + 1;
				uint nx = 0, px = 0;
				for (int j = 0; j < Span; j++)
				{
					uint row = fill[j + 1 + a * Dim];
					nx |= ((row >> 1) & 1u) << j;
					px |= ((row >> Last) & 1u) << j;
				}

				masks[offset + 0 * Span + i] = nx;
				masks[offset + 1 * Span + i] = px;
				masks[offset + 2 * Span + i] = (fill[1 + a * Dim] >> 1) & RowMask;
				masks[offset + 3 * Span + i] = (fill[Last + a * Dim] >> 1) & RowMask;
				masks[offset + 4 * Span + i] = (fill[a + 1 * Dim] >> 1) & RowMask;
				masks[offset + 5 * Span + i] = (fill[a + Last * Dim] >> 1) & RowMask;
			}
		}

		private static bool IsFaceRow(int r)
		{
			int y = r % Dim, z = r / Dim;
			return y == 1 || y == Last || z == 1 || z == Last;
		}

		/// <summary>Splits a chunk's empty space into the components that reach its faces.</summary>
		[BurstCompile]
		public struct ComponentsJob : IJob
		{
			/// <summary>
			/// <see cref="Chunk.voxelSignBits"/>: a set bit is behind a surface. One word is
			/// one x row; row = y + z * dim
			/// </summary>
			[ReadOnly] public NativeArray<uint> solidBits;

			/// <summary><see cref="MaxComponents"/> × <see cref="ComponentWords"/>.</summary>
			[WriteOnly] public NativeArray<uint> masks;

			[WriteOnly] public NativeArray<int> count;

			public void Execute()
			{
				// a row must be exactly one word
				if (solidBits.Length != Dim * Dim)
				{
					for (int i = 0; i < ComponentWords; i++) masks[i] = RowMask;
					count[0] = 1;
					return;
				}

				NativeArray<uint> empty = new(Dim * Dim, Allocator.Temp);
				NativeArray<uint> fill = new(Dim * Dim, Allocator.Temp);
				LoadEmpty(solidBits, empty);

				int n = 0;
				while (n < MaxComponents)
				{
					int seedRow = -1;
					uint seedBits = 0;
					for (int r = Dim; r < Dim * (Dim - 1) && seedRow < 0; r++)
					{
						uint face = empty[r] & (IsFaceRow(r) ? Interior : (1u << 1) | (1u << Last));
						if (face == 0) continue;
						seedRow = r;
						seedBits = face & (~face + 1);
					}

					if (seedRow < 0) break;

					if (n == MaxComponents - 1)
					{
						// everything left, as one
						for (int r = 0; r < Dim * Dim; r++) fill[r] = empty[r];
					}
					else
					{
						for (int r = 0; r < Dim * Dim; r++) fill[r] = 0;
						fill[seedRow] = seedBits;
						Flood(empty, fill);
					}

					ExtractFaces(fill, masks, n * ComponentWords);
					n++;

					for (int r = 0; r < Dim * Dim; r++) empty[r] &= ~fill[r];
				}

				count[0] = n;
			}
		}

		/// <summary>The face masks of the empty component holding one voxel, if it is empty.</summary>
		[BurstCompile]
		public struct ContainingComponentJob : IJob
		{
			[ReadOnly] public NativeArray<uint> solidBits;
			public int3 voxel;

			[WriteOnly] public NativeArray<uint> masks;
			[WriteOnly] public NativeArray<int> found;

			public void Execute()
			{
				NativeArray<uint> empty = new(Dim * Dim, Allocator.Temp);
				NativeArray<uint> fill = new(Dim * Dim, Allocator.Temp);
				LoadEmpty(solidBits, empty);

				int r = voxel.y + voxel.z * Dim;
				uint bit = 1u << voxel.x;
				if ((empty[r] & bit) == 0)
				{
					found[0] = 0;
					return;
				}

				fill[r] = bit;
				Flood(empty, fill);
				ExtractFaces(fill, masks, 0);
				found[0] = 1;
			}
		}
	}

	/// <summary>
	/// Hides chunks a camera can't see past the scanned surfaces, for that camera only.
	/// Walks the empty space of the chunk grid out from the camera (cave culling): from
	/// the component holding the camera, a step crosses a face into each neighbouring
	/// component the face masks join it to, provided the neighbour touches the view
	/// frustum and the step doesn't head back toward the camera. Chunks the walk never
	/// reaches are behind walls. Runs before each camera culls, on the CPU, with no
	/// readback and no frame of lag
	/// </summary>
	public sealed class ChunkOcclusionCuller : IDisposable
	{
		// a box of more cells than this is a map spread too thin to be worth walking
		private const int MaxCells = 1 << 18;

		// component index of the walk's first step, whose masks are startMasks
		private const byte StartComponent = 31;

		// a cell whose face the walk looked at, whether or not it went in
		private const uint SeenBit = 1u << 30;

		private static readonly ProfilerMarker CullMarker = new("EnvMesh.OcclusionCull");

		private readonly EnvMesher mesher;
		private readonly int layerMask;
		private readonly float frustumMargin;

		private readonly List<Chunk> hidden = new();
		private Camera hiddenFor;

		// per cell of the box around the chunks and camera: the components reached
		private uint[] reached = Array.Empty<uint>();
		private readonly Queue<Step> queue = new();
		private readonly float4[] planes = new float4[16];
		private int planeCount;

		// the camera's component, kept until the camera changes voxel or its chunk remeshes
		private readonly uint[] startMasks = new uint[ChunkPortals.ComponentWords];
		private NativeArray<uint> startMasksNative;
		private NativeArray<int> startFound;
		private bool startValid;
		private Chunk startChunk;
		private int3 startVoxel;
		private int startRevision;

		public int VisibleCount { get; private set; }
		public int CulledCount { get; private set; }

		private struct Step
		{
			public int3 coord;
			public byte component;
			public byte dirs;
		}

		public ChunkOcclusionCuller(EnvMesher mesher, int layerMask, float frustumMargin)
		{
			this.mesher = mesher;
			this.layerMask = layerMask;
			this.frustumMargin = frustumMargin;
			startMasksNative = new NativeArray<uint>(ChunkPortals.ComponentWords, Allocator.Persistent);
			startFound = new NativeArray<int>(1, Allocator.Persistent);
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
			RenderPipelineManager.endCameraRendering += OnEndCamera;
		}

		public void Dispose()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
			RenderPipelineManager.endCameraRendering -= OnEndCamera;
			Restore();
			if (startMasksNative.IsCreated) startMasksNative.Dispose();
			if (startFound.IsCreated) startFound.Dispose();
		}

		private void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
		{
			Restore();

			// a parallel view has no eye to walk out from
			if (cam.cameraType != CameraType.Game || cam.orthographic || (cam.cullingMask & layerMask) == 0)
				return;

			using (CullMarker.Auto())
				Cull(cam);
		}

		private void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
		{
			if (cam == hiddenFor) Restore();
		}

		private void Restore()
		{
			foreach (Chunk chunk in hidden)
				if (chunk) chunk.meshRenderer.forceRenderingOff = false;

			hidden.Clear();
			hiddenFor = null;
		}

		private void Cull(Camera cam)
		{
			EnvScanner scanner = EnvScanner.Instance;
			IReadOnlyList<Chunk> chunks = mesher.ChunksList;
			if (!scanner || chunks.Count == 0 || !LoadPlanes(cam)) return;

			float3 eye = cam.transform.position;
			int3 camCoord = scanner.WorldPosToChunkCoord(eye);
			int3 min = camCoord, max = camCoord;
			foreach (Chunk chunk in chunks)
			{
				int3 c = scanner.ChunkIndexToChunkCoord(chunk.chunkIndex);
				min = math.min(min, c);
				max = math.max(max, c);
			}

			int3 dims = max - min + 1;
			long cellCount = (long)dims.x * dims.y * dims.z;
			if (cellCount > MaxCells) return;

			if (reached.Length < cellCount) reached = new uint[cellCount];
			else Array.Clear(reached, 0, (int)cellCount);

			Walk(scanner, eye, camCoord, min, max, dims);

			int visible = 0;
			foreach (Chunk chunk in chunks)
			{
				if (!chunk.meshIsPopulated) continue;

				int3 c = scanner.ChunkIndexToChunkCoord(chunk.chunkIndex);
				if (reached[Cell(c, min, dims)] != 0)
				{
					visible++;
					continue;
				}

				chunk.meshRenderer.forceRenderingOff = true;
				hidden.Add(chunk);
			}

			VisibleCount = visible;
			CulledCount = hidden.Count;
			if (hidden.Count > 0) hiddenFor = cam;
		}

		private void Walk(EnvScanner scanner, float3 eye, int3 start, int3 min, int3 max, int3 dims)
		{
			queue.Clear();

			int startCell = Cell(start, min, dims);
			reached[startCell] = 1u << StartComponent;
			if (FindStartComponent(scanner, eye, start, out Chunk startChunkHere))
			{
				queue.Enqueue(new Step { coord = start, component = StartComponent });
			}
			else
			{
				// the camera sits in what reads as solid: walk out of every pocket of its chunk
				int count = startChunkHere ? startChunkHere.portalComponents : 1;
				for (int k = 0; k < count; k++)
				{
					reached[startCell] |= 1u << k;
					queue.Enqueue(new Step { coord = start, component = (byte)k });
				}
			}

			float chunkSize = scanner.ChunkWorldSizeDim;
			float voxSize = scanner.VoxSize;
			// the mesh reaches into the apron, a voxel past the chunk on each side
			float3 extents = (chunkSize + voxSize * 2) * 0.5f;

			while (queue.Count > 0)
			{
				Step s = queue.Dequeue();
				Portals(scanner, s.coord, out uint[] masks, out _);
				int offset = s.component * ChunkPortals.ComponentWords;
				if (s.component == StartComponent)
				{
					masks = startMasks;
					offset = 0;
				}

				for (int d = 0; d < ChunkPortals.FaceCount; d++)
				{
					// never back toward the camera
					if ((s.dirs & (1 << (d ^ 1))) != 0) continue;

					int from = offset + d * ChunkPortals.Span;
					if (!Any(masks, from)) continue;

					int3 n = s.coord + ChunkPortals.FaceSteps[d];
					if (math.any(n < min) || math.any(n > max)) continue;

					float3 corner = scanner.ChunkCoordToCornerWorldPos(n);
					if (!InFrustum(corner + chunkSize * 0.5f + voxSize * 0.5f, extents)) continue;

					// its face is in view, and a surface on it is drawn even when no empty
					// space leads on into it (a chunk solid right up to the face)
					int cell = Cell(n, min, dims);
					reached[cell] |= SeenBit;

					Portals(scanner, n, out uint[] nMasks, out int nCount);
					int entry = (d ^ 1) * ChunkPortals.Span;
					byte dirs = (byte)(s.dirs | (1 << d));
					for (int k = 0; k < nCount; k++)
					{
						if ((reached[cell] & (1u << k)) != 0) continue;
						if (!Overlap(masks, from, nMasks, k * ChunkPortals.ComponentWords + entry)) continue;

						reached[cell] |= 1u << k;
						queue.Enqueue(new Step { coord = n, component = (byte)k, dirs = dirs });
					}
				}
			}
		}

		private void Portals(EnvScanner scanner, int3 coord, out uint[] masks, out int count)
		{
			int index = scanner.ChunkCoordToChunkIndex(coord);
			if (index >= 0 && mesher.TryGetChunk(index, out Chunk chunk))
			{
				masks = chunk.portalMasks;
				count = chunk.portalComponents;
				return;
			}

			masks = ChunkPortals.Open;
			count = 1;
		}

		// floods the camera's chunk from the camera's voxel into startMasks
		private bool FindStartComponent(EnvScanner scanner, float3 eye, int3 coord, out Chunk chunk)
		{
			chunk = null;
			int index = scanner.ChunkCoordToChunkIndex(coord);
			if (index < 0 || !mesher.TryGetChunk(index, out chunk) || !chunk.voxelSignBits.IsCreated)
			{
				Array.Copy(ChunkPortals.Open, startMasks, ChunkPortals.ComponentWords);
				return true;
			}

			float3 corner = scanner.ChunkCoordToCornerWorldPos(coord);
			int3 voxel = math.clamp((int3)math.round((eye - corner) / scanner.VoxSize), 1, ChunkPortals.Span);

			if (chunk == startChunk && math.all(voxel == startVoxel) && chunk.portalRevision == startRevision)
				return startValid;

			new ChunkPortals.ContainingComponentJob
			{
				solidBits = chunk.voxelSignBits,
				voxel = voxel,
				masks = startMasksNative,
				found = startFound
			}.Run();

			startChunk = chunk;
			startVoxel = voxel;
			startRevision = chunk.portalRevision;
			startValid = startFound[0] != 0;
			if (startValid) startMasksNative.CopyTo(startMasks);
			return startValid;
		}

		private static bool Any(uint[] masks, int from)
		{
			for (int i = 0; i < ChunkPortals.Span; i++)
				if (masks[from + i] != 0) return true;
			return false;
		}

		private static bool Overlap(uint[] a, int aFrom, uint[] b, int bFrom)
		{
			for (int i = 0; i < ChunkPortals.Span; i++)
				if ((a[aFrom + i] & b[bFrom + i]) != 0) return true;
			return false;
		}

		private static int Cell(int3 c, int3 min, int3 dims)
		{
			int3 l = c - min;
			return l.x + dims.x * (l.y + dims.y * l.z);
		}

		// the planes the camera culls with, both eyes' together under XR, pushed out by
		// the margin so the walk never stops short of what is drawn
		private bool LoadPlanes(Camera cam)
		{
			if (!cam.TryGetCullingParameters(cam.stereoEnabled, out ScriptableCullingParameters p)) return false;

			planeCount = math.min(p.cullingPlaneCount, planes.Length);
			for (int i = 0; i < planeCount; i++)
			{
				Plane plane = p.GetCullingPlane(i);
				planes[i] = new float4(plane.normal, plane.distance + frustumMargin);
			}

			return true;
		}

		private bool InFrustum(float3 center, float3 extents)
		{
			for (int i = 0; i < planeCount; i++)
			{
				float4 plane = planes[i];
				float r = math.dot(extents, math.abs(plane.xyz));
				if (math.dot(plane.xyz, center) + plane.w + r < 0) return false;
			}

			return true;
		}
	}
}
