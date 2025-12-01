using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.DepthKit
{
	public class Marcher : MonoBehaviour
	{
		private List<float3> verts = new();
		private List<uint> tris = new();

		public float metersPerVoxel;
		public uint3 voxelCount;
		[NonSerialized] public sbyte[] data;
		
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

		private static readonly uint3[] CornerCoords =
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

		// 'start' or '0 coord' of a cube is its back bottom left corner
		private struct Cube
		{
			
		}


		

		private const float IsoLevel = 0;

		public unsafe void TriangulateVoxelRange(uint3 start, uint3 end, Mesh mesh)
		{
			verts.Clear();
			tris.Clear();

			// uint3 size = end - start;
			Span<sbyte> values = stackalloc sbyte[8];
			
			for (uint x = start.x; x < end.x - 1; x++)
			for (uint y = start.y; y < end.y - 1; y++)
			for (uint z = start.z; z < end.z - 1; z++)
			{
				uint3 coord = new(x, y, z);
				
				for (int i = 0; i < 8; i++)
				{
					uint3 offs = coord + CornerCoords[i];
					uint index = CoordToIndex(offs);
					values[i] = data[index];
				}

				float3 pos = new();
				byte numCrossings = 0;
				
				for (int i = 0; i < 12; i++)
				{
					byte a = EdgeCornerA[i];
					byte b = EdgeCornerB[i];
					
					float va = values[a] / 127f;
					float vb = values[b] / 127f;

					bool crosses = (va < 0) != (vb < 0);
					if (crosses)
					{
						numCrossings++;
						
						float3 pa = CoordsToPos(coord + CornerCoords[a]);
						float3 pb = CoordsToPos(coord + CornerCoords[b]);
						
						float t = va / (va - vb);
						pos += pa + t * (pb - pa);
					}
				}
				
				if(numCrossings == 0) continue; 
				
				pos /= numCrossings;
				
				// uint index = (uint)verts.Count;
				verts.Add(pos);
			}

			foreach (float3 v in verts)
			{
				uint3 coord = (uint3)(v * metersPerVoxel);
				
				
			}
			
			// foreach (uint3 coord in coords)
			// {
			// 	Cube c = GetCube(coord);
			// 	foreach (Edge e in c.edges)
			// 		if (e.signChange)
			// 		{
			// 			uint3 s = e.p1.coord;
			//
			// 			if (s.x >= cubes.GetLength(0) || s.y >= cubes.GetLength(1) || s.z >= cubes.GetLength(2))
			// 				continue;
			//
			// 			uint3 d1;
			// 			uint3 d2;
			//
			// 			switch (e.axis)
			// 			{
			// 				case Axis.X:
			// 					d1 = new uint3(0, 1, 0);
			// 					d2 = new uint3(0, 0, 1);
			// 					break;
			//
			// 				case Axis.Y:
			// 					d1 = new uint3(1, 0, 0);
			// 					d2 = new uint3(0, 0, 1);
			// 					break;
			//
			// 				case Axis.Z:
			// 					d1 = new uint3(1, 0, 0);
			// 					d2 = new uint3(0, 1, 0);
			// 					break;
			// 				default:
			// 					throw new ArgumentOutOfRangeException();
			// 			}
			//
			// 			Cube c0 = GetCube(s);
			// 			Cube c1 = GetCube(s - d1);
			// 			Cube c2 = GetCube(s - d1 - d2);
			// 			Cube c3 = GetCube(s - d2);
			//
			// 			tris.Add(c2.vertIndex);
			// 			tris.Add(c1.vertIndex);
			// 			tris.Add(c0.vertIndex);
			//
			// 			tris.Add(c3.vertIndex);
			// 			tris.Add(c2.vertIndex);
			// 			tris.Add(c0.vertIndex);
			// 		}
			// }

			mesh.Clear();

			if (verts.Count == 0)
				return;

			float3[] vs = verts.ToArray();

			mesh.SetVertexBufferParams(vs.Length,
				new VertexAttributeDescriptor(VertexAttribute.Position));
			mesh.SetVertexBufferData(vs, 0, 0, vs.Length);
			
			// uint[] ts = tris.ToArray();
			// mesh.SetIndexBufferParams(ts.Length, IndexFormat.UInt32);
			// mesh.SetIndexBufferData(ts, 0, 0, ts.Length);
			// mesh.subMeshCount = 1;
			// mesh.SetSubMesh(0, new SubMeshDescriptor(0, ts.Length));
			// mesh.RecalculateNormals();
			// mesh.RecalculateTangents();
			
			
			var ts = new int[vs.Length];
			for (int i = 0; i < vs.Length; i++)
				ts[i] = i;
			mesh.SetIndexBufferParams(ts.Length, IndexFormat.UInt32);
			mesh.SetIndexBufferData(ts, 0, 0, ts.Length);
			mesh.subMeshCount = 1;
			mesh.SetSubMesh(0, new SubMeshDescriptor(0, vs.Length, MeshTopology.Points));
			
			mesh.RecalculateBounds();
		}
		
		public float3 Lerp(float3 p1, float v1, float3 p2, float v2)
		{
			float mu = (IsoLevel - v1) / (v2 - v1);
			return p1 + mu * (p2 - p1);
		}


		private float3 CoordsToPos(uint3 coords)
		{
			return (float3)(coords) * metersPerVoxel;
		}

		private uint CoordToIndex(uint3 coord)
		{
			uint3 c = coord;
			uint3 s = voxelCount;

			return c.x + c.y * s.x + c.z * s.x * s.y;
		}
	}
}