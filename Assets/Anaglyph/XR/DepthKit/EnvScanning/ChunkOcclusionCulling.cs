using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
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
		private NativeArray<uint> startMasks;
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
			startMasks = new NativeArray<uint>(ChunkPortals.ComponentWords, Allocator.Persistent);
			startFound = new NativeArray<int>(1, Allocator.Persistent);
			RenderPipelineManager.beginCameraRendering += OnBeginCamera;
			RenderPipelineManager.endCameraRendering += OnEndCamera;
		}

		public void Dispose()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
			RenderPipelineManager.endCameraRendering -= OnEndCamera;
			Restore();
			if (startMasks.IsCreated) startMasks.Dispose();
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
				int count = startChunkHere ? mesher.Portals.ComponentCount(mesher.Portals.SlotOf(startChunkHere.chunkIndex)) : 1;
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

			ChunkPortalStore portals = mesher.Portals;
			NativeArray<uint> storeMasks = portals.Masks;

			while (queue.Count > 0)
			{
				Step s = queue.Dequeue();
				NativeArray<uint> masks = storeMasks;
				int offset = ChunkPortalStore.MaskOffset(Slot(scanner, s.coord), s.component);
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
					if (!ChunkPortals.Any(masks, from)) continue;

					int3 n = s.coord + ChunkPortals.FaceSteps[d];
					if (math.any(n < min) || math.any(n > max)) continue;

					float3 corner = scanner.ChunkCoordToCornerWorldPos(n);
					if (!InFrustum(corner + chunkSize * 0.5f + voxSize * 0.5f, extents)) continue;

					// its face is in view, and a surface on it is drawn even when no empty
					// space leads on into it (a chunk solid right up to the face)
					int cell = Cell(n, min, dims);
					reached[cell] |= SeenBit;

					int nSlot = Slot(scanner, n);
					int nCount = portals.ComponentCount(nSlot);
					int entry = (d ^ 1) * ChunkPortals.Span;
					byte dirs = (byte)(s.dirs | (1 << d));
					for (int k = 0; k < nCount; k++)
					{
						if ((reached[cell] & (1u << k)) != 0) continue;
						if (!ChunkPortals.Overlap(masks, from, storeMasks, ChunkPortalStore.MaskOffset(nSlot, k) + entry)) continue;

						reached[cell] |= 1u << k;
						queue.Enqueue(new Step { coord = n, component = (byte)k, dirs = dirs });
					}
				}
			}
		}

		private int Slot(EnvScanner scanner, int3 coord)
		{
			int index = scanner.ChunkCoordToChunkIndex(coord);
			return index < 0 ? ChunkPortalStore.OpenSlot : mesher.Portals.SlotOf(index);
		}

		// floods the camera's chunk from the camera's voxel into startMasks
		private bool FindStartComponent(EnvScanner scanner, float3 eye, int3 coord, out Chunk chunk)
		{
			chunk = null;
			int index = scanner.ChunkCoordToChunkIndex(coord);
			if (index < 0 || !mesher.TryGetChunk(index, out chunk) || !chunk.voxelSignBits.IsCreated)
			{
				NativeArray<uint>.Copy(mesher.Portals.Masks, startMasks, ChunkPortals.ComponentWords);
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
				masks = startMasks,
				found = startFound
			}.Run();

			startChunk = chunk;
			startVoxel = voxel;
			startRevision = chunk.portalRevision;
			startValid = startFound[0] != 0;
			return startValid;
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
