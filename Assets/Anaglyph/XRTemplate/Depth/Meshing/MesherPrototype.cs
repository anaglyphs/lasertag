using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.DepthKit
{
	public class MesherPrototype
	{
		private readonly List<int3> coords = new();
		private readonly List<float3> verts = new();
		private readonly List<uint> tris = new();

		public float metersPerVoxel;
		public int3 voxelCount;
		[NonSerialized] public sbyte[] Data;
		
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
		
		private float3 CoordToPos(int3 coord)
		{
			return (float3)(coord) * metersPerVoxel;
		}

		private int CoordToDataIndex(int3 coord)
		{
			int3 c = coord;
			int3 s = voxelCount;

			return c.x + c.y * s.x + c.z * s.x * s.y;
		}

		public void BuildMesh(int3 start, int3 end, Mesh mesh)
		{
			verts.Clear();
			tris.Clear();
			coords.Clear();

			int3 size = end - start;
			uint[] vertIndices = new uint[size.x * size.y * size.z];
			Span<sbyte> values = stackalloc sbyte[8];
			Span<float3> positions = stackalloc float3[8];
			
			for (int x = start.x; x < end.x - 1; x++)
			for (int y = start.y; y < end.y - 1; y++)
			for (int z = start.z; z < end.z - 1; z++)
			{
				int3 coord = new(x, y, z);
				
				for (int i = 0; i < 8; i++)
				{
					int3 offs = coord + CornerCoords[i];
					int dataIndex = CoordToDataIndex(offs);
					values[i] = Data[dataIndex];
					positions[i] = CoordToPos(offs);
				}

				float3 pos = new();
				byte numCrossings = 0;
				
				for (int i = 0; i < 12; i++)
				{
					byte a = EdgeCornerA[i];
					byte b = EdgeCornerB[i];
					
					float fa = values[a] / 127f;
					float fb = values[b] / 127f;

					bool crosses = (fa < 0) != (fb < 0);
					if (crosses)
					{
						numCrossings++;

						float3 pa = positions[a];
						float3 pb = positions[b];
						
						float t = fa / (fa - fb);
						pos += pa + t * (pb - pa);
					}
				}
				
				if(numCrossings == 0) continue;
				
				float inv = math.rcp(numCrossings);
				pos *= inv;
				
				int index = verts.Count;
				verts.Add(pos);
				coords.Add(coord);

				int vertIndexIndex = CoordToVertIndex(coord);
				vertIndices[vertIndexIndex] = (uint)index;
			}
			
			foreach (int3 coord in coords)
			{
				if(coord.x == start.x ||  coord.y == start.y || coord.z == start.z)
					continue;

				TrisForAxis(X, Z, Y);
				TrisForAxis(Y, X, Z);
				TrisForAxis(Z, Y, X);
				continue;

				void TrisForAxis(int3 axis, int3 d1, int3 d2)
				{
					int i = CoordToDataIndex(coord);
					float f = Data[i] / 127f;
			
					int3 cx = coord + axis;
					int ix = CoordToDataIndex(cx);
					float fx = Data[ix] / 127f;
				
					uint a = vertIndices[CoordToVertIndex(coord)];
					uint b = vertIndices[CoordToVertIndex(coord - d1)];
					uint c = vertIndices[CoordToVertIndex(coord - (d1 + d2))];
					uint d = vertIndices[CoordToVertIndex(coord - d2)];
				
					if (f < 0 && fx > 0)
					{
						tris.Add(c);
						tris.Add(b);
						tris.Add(a);
						tris.Add(d);
						tris.Add(c);
						tris.Add(a);

					} else if (f > 0 && fx < 0)
					{
						tris.Add(a);
						tris.Add(c);
						tris.Add(d);
						tris.Add(a);
						tris.Add(b);
						tris.Add(c);
					}
				}
			}

			mesh.Clear();

			if (verts.Count == 0)
				return;

			float3[] vs = verts.ToArray();

			mesh.SetVertexBufferParams(vs.Length,
				new VertexAttributeDescriptor(VertexAttribute.Position));
			mesh.SetVertexBufferData(vs, 0, 0, vs.Length);
			
			uint[] ts = tris.ToArray();
			mesh.SetIndexBufferParams(ts.Length, IndexFormat.UInt32);
			mesh.SetIndexBufferData(ts, 0, 0, ts.Length);
			mesh.subMeshCount = 1;
			mesh.SetSubMesh(0, new SubMeshDescriptor(0, ts.Length));
			mesh.RecalculateNormals();
			mesh.RecalculateTangents();
			
			mesh.RecalculateBounds();
			return;

			int CoordToVertIndex(int3 coord)
			{
				int3 localCoord = coord - start;
				return localCoord.x + localCoord.y * size.x + localCoord.z * size.y * size.x;
			}
		}
	}
}






// debug display verts as points
// var ts = new int[vs.Length];
// for (int i = 0; i < vs.Length; i++)
// 	ts[i] = i;
// mesh.SetIndexBufferParams(ts.Length, IndexFormat.UInt32);
// mesh.SetIndexBufferData(ts, 0, 0, ts.Length);
// mesh.subMeshCount = 1;
// mesh.SetSubMesh(0, new SubMeshDescriptor(0, vs.Length, MeshTopology.Points));