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

		private enum Axis : byte
		{
			X = 0,
			Y = 1,
			Z = 2
		}

		private struct Point
		{
			public uint3 coord;
			public float3 pos;
			public uint index;
			public float value;

			public Point(uint3 coord, Marcher marcher)
			{
				this.coord = coord;
				pos = marcher.CoordsToPos(coord);
				index = marcher.CoordToIndex(coord);
				value = marcher.GetValue(index);
			}
		}

		private struct Edge
		{
			public Point p1;
			public Point p2;
			public bool signChange;
			public Axis axis;

			public Edge(Point p1, Point p2, Axis axis)
			{
				this.p1 = p1;
				this.p2 = p2;
				this.axis = axis;

				signChange = p1.value * p2.value < 0;
			}

			public float3 Lerp()
			{
				float mu = 0;
				float dif = p2.value - p1.value;

				if (dif < 0.0001)
					mu = 0.5f;
				else
					mu = (IsoLevel - p1.value) / dif;
				
				return p1.pos + mu * (p2.pos - p1.pos);
			}
		}

		// 'start' or '0 coord' of a cube is its back bottom left corner
		private struct Cube
		{
			public Edge[] edges;
			public uint vertIndex;

			public Cube(Edge[] edges)
			{
				this.edges = edges;
				vertIndex = 0;
			}
		}


		private Cube[,,] cubes;

		private Cube GetCube(uint3 coord)
		{
			return cubes[coord.x, coord.y, coord.z];
		}

		private const float IsoLevel = 0;

		public float GetValue(uint index)
		{
			return math.max(data[index] / 127.0f, -1.0f);
		}

		public void TriangulateVoxelRange(uint3 start, uint3 end, Mesh mesh)
		{
			verts.Clear();
			List<uint3> coords = new(verts.Capacity);
			tris.Clear();

			uint3 size = end - start;

			cubes = new Cube[size.x - 1, size.y - 1, size.z - 1];

			for (uint x = start.x; x < end.x - 1; x++)
			for (uint y = start.y; y < end.y - 1; y++)
			for (uint z = start.z; z < end.z - 1; z++)
			{
				uint3 c0 =      new(x, y, z);
				uint3 c1 = c0 + new uint3(1, 0, 0);
				uint3 c2 = c0 + new uint3(1, 0, 1);
				uint3 c3 = c0 + new uint3(0, 0, 1);
				uint3 c4 = c0 + new uint3(0, 1, 0);
				uint3 c5 = c0 + new uint3(1, 1, 0);
				uint3 c6 = c0 + new uint3(1, 1, 1);
				uint3 c7 = c0 + new uint3(0, 1, 1);

				Point p0 = new(c0, this); // start
				Point p1 = new(c1, this); // right
				Point p2 = new(c2, this); // right, forw
				Point p3 = new(c3, this); // forw
				Point p4 = new(c4, this); // up
				Point p5 = new(c5, this); // up, right
				Point p6 = new(c6, this); // up, right, forw
				Point p7 = new(c7, this); // up, forw

				Edge[] edges = new Edge[12];

				// going right
				edges[00] = new Edge(p0, p1, Axis.X); // start to right
				edges[01] = new Edge(p3, p2, Axis.X); // forw to forw right
				edges[02] = new Edge(p4, p5, Axis.X); // up to up right
				edges[03] = new Edge(p7, p6, Axis.X); // up forw to up forw right

				// going forw
				edges[04] = new Edge(p0, p3, Axis.Z); // start to forw
				edges[05] = new Edge(p1, p2, Axis.Z); // right to right forw
				edges[06] = new Edge(p4, p7, Axis.Z); // up to up forw
				edges[07] = new Edge(p5, p6, Axis.Z); // up right to up right forw

				// going up
				edges[08] = new Edge(p0, p4, Axis.Y); // start to up 
				edges[09] = new Edge(p1, p5, Axis.Y); // right to up right (going up)
				edges[10] = new Edge(p3, p7, Axis.Y); // forw to up forw (going up)
				edges[11] = new Edge(p2, p6, Axis.Y); // right forw to up right forw (going up)

				float3 point = new();
				int numSignChanges = 0;

				foreach (Edge e in edges)
					if (e.signChange)
					{
						numSignChanges++;
						point += e.Lerp();
					}

				point /= numSignChanges;
				
				if (numSignChanges > 0)
				{
					uint index = (uint)verts.Count;
					verts.Add(point);
					coords.Add(c0);
					
					Edge[] axiis = new Edge[3];
					axiis[0] = edges[0];
					axiis[1] = edges[4];
					axiis[2] = edges[8];
					
					Cube cube = new(axiis);
					cube.vertIndex = index;
					cubes[x, y, z] = cube;
				}
			}
			
			foreach (uint3 coord in coords)
			{
				Cube c = GetCube(coord);
				foreach (Edge e in c.edges)
					if (e.signChange)
					{
						uint3 s = e.p1.coord;
			
						if (s.x >= cubes.GetLength(0) || s.y >= cubes.GetLength(1) || s.z >= cubes.GetLength(2))
							continue;
			
						uint3 d1;
						uint3 d2;
			
						switch (e.axis)
						{
							case Axis.X:
								d1 = new uint3(0, 1, 0);
								d2 = new uint3(0, 0, 1);
								break;
			
							case Axis.Y:
								d1 = new uint3(1, 0, 0);
								d2 = new uint3(0, 0, 1);
								break;
			
							case Axis.Z:
								d1 = new uint3(1, 0, 0);
								d2 = new uint3(0, 1, 0);
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}
			
						Cube c0 = GetCube(s);
						Cube c1 = GetCube(s - d1);
						Cube c2 = GetCube(s - d1 - d2);
						Cube c3 = GetCube(s - d2);
			
						tris.Add(c2.vertIndex);
						tris.Add(c1.vertIndex);
						tris.Add(c0.vertIndex);
			
						tris.Add(c3.vertIndex);
						tris.Add(c2.vertIndex);
						tris.Add(c0.vertIndex);
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
			
			// var ts = new int[vs.Length];
			// for (int i = 0; i < vs.Length; i++)
			// 	ts[i] = i;
			// mesh.SetIndexBufferParams(ts.Length, IndexFormat.UInt32);
			// mesh.SetIndexBufferData(ts, 0, 0, ts.Length);
			// mesh.subMeshCount = 1;
			// mesh.SetSubMesh(0, new SubMeshDescriptor(0, vs.Length, MeshTopology.Points));
			
			mesh.RecalculateNormals();
			mesh.RecalculateTangents();
			mesh.RecalculateBounds();
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