using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.DepthKit
{
	public class NetMesher : IDisposable
	{
		private NativeList<Vertex> verts;
		private NativeList<int3> vertCoords;
		private NativeArray<int> coordVertMap;
		private NativeList<uint> tris;

		private NativeReference<MinMaxAABB> boundsRef;

		private bool isBusy = false;
		public bool IsIsBusy => isBusy;

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
		private const float EmptyVoxel = -1.0f;
		private const float sbyteMax = sbyte.MaxValue;

		private static readonly int3 X = new(1, 0, 0);
		private static readonly int3 Y = new(0, 1, 0);
		private static readonly int3 Z = new(0, 0, 1);

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
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
			[ReadOnly] public NativeArray<sbyte> Volume;
			[ReadOnly] public int3 VoxCount;
			[ReadOnly] public float VoxSize;

			// maps coords as flat indices to vert indices 
			[WriteOnly] public NativeArray<int> CoordVertMap;
			public NativeList<Vertex> Verts;
			public NativeList<int3> VertCoords;

			public NativeReference<MinMaxAABB> BoundsRef;

			public void Execute()
			{
				int voxelCount = VoxCount.x * VoxCount.y * VoxCount.z;

				MinMaxAABB bounds = new();

				for (int i = 0; i < voxelCount; i++)
				{
					int3 coord = IndexToCoord(i);

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

						float valA = ValueForCoord(coordA);
						float valB = ValueForCoord(coordB);

						float change = valA - valB;
						dir += new float3(coordA - coordB) * change;

						bool doesCross = valA < 0 != valB < 0;
						if (doesCross)
						{
							// cull false isosurface sign changes
							if (valA == EmptyVoxel || valB == EmptyVoxel)
								numBadCrossings++;

							float t = valA / change;
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

					bounds.Encapsulate(pos);

					CoordVertMap[i] = Verts.Length;
					Verts.Add(vert);
					VertCoords.Add(coord);
				}

				BoundsRef.Value = bounds;
			}

			private int3 IndexToCoord(int i)
			{
				int3 s = VoxCount;
				return new int3(
					i % s.x,
					i / s.x % s.y,
					i / (s.x * s.y)
				);
			}

			private int CoordToIndex(int3 c)
			{
				int3 s = VoxCount;
				return c.x + c.y * s.x + c.z * s.x * s.y;
			}

			private float3 CoordToPos(float3 c)
			{
				return c * VoxSize + VoxSize * 0.5f;
			}

			private float ValueForCoord(int3 c)
			{
				float val = Volume[CoordToIndex(c)] / sbyteMax;
				if (val == EmptyVoxel) val = 0;
				return val;
			}
		}

		[BurstCompile]
		private struct IndexJob : IJob
		{
			[ReadOnly] public NativeArray<sbyte> Volume;

			[ReadOnly] public int3 VoxCount;
			[ReadOnly] public NativeList<int3> VertCoords;
			[ReadOnly] public NativeArray<int> CoordVertMap;

			public NativeList<uint> Tris;

			public void Execute()
			{
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

				int ia = Flatten(coord);
				float va = ValueForCoord(coord);
				float vb = ValueForCoord(coord + axis);

				if (va < 0 == vb < 0) return;

				int a = CoordVertMap[ia];
				int b = CoordVertMap[Flatten(coord - d1)];
				int c = CoordVertMap[Flatten(coord - (d1 + d2))];
				int d = CoordVertMap[Flatten(coord - d2)];

				if (a == InvalidVertSentinel || b == InvalidVertSentinel || c == InvalidVertSentinel ||
				    d == InvalidVertSentinel)
					return;

				if (va < 0)
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
				Tris.AddNoResize((uint)a);
				Tris.AddNoResize((uint)b);
				Tris.AddNoResize((uint)c);
			}

			private int Flatten(int3 c)
			{
				return FlattenCoord(c, VoxCount);
			}

			private float ValueForCoord(int3 c)
			{
				return Volume[Flatten(c)] / sbyteMax;
			}
		}

		private static int FlattenCoord(int3 coord, int3 voxCount)
		{
			int3 c = coord;
			int3 s = voxCount;
			return c.x + c.y * s.x + c.z * s.x * s.y;
		}

		public async Task<bool> CreateMesh(
			NativeArray<sbyte> volume,
			int3 voxCount, float voxSize, Mesh mesh,
			CancellationToken ctkn = default)
		{
			if (isBusy) throw new Exception("Mesher is busy");
			isBusy = true;

			if (!verts.IsCreated)
			{
				int vertCountEstimate = volume.Length / 3;
				int triCountEstimate = vertCountEstimate * 6;

				verts = new NativeList<Vertex>(vertCountEstimate, Allocator.Persistent);
				vertCoords = new NativeList<int3>(vertCountEstimate, Allocator.Persistent);
				tris = new NativeList<uint>(triCountEstimate, Allocator.Persistent);
				boundsRef = new NativeReference<MinMaxAABB>(Allocator.Persistent);

				coordVertMap = new NativeArray<int>(volume.Length, Allocator.Persistent);
			}

			if (coordVertMap.IsCreated && coordVertMap.Length < volume.Length)
			{
				coordVertMap.Dispose();
				coordVertMap = new NativeArray<int>(volume.Length, Allocator.Persistent);
			}

			verts.Clear();
			vertCoords.Clear();
			tris.Clear();

			boundsRef.Value = new MinMaxAABB();

			bool hasTriangles = false;

			try
			{
				VertexJob vertexMaker = new()
				{
					Volume = volume,
					VoxCount = voxCount,
					VoxSize = voxSize,

					CoordVertMap = coordVertMap,
					Verts = verts,
					VertCoords = vertCoords,

					BoundsRef = boundsRef
				};

				JobHandle vertHandle = vertexMaker.Schedule();
				while (!vertHandle.IsCompleted) await Awaitable.NextFrameAsync(ctkn);

				vertHandle.Complete();
				ctkn.ThrowIfCancellationRequested();

				if (verts.Length < 3)
					return false;

				IndexJob triMaker = new()
				{
					VertCoords = vertCoords,
					CoordVertMap = coordVertMap,

					Volume = volume,
					VoxCount = voxCount,
					// Verts = verts,

					Tris = tris
				};

				JobHandle triHandle = triMaker.Schedule();
				while (!triHandle.IsCompleted) await Awaitable.NextFrameAsync(ctkn);

				triHandle.Complete();
				ctkn.ThrowIfCancellationRequested();

				MinMaxAABB b = boundsRef.Value;

				Bounds bounds = new(b.Center, b.Extents);
				ApplyToMesh(verts.AsArray(), tris.AsArray(), bounds, mesh);

				hasTriangles = tris.Length > 0;
			}
			finally
			{
				isBusy = false;
			}

			return hasTriangles;
		}

		private static void ApplyToMesh(NativeArray<Vertex> verts, NativeArray<uint> tris,
			Bounds bounds, Mesh mesh)
		{
			Mesh.MeshDataArray meshDataArray = Mesh.AllocateWritableMeshData(1);
			Mesh.MeshData meshData = meshDataArray[0];

			meshData.SetVertexBufferParams(verts.Length, Vertex.Layout);
			meshData.SetIndexBufferParams(tris.Length, IndexFormat.UInt32);

			NativeArray<Vertex> vb = meshData.GetVertexData<Vertex>();
			vb.CopyFrom(verts);

			NativeArray<uint> ib = meshData.GetIndexData<uint>();
			ib.CopyFrom(tris);

			const MeshUpdateFlags flags = MeshUpdateFlags.DontNotifyMeshUsers |
			                              MeshUpdateFlags.DontRecalculateBounds |
			                              MeshUpdateFlags.DontValidateIndices;
			meshData.subMeshCount = 1;
			meshData.SetSubMesh(0, new SubMeshDescriptor(0, tris.Length), flags);

			Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);

			mesh.bounds = bounds;
			mesh.MarkModified();
		}

		public void Dispose()
		{
			verts.Dispose();
			vertCoords.Dispose();
			coordVertMap.Dispose();
			tris.Dispose();
			boundsRef.Dispose();
		}
	}
}