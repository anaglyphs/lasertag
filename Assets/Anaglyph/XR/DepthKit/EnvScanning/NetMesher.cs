using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	public class NetMesher : IDisposable
	{
		private NativeList<Vertex> verts;
		private NativeList<int3> vertCoords;
		private NativeArray<int> coordVertMap;
		private NativeList<uint> tris;
		private NativeArray<VertexAttributeDescriptor> vertexLayout;

		/// <summary>Indices the last completed <see cref="Schedule"/> wrote; 0 when the chunk holds no surface.</summary>
		public int IndexCount => tris.IsCreated ? tris.Length : 0;

		private static readonly byte[] CrnrOffsIdxA =
		{
			0, 1, 2, 3,
			4, 5, 6, 7,
			0, 1, 2, 3
		};

		private static readonly byte[] CrnrOffsIdxB =
		{
			1, 2, 3, 0,
			5, 6, 7, 4,
			4, 5, 6, 7
		};

		private static readonly int3[] CornerOffs =
		{
			new(0, 0, 0),
			new(1, 0, 0),
			new(1, 0, 1),
			new(0, 0, 1),

			new(0, 1, 0),
			new(1, 1, 0),
			new(1, 1, 1),
			new(0, 1, 1)
		};

		private const int InvalidVertSentinel = -1;

		private static readonly int3 X = new(1, 0, 0);
		private static readonly int3 Y = new(0, 1, 0);
		private static readonly int3 Z = new(0, 0, 1);

		[StructLayout(LayoutKind.Sequential)]
		public struct Vertex
		{
			public Vertex(float3 position, float3 normal)
			{
				pos = position;
				norm = normal;
			}

			public float3 pos;
			public float3 norm;

			public static readonly VertexAttributeDescriptor[] Layout =
			{
				new(VertexAttribute.Position),
				new(VertexAttribute.Normal)
			};
		}

		[BurstCompile]
		private struct VertexJob : IJob
		{
			[ReadOnly] public NativeArray<EnvScanner.Voxel> Volume;
			[ReadOnly] public int3 VoxCount;
			[ReadOnly] public float VoxSize;

			// maps coords as flat indices to vert indices 
			[WriteOnly] public NativeArray<int> CoordVertMap;
			public NativeList<Vertex> Verts;
			public NativeList<int3> VertCoords;

			public void Execute()
			{
				int voxelCount = VoxCount.x * VoxCount.y * VoxCount.z;

				for (int i = 0; i < voxelCount; i++)
				{
					int3 coord = IndexToCoord(i, VoxCount);

					if (coord.x == VoxCount.x - 1 ||
					    coord.y == VoxCount.y - 1 ||
					    coord.z == VoxCount.z - 1)
					{
						CoordVertMap[i] = InvalidVertSentinel;
						continue;
					}

					float3 posCoord = default;
					float3 dir = default;
					byte numCrossings = 0;
					byte numBadCrossings = 0;

					for (int e = 0; e < 12; e++)
					{
						int3 coordA = coord + CornerOffs[CrnrOffsIdxA[e]];
						int3 coordB = coord + CornerOffs[CrnrOffsIdxB[e]];

						EnvScanner.Voxel voxA = VoxelForCoord(coordA, VoxCount, Volume);
						EnvScanner.Voxel voxB = VoxelForCoord(coordB, VoxCount, Volume);

						float distA = voxA.CalcDistNorm();
						float distB = voxB.CalcDistNorm();

						float change = distA - distB;
						dir += new float3(coordA - coordB) * change;

						bool doesCross = distA < 0 != distB < 0;
						if (doesCross)
						{
							// cull false isosurface sign changes
							if (distA == 1 || distB == 1)
								numBadCrossings++;

							float t = distA / change;
							float3 crossingCoord = coordA + t * new float3(coordB - coordA);

							posCoord += crossingCoord;
							numCrossings++;
						}
					}

					if (numCrossings < 3 || numCrossings == numBadCrossings)
					{
						CoordVertMap[i] = InvalidVertSentinel;
						continue;
					}

					posCoord /= numCrossings;
					float3 pos = CoordToPos(posCoord);
					float3 norm = math.normalize(dir);

					Vertex vert = new(pos, norm);

					CoordVertMap[i] = Verts.Length;
					Verts.Add(vert);
					VertCoords.Add(coord);
				}
			}

			private float3 CoordToPos(float3 c)
			{
				return c * VoxSize + VoxSize * 0.5f;
			}
		}

		[BurstCompile]
		private struct IndexJob : IJob
		{
			[ReadOnly] public NativeArray<EnvScanner.Voxel> Volume;

			[ReadOnly] public int3 VoxCount;
			[ReadOnly] public NativeList<int3> VertCoords;
			[ReadOnly] public NativeArray<int> CoordVertMap;

			public NativeList<uint> Tris;

			public void Execute()
			{
				// fewer than three vertices can't make a triangle
				if (VertCoords.Length < 3) return;

				foreach (int3 coord in VertCoords)
				{
					TrisForAxis(coord, X, Z, Y);
					TrisForAxis(coord, Y, X, Z);
					TrisForAxis(coord, Z, Y, X);
				}
			}

			private void TrisForAxis(int3 coord, int3 axis, int3 d1, int3 d2)
			{
				if (math.any(coord - d1 < int3.zero)
				    || math.any(coord - d2 < int3.zero))
					return;

				int ia = FlattenCoord(coord, VoxCount);
				EnvScanner.Voxel voxA = VoxelForCoord(coord, VoxCount, Volume);
				EnvScanner.Voxel voxB = VoxelForCoord(coord + axis, VoxCount, Volume);

				float distA = voxA.CalcDistNorm();
				float distB = voxB.CalcDistNorm();

				if (distA < 0 == distB < 0) return;

				int a = CoordVertMap[ia];
				int b = CoordVertMap[FlattenCoord(coord - d1, VoxCount)];
				int c = CoordVertMap[FlattenCoord(coord - (d1 + d2), VoxCount)];
				int d = CoordVertMap[FlattenCoord(coord - d2, VoxCount)];

				if (a == InvalidVertSentinel || b == InvalidVertSentinel || c == InvalidVertSentinel ||
				    d == InvalidVertSentinel)
					return;

				if (distA < 0)
				{
					AddTriangle(c, b, a);
					AddTriangle(d, c, a);
				}
				else
				{
					AddTriangle(a, c, d);
					AddTriangle(a, b, c);
				}
			}

			private void AddTriangle(int a, int b, int c)
			{
				// a vertex can own up to three quads, so the list may outgrow its estimate
				Tris.Add((uint)a);
				Tris.Add((uint)b);
				Tris.Add((uint)c);
			}
		}

		private static int FlattenCoord(int3 coord, int3 voxCount)
		{
			int3 c = coord;
			int3 s = voxCount;
			return c.x + c.y * s.x + c.z * s.x * s.y;
		}

		private static EnvScanner.Voxel VoxelForCoord(int3 coord, int3 voxCount, NativeArray<EnvScanner.Voxel> volume)
		{
			return volume[FlattenCoord(coord, voxCount)];
		}

		private static int3 IndexToCoord(int i, int3 voxCount)
		{
			int3 s = voxCount;
			return new int3(
				i % s.x,
				i / s.x % s.y,
				i / (s.x * s.y)
			);
		}

		/// <summary>
		/// Writes the vertices and indices into a writable mesh, in a job, so the result never
		/// passes through a Mesh on the main thread before it is simplified
		/// </summary>
		[BurstCompile]
		private struct FillMeshDataJob : IJob
		{
			[ReadOnly] public NativeList<Vertex> Verts;
			[ReadOnly] public NativeList<uint> Tris;
			[ReadOnly] public NativeArray<VertexAttributeDescriptor> Layout;
			public float BoundsPadding;

			public Mesh.MeshData MeshData;

			public void Execute()
			{
				int indexCount = Tris.Length;
				// a surface with no triangles is no mesh at all
				int vertexCount = indexCount > 0 ? Verts.Length : 0;

				MeshData.SetVertexBufferParams(vertexCount, Layout);
				MeshData.SetIndexBufferParams(indexCount, IndexFormat.UInt32);

				if (vertexCount > 0)
					MeshData.GetVertexData<Vertex>().CopyFrom(Verts.AsArray());
				if (indexCount > 0)
					MeshData.GetIndexData<uint>().CopyFrom(Tris.AsArray());

				// the mesh's bounds are its submesh's, and the simplifier carries them over
				// unchanged. Padded, since decimation can place a vertex a little outside
				float3 min = float.PositiveInfinity, max = float.NegativeInfinity;
				for (int i = 0; i < vertexCount; i++)
				{
					min = math.min(min, Verts[i].pos);
					max = math.max(max, Verts[i].pos);
				}

				Bounds bounds = vertexCount > 0 ? new Bounds((min + max) * 0.5f, max - min + BoundsPadding * 2) : default;

				const MeshUpdateFlags flags = MeshUpdateFlags.DontNotifyMeshUsers |
				                              MeshUpdateFlags.DontRecalculateBounds |
				                              MeshUpdateFlags.DontValidateIndices;
				MeshData.subMeshCount = 1;
				MeshData.SetSubMesh(0, new SubMeshDescriptor(0, indexCount)
				{
					bounds = bounds,
					vertexCount = vertexCount
				}, flags);
			}
		}

		/// <summary>
		/// Schedules surface nets over <paramref name="volume"/> into <paramref name="destination"/>,
		/// one chain of jobs. Neither the volume nor this mesher may be touched until the returned
		/// handle completes; <see cref="IndexCount"/> is then the result's index count
		/// </summary>
		public JobHandle Schedule(NativeArray<EnvScanner.Voxel> volume, int3 voxCount, float voxSize,
			Mesh.MeshData destination, JobHandle dependency = default)
		{
			if (!verts.IsCreated)
			{
				// a surface crossing a chunk touches a few thousand of its cells, not a third
				int vertCountEstimate = volume.Length / 8;
				int triCountEstimate = vertCountEstimate * 6;

				verts = new NativeList<Vertex>(vertCountEstimate, Allocator.Persistent);
				vertCoords = new NativeList<int3>(vertCountEstimate, Allocator.Persistent);
				tris = new NativeList<uint>(triCountEstimate, Allocator.Persistent);
				vertexLayout = new NativeArray<VertexAttributeDescriptor>(Vertex.Layout, Allocator.Persistent);
			}

			if (!coordVertMap.IsCreated || coordVertMap.Length < volume.Length)
			{
				if (coordVertMap.IsCreated) coordVertMap.Dispose();
				coordVertMap = new NativeArray<int>(volume.Length, Allocator.Persistent);
			}

			verts.Clear();
			vertCoords.Clear();
			tris.Clear();

			JobHandle vertices = new VertexJob
			{
				Volume = volume,
				VoxCount = voxCount,
				VoxSize = voxSize,

				CoordVertMap = coordVertMap,
				Verts = verts,
				VertCoords = vertCoords
			}.Schedule(dependency);

			JobHandle indices = new IndexJob
			{
				VertCoords = vertCoords,
				CoordVertMap = coordVertMap,

				Volume = volume,
				VoxCount = voxCount,

				Tris = tris
			}.Schedule(vertices);

			return new FillMeshDataJob
			{
				Verts = verts,
				Tris = tris,
				Layout = vertexLayout,
				BoundsPadding = voxSize * 0.5f,
				MeshData = destination
			}.Schedule(indices);
		}

		public void Dispose()
		{
			if (verts.IsCreated) verts.Dispose();
			if (vertCoords.IsCreated) vertCoords.Dispose();
			if (coordVertMap.IsCreated) coordVertMap.Dispose();
			if (tris.IsCreated) tris.Dispose();
			if (vertexLayout.IsCreated) vertexLayout.Dispose();
		}
	}
}