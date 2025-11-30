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
		public sbyte[] data;

		private enum Axis : byte
		{
			X = 0,
			Y = 1,
			Z = 2
		}

		private struct Point
		{
			public float3 pos;
			public uint index;
			public float value;

			public Point(uint3 coord, Marcher marcher)
			{
				pos = marcher.CoordsToPos(coord);
				index = marcher.CoordToIndex(coord);
				value = marcher.data[index];
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
				float mu = (IsoLevel - p1.value) / (p2.value - p1.value);
				return p1.pos + mu * (p2.pos - p1.pos);
			}
		}

		// 'start' or '0 coord' of a cube is its back bottom left corner
		private struct Cube
		{
			public Edge[] edges;
			public int vertIndex;

			public Cube(Edge[] edges)
			{
				this.edges = edges;
				vertIndex = -1;
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
			tris.Clear();

			uint3 size = end - start;

			cubes = new Cube[size.x - 1, size.y - 1, size.z - 1];

			Point[] points = new Point[8];
			Edge[] edges = new Edge[11];

			for (uint x = start.x; x < end.x - 1; x++)
			for (uint y = start.y; y < end.y - 1; y++)
			for (uint z = start.z; z < end.z - 1; z++)
			{
				uint3 p0 = new(x, y, z);
				uint3 p1 = p0 + new uint3(1, 0, 0);
				uint3 p2 = p0 + new uint3(1, 0, 1);
				uint3 p3 = p0 + new uint3(0, 0, 1);
				uint3 p4 = p0 + new uint3(0, 1, 0);
				uint3 p5 = p0 + new uint3(1, 1, 0);
				uint3 p6 = p0 + new uint3(1, 1, 1);
				uint3 p7 = p0 + new uint3(0, 1, 1);

				points[0] = new Point(p0, this); // start
				points[1] = new Point(p1, this); // right
				points[2] = new Point(p2, this); // right, forw
				points[3] = new Point(p3, this); // forw
				points[4] = new Point(p4, this); // up
				points[5] = new Point(p5, this); // up, right
				points[6] = new Point(p6, this); // up, right, forw
				points[7] = new Point(p7, this); // up, forw


				// going right
				edges[00] = new Edge(points[0], points[1], Axis.X); // start to right
				edges[01] = new Edge(points[3], points[2], Axis.X); // forw to forw right
				edges[02] = new Edge(points[4], points[5], Axis.X); // up to up right
				edges[03] = new Edge(points[7], points[6], Axis.X); // up forw to up forw right

				// going forw
				edges[04] = new Edge(points[0], points[3], Axis.Z); // start to forw
				edges[05] = new Edge(points[1], points[2], Axis.Z); // right to right forw
				edges[06] = new Edge(points[4], points[7], Axis.Z); // up to up forw
				edges[07] = new Edge(points[5], points[6], Axis.Z); // up right to up right forw

				// going up
				edges[08] = new Edge(points[0], points[4], Axis.Y); // start to up 
				edges[09] = new Edge(points[1], points[5], Axis.Y); // right to up right (going up)
				edges[10] = new Edge(points[3], points[7], Axis.Y); // forw to up forw (going up)
				edges[11] = new Edge(points[2], points[6], Axis.Y); // right forw to up right forw (going up)

				Cube cube = new(edges);

				float3 point = new();
				bool pointCreated = false;

				foreach (Edge e in edges)
					if (e.signChange)
					{
						pointCreated = true;
						point += e.Lerp();
					}

				if (pointCreated)
				{
					int index = verts.Count;
					verts.Add(point);
					cube.vertIndex = index;
				}

				cubes[x, y, z] = cube;
			}

			for (uint x = start.x + 1; x < end.x - 1; x++)
			for (uint y = start.y + 1; y < end.y - 1; y++)
			for (uint z = start.z + 1; z < end.z - 1; z++)
			{
				uint3 coord = new(x, y, z);
				Cube c0 = GetCube(coord);

				foreach (Edge e in edges)
					if (e.signChange)
					{
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

						Cube c1 = GetCube(coord + d1);
						Cube c2 = GetCube(coord + d1 + d2);
						Cube c3 = GetCube(coord + d2);

						tris.Add((uint)c0.vertIndex);
						tris.Add((uint)c1.vertIndex);
						tris.Add((uint)c2.vertIndex);

						tris.Add((uint)c3.vertIndex);
						tris.Add((uint)c2.vertIndex);
						tris.Add((uint)c0.vertIndex);
					}
			}

			mesh.Clear();

			if (verts.Count == 0)
				return;

			float3[] vs = verts.ToArray();
			uint[] ts = tris.ToArray();

			mesh.SetVertexBufferParams(vs.Length,
				new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
			mesh.SetIndexBufferParams(ts.Length, IndexFormat.UInt32);
			mesh.SetVertexBufferData(vs, 0, 0, vs.Length);
			mesh.SetIndexBufferData(ts, 0, 0, ts.Length);
		}


		private float3 CoordsToPos(uint3 coords)
		{
			return (float3)coords * metersPerVoxel;
		}

		private uint3 PosToCoords(float3 pos)
		{
			return (uint3)(pos / metersPerVoxel);
		}

		private uint CoordToIndex(uint3 coord)
		{
			uint3 c = coord;
			uint3 s = voxelCount;

			return c.x + c.y * s.x + c.z * s.x * s.y;
		}
	}
}