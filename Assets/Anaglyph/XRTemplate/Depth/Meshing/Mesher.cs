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
	public static class Mesher
	{

		private static readonly byte[] EdgeCornerA = {
			0, 1, 2, 3,
			4, 5, 6, 7,
			0, 1, 2, 3
		};

		private static readonly byte[] EdgeCornerB = {
			1, 2, 3, 0,
			5, 6, 7, 4,
			4, 5, 6, 7
		};

		private static readonly int3[] CornerCoords =
		{
			new(0, 0, 0),
			new(1, 0, 0),
			new(1, 0, 1),
			new(0, 0, 1),

			new(0, 1, 0),
			new(1, 1, 0),
			new(1, 1, 1),
			new(0, 1, 1),
		};

		private static readonly int3 X = new(1, 0, 0);
		private static readonly int3 Y = new(0, 1, 0);
		private static readonly int3 Z = new(0, 0, 1);

		// todo: cancellation
		public static async Task CreateMesh(NativeArray<sbyte> volume, int3 volumeSize, float metersPerVoxel, Mesh mesh)
		{
			int vertCountEstimate = volume.Length / 5;
			NativeList<float3> verts = new(vertCountEstimate, Allocator.TempJob);
			NativeList<int3> vertCoords = new(vertCountEstimate, Allocator.TempJob);
			
			NativeArray<uint> vertexIndices = new(volume.Length, Allocator.TempJob);

			int triCountEstimate = vertCountEstimate * 6;
			NativeList<uint> tris = new(triCountEstimate, Allocator.TempJob);

			try
			{
				VertexJob vertexMaker = new()
				{
					Volume = volume,
					VolumeSize = volumeSize,
					MetersPerVoxel = metersPerVoxel,

					VertexIndices = vertexIndices,
					Verts = verts.AsParallelWriter(),
					VertCoords = vertCoords.AsParallelWriter(),
				};

				JobHandle vertHandle = vertexMaker.ScheduleParallelByRef(volume.Length, 128, default);
				while (!vertHandle.IsCompleted) await Awaitable.NextFrameAsync();
				vertHandle.Complete();

				IndexJob triMaker = new()
				{
					VertCoords = vertCoords.AsArray(),
					VertIndices = vertexIndices,

					Volume = volume,
					VolumeSize = volumeSize,

					Tris = tris.AsParallelWriter(),
				};

				JobHandle triHandle = triMaker.ScheduleParallelByRef(vertCoords.Length, 256, vertHandle);
				while (!triHandle.IsCompleted) await Awaitable.NextFrameAsync();
				triHandle.Complete();

				ApplyToMesh(verts, tris, mesh);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}

			vertexIndices.Dispose();
			verts.Dispose();
			vertCoords.Dispose();
			tris.Dispose();
		}

		private static void ApplyToMesh(NativeList<float3> verts, NativeList<uint> tris, Mesh mesh)
		{
			const MeshUpdateFlags flags = MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds |
			                              MeshUpdateFlags.DontNotifyMeshUsers;

			VertexAttributeDescriptor vad = new (VertexAttribute.Position);
			mesh.SetVertexBufferParams(verts.Length, vad);
			mesh.SetVertexBufferData(verts.AsArray(), 0, 0, verts.Length, 0, flags);
			
			mesh.SetIndexBufferParams(tris.Length, IndexFormat.UInt32);
			mesh.SetIndexBufferData(tris.AsArray(), 0, 0, tris.Length, flags);
			
			mesh.subMeshCount = 1;
			SubMeshDescriptor smd = new(0, tris.Length);
			mesh.SetSubMesh(0, smd);
			
			mesh.RecalculateNormals(flags);

			mesh.RecalculateBounds();
		}
		
		[BurstCompile]
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

				if (coord.x == VolumeSize.x - 1 || coord.y == VolumeSize.y - 1 || coord.z == VolumeSize.z - 1)
					return;

				sbyte* values = stackalloc sbyte[8];
				float3* positions = stackalloc  float3[8];

				for (int i = 0; i < 8; i++)
				{
					int3 offs = coord + CornerCoords[i];
					int dataIndex = CoordToDataIndex(offs);
					values[i] = Volume[dataIndex];
					positions[i] = CoordToPos(offs);
				}

				float3 pos = new();
				byte numCrossings = 0;
				
				for (int i = 0; i < 12; i++)
				{
					byte a = EdgeCornerA[i];
					byte b = EdgeCornerB[i];

					sbyte va = values[a];
					sbyte vb = values[b];

					bool crosses = (va < 0) != (vb < 0);
					if (crosses)
					{
						float fa = va / 127f;
						float fb = vb / 127f;
						
						numCrossings++;

						float3 pa = positions[a];
						float3 pb = positions[b];
						
						float t = fa / (fa - fb);
						pos += pa + t * (pb - pa);
					}
				}

				if (numCrossings == 0) return;

				float inv = math.rcp(numCrossings);
				pos *= inv;

				UnsafeList<float3>* vertsUnsafe = Verts.ListData;
				if (vertsUnsafe->m_length >= vertsUnsafe->Capacity) return;
				var idx = Interlocked.Increment(ref vertsUnsafe->m_length) - 1;
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
				c.y = (i / s.x) % s.y;
				c.z = i / (s.x * s.y);
				return c;
			}
			
			private int CoordToDataIndex(int3 coord)
			{
				int3 c = coord;
				int3 s = VolumeSize;
				return c.x + c.y * s.x + c.z * s.x * s.y;
			}
			
			private float3 CoordToPos(int3 c)
			{
				return (float3)c * MetersPerVoxel;
			}
		}
		
		[BurstCompile]
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
				
				if(coord.x == 0 ||  coord.y == 0 || coord.z == 0)
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

				bool negA = (va < 0);
				bool negB = (vb < 0);

				if (negA != negB)
				{
					uint a = VertIndices[ia];
					uint b = VertIndices[CoordToDataIndex(coord - d1)];
					uint c = VertIndices[CoordToDataIndex(coord - (d1 + d2))];
					uint d = VertIndices[CoordToDataIndex(coord - d2)];

					UnsafeList<uint>* trisUnsafe = Tris.ListData;
					if (trisUnsafe->m_length >= trisUnsafe->Capacity) return;
					var idx = Interlocked.Add(ref trisUnsafe->m_length, 6) - 6;
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
			}
			
			private int CoordToDataIndex(int3 c)
			{
				int3 s = VolumeSize;
				return c.x + c.y * s.x + c.z * s.x * s.y;
			}
		}
	}
}