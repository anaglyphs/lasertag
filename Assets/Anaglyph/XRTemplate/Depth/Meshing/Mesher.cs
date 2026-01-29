using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.DepthKit
{
	public static class NetMesher
	{
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

		private const float sbyteMax = sbyte.MaxValue;

		private static readonly int3 X = new(1, 0, 0);
		private static readonly int3 Y = new(0, 1, 0);
		private static readonly int3 Z = new(0, 0, 1);

		public static async Task<bool> CreateMesh(
			NativeArray<sbyte> volume, int3 volumeSize, float metersPerVoxel,
			Mesh mesh, CancellationToken ctkn = default)
		{
			bool hasTriangles = false;

			int vertCountEstimate = volume.Length / 3;
			NativeList<Vertex> verts = new(vertCountEstimate, Allocator.TempJob);
			NativeList<int3> vertCoords = new(vertCountEstimate, Allocator.TempJob);

			NativeArray<uint> vertexIndices = new(volume.Length, Allocator.TempJob);

			int triCountEstimate = vertCountEstimate * 6;
			NativeList<uint> tris = new(triCountEstimate, Allocator.TempJob);

			try
			{
				VertexJob vertexMaker = new()
				{
					Volume = volume,
					VoxelCount = volumeSize,
					VoxelSize = metersPerVoxel,

					VertIndices = vertexIndices,
					Verts = verts, //.AsParallelWriter(),
					VertCoords = vertCoords //.AsParallelWriter()
				};

				JobHandle vertHandle = vertexMaker.Schedule(); // .ScheduleParallelByRef(volume.Length, 128, default);
				while (!vertHandle.IsCompleted) await Task.Yield();

				vertHandle.Complete();
				ctkn.ThrowIfCancellationRequested();

				IndexJob triMaker = new()
				{
					VertCoords = vertCoords.AsArray(),
					VertIndices = vertexIndices,

					Volume = volume,
					VolumeSize = volumeSize,

					Tris = tris //.AsParallelWriter()
				};

				JobHandle triHandle = triMaker.Schedule(); //.ScheduleParallelByRef(vertCoords.Length, 256, vertHandle);
				while (!triHandle.IsCompleted) await Task.Yield();

				triHandle.Complete();
				ctkn.ThrowIfCancellationRequested();

				ApplyToMesh(verts, tris, mesh);

				hasTriangles = tris.Length > 0;
			}
			finally
			{
				vertexIndices.Dispose();
				verts.Dispose();
				vertCoords.Dispose();
				tris.Dispose();
			}

			return hasTriangles;
		}

		private static void ApplyToMesh(NativeArray<Vertex> verts, NativeArray<uint> tris,
			Mesh mesh)
		{
			const MeshUpdateFlags flags = MeshUpdateFlags.DontNotifyMeshUsers |
			                              MeshUpdateFlags.DontRecalculateBounds |
			                              MeshUpdateFlags.DontValidateIndices;

			mesh.SetVertexBufferParams(verts.Length, Vertex.Layout);
			mesh.SetVertexBufferData(verts, 0, 0, verts.Length, 0, flags);

			mesh.SetIndexBufferParams(tris.Length, IndexFormat.UInt32);
			mesh.SetIndexBufferData(tris, 0, 0, tris.Length, flags);

			mesh.subMeshCount = 1;
			SubMeshDescriptor smd = new(0, tris.Length);
			mesh.SetSubMesh(0, smd);

			// mesh.RecalculateNormals(flags);

			mesh.RecalculateBounds();
		}

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
			[ReadOnly] public int3 VoxelCount;
			[ReadOnly] public float VoxelSize;

			[WriteOnly] public NativeArray<uint> VertIndices;
			public NativeList<Vertex> Verts;
			public NativeList<int3> VertCoords;

			public void Execute()
			{
				int voxelCount = VoxelCount.x * VoxelCount.y * VoxelCount.z;

				for (int i = 0; i < voxelCount; i++)
				{
					int3 coord = IndexToCoord(i);

					if (coord.x == VoxelCount.x - 1 ||
					    coord.y == VoxelCount.y - 1 ||
					    coord.z == VoxelCount.z - 1)
						continue;

					float3 posCoord = default;
					float3 dir = default;
					byte numCrossings = 0;

					for (int e = 0; e < 12; e++)
					{
						int3 coordA = coord + CornerOffs[CrnrOffsIdxA[e]];
						int3 coordB = coord + CornerOffs[CrnrOffsIdxB[e]];

						float valA = ValueForCoord(coordA);
						float valB = ValueForCoord(coordB);

						float change = valA - valB;
						dir += new float3(coordA - coordB) * change;

						bool doesCross = valA < 0 != valB < 0;
						if (!doesCross) continue;

						float t = valA / change;
						float3 crossingCoord = coordA + t * new float3(coordB - coordA);

						posCoord += crossingCoord;
						numCrossings++;
					}

					if (numCrossings == 0)
						continue;

					posCoord /= numCrossings;
					float3 pos = CoordToPos(posCoord);
					float3 norm = math.normalize(dir);

					Vertex vert = new(pos, norm);

					VertIndices[i] = (uint)Verts.Length;
					Verts.Add(vert);
					VertCoords.Add(coord);
				}
			}

			private int3 IndexToCoord(int i)
			{
				int3 s = VoxelCount;
				return new int3(
					i % s.x,
					i / s.x % s.y,
					i / (s.x * s.y)
				);
			}

			private int CoordToIndex(int3 c)
			{
				int3 s = VoxelCount;
				return c.x + c.y * s.x + c.z * s.x * s.y;
			}

			private float3 CoordToPos(float3 c)
			{
				return c * VoxelSize + VoxelSize * 0.5f;
			}

			private float ValueForCoord(int3 c)
			{
				return Volume[CoordToIndex(c)] / sbyteMax;
			}
		}

		/*[BurstCompile]
		private struct VertexJob : IJobFor
		{
			[ReadOnly] public NativeArray<sbyte> Volume;
			[ReadOnly] public int3 VolumeSize;
			[ReadOnly] public float MetersPerVoxel;

			[WriteOnly] public NativeArray<uint> VertexIndices;
			[WriteOnly] public NativeList<float3>.ParallelWriter Verts;
			[WriteOnly] public NativeList<int3>.ParallelWriter VertCoords;

			public unsafe void Execute(int threadIdx)
			{
				int3 coord = ThreadIndexToCoord(threadIdx);

				if (coord.x == VolumeSize.x - 1 ||
				    coord.y == VolumeSize.y - 1 ||
				    coord.z == VolumeSize.z - 1)
					return;

				float3 posCoord = new();
				byte numCrossings = 0;

				for (int i = 0; i < 12; i++)
				{
					int3 coordA = coord + CornerOffs[CrnrOffsIdxA[i]];
					int3 coordB = coord + CornerOffs[CrnrOffsIdxB[i]];

					sbyte valA = Volume[CoordToIndex(coordA)];
					sbyte valB = Volume[CoordToIndex(coordB)];

					bool crossing = valA < 0 != valB < 0;
					if (crossing)
					{
						numCrossings++;

						float fValA = valA / sbyteMax;
						float fValB = valB / sbyteMax;
						float t = fValA / (fValA - fValB);
						posCoord += coordA + t * new float3(coordB - coordA);
					}
				}

				if (numCrossings == 0) return;

				posCoord /= numCrossings;

				float3 pos = CoordToPos(posCoord);

				UnsafeList<float3>* vertsUnsafe = Verts.ListData;
				if (vertsUnsafe->m_length >= vertsUnsafe->Capacity) return;
				int idx = Interlocked.Increment(ref vertsUnsafe->m_length) - 1;
				if (vertsUnsafe->m_length >= vertsUnsafe->Capacity) return; // capacity exceeded
				UnsafeUtility.WriteArrayElement(vertsUnsafe->Ptr, idx, pos);

				VertCoords.AddNoResize(coord);
				VertexIndices[threadIdx] = (uint)idx;
			}

			private int3 ThreadIndexToCoord(int i)
			{
				int3 c;
				int3 s = VolumeSize;
				c.x = i % s.x;
				c.y = i / s.x % s.y;
				c.z = i / (s.x * s.y);
				return c;
			}

			private int CoordToIndex(int3 c)
			{
				int3 s = VolumeSize;
				return c.x + c.y * s.x + c.z * s.x * s.y;
			}

			private float3 CoordToPos(float3 c)
			{
				return c * MetersPerVoxel + MetersPerVoxel / 2f;
			}
		}*/

		[BurstCompile]
		private struct IndexJob : IJob
		{
			[ReadOnly] public NativeArray<int3> VertCoords;
			[ReadOnly] public NativeArray<uint> VertIndices;

			[ReadOnly] public NativeArray<sbyte> Volume;
			[ReadOnly] public int3 VolumeSize;

			public NativeList<uint> Tris;

			public void Execute()
			{
				int count = VertCoords.Length;

				for (int i = 0; i < count; i++)
				{
					int3 coord = VertCoords[i];

					if (coord.x == 0 || coord.y == 0 || coord.z == 0)
						continue;

					TrisForAxis(coord, X, Z, Y);
					TrisForAxis(coord, Y, X, Z);
					TrisForAxis(coord, Z, Y, X);
				}
			}

			private void TrisForAxis(int3 coord, int3 axis, int3 d1, int3 d2)
			{
				int ia = CoordToIndex(coord);
				sbyte va = Volume[ia];

				int3 ca = coord + axis;
				int ib = CoordToIndex(ca);
				sbyte vb = Volume[ib];

				if (va < 0 == vb < 0)
					return;

				uint a = VertIndices[ia];
				uint b = VertIndices[CoordToIndex(coord - d1)];
				uint c = VertIndices[CoordToIndex(coord - (d1 + d2))];
				uint d = VertIndices[CoordToIndex(coord - d2)];

				Tris.Resize(Tris.Length + 6, NativeArrayOptions.ClearMemory);

				if (va < 0)
				{
					Tris.AddNoResize(c);
					Tris.AddNoResize(b);
					Tris.AddNoResize(a);
					Tris.AddNoResize(d);
					Tris.AddNoResize(c);
					Tris.AddNoResize(a);
				}
				else
				{
					Tris.AddNoResize(a);
					Tris.AddNoResize(c);
					Tris.AddNoResize(d);
					Tris.AddNoResize(a);
					Tris.AddNoResize(b);
					Tris.AddNoResize(c);
				}
			}

			private int CoordToIndex(int3 c)
			{
				int3 s = VolumeSize;
				return c.x + c.y * s.x + c.z * s.x * s.y;
			}
		}

		/*[BurstCompile]
		private struct IndexJob : IJobFor
		{
			[ReadOnly] public NativeArray<int3> VertCoords;
			[ReadOnly] public NativeArray<uint> VertIndices;

			[ReadOnly] public NativeArray<sbyte> Volume;
			[ReadOnly] public int3 VolumeSize;

			[WriteOnly] public NativeList<uint>.ParallelWriter Tris;

			public void Execute(int threadIndex)
			{
				int3 coord = VertCoords[threadIndex];

				if (coord.x == 0 || coord.y == 0 || coord.z == 0)
					return;

				TrisForAxis(coord, X, Z, Y);
				TrisForAxis(coord, Y, X, Z);
				TrisForAxis(coord, Z, Y, X);
			}

			private unsafe void TrisForAxis(int3 coord, int3 axis, int3 d1, int3 d2)
			{
				int ia = CoordToDataIndex(coord);
				sbyte va = Volume[ia];

				int3 ca = coord + axis;
				int ib = CoordToDataIndex(ca);
				sbyte vb = Volume[ib];

				bool negA = va < 0;
				bool negB = vb < 0;

				if (negA == negB) return;

				uint a = VertIndices[ia];
				uint b = VertIndices[CoordToDataIndex(coord - d1)];
				uint c = VertIndices[CoordToDataIndex(coord - (d1 + d2))];
				uint d = VertIndices[CoordToDataIndex(coord - d2)];

				UnsafeList<uint>* trisUnsafe = Tris.ListData;
				if (trisUnsafe->m_length >= trisUnsafe->Capacity) return;
				int idx = Interlocked.Add(ref trisUnsafe->m_length, 6) - 6;
				if (trisUnsafe->m_length >= trisUnsafe->Capacity) return; // capacity exceeded

				if (negA)
				{
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 0, c);
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 1, b);
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 2, a);
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 3, d);
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 4, c);
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 5, a);
				}
				else
				{
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 0, a);
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 1, c);
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 2, d);
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 3, a);
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 4, b);
					UnsafeUtility.WriteArrayElement(trisUnsafe->Ptr, idx + 5, c);
				}
			}

			private int CoordToDataIndex(int3 c)
			{
				int3 s = VolumeSize;
				return c.x + c.y * s.x + c.z * s.x * s.y;
			}
		}*/
	}
}