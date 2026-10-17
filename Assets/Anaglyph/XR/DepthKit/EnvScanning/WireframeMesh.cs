using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// Builds draw-only copies of meshes for the debug wireframe shaders. Mobile GPUs
	/// have no cheap way to know where a pixel sits in its triangle (geometry shaders are
	/// slow on tilers and barycentric intrinsics are missing on Quest 2), so every
	/// triangle gets its own three vertices carrying (1,0,0), (0,1,0) and (0,0,1) in the
	/// vertex color, and the fragment shader draws edges where any of them nears zero.
	/// </summary>
	public static class WireframeMesh
	{
		private static readonly List<Vector3> sourcePositions = new();
		private static readonly List<int> sourceIndices = new();
		private static readonly List<Vector3> positions = new();
		private static readonly List<Color32> barycentrics = new();
		private static readonly List<int> indices = new();

		private static readonly Color32[] corners =
		{
			new(255, 0, 0, 255),
			new(0, 255, 0, 255),
			new(0, 0, 255, 255),
		};

		/// <summary>Fills <paramref name="target"/> with an unwelded, barycentric copy of <paramref name="source"/></summary>
		public static void Build(Mesh source, Mesh target)
		{
			target.Clear();
			if (source == null || source.vertexCount == 0) return;

			source.GetVertices(sourcePositions);
			sourceIndices.Clear();
			for (int s = 0; s < source.subMeshCount; s++)
				if (source.GetTopology(s) == MeshTopology.Triangles)
				{
					source.GetTriangles(indices, s);
					sourceIndices.AddRange(indices);
				}

			positions.Clear();
			barycentrics.Clear();
			indices.Clear();
			for (int i = 0; i < sourceIndices.Count; i++)
			{
				positions.Add(sourcePositions[sourceIndices[i]]);
				barycentrics.Add(corners[i % 3]);
				indices.Add(i);
			}

			target.indexFormat = positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
			target.SetVertices(positions);
			target.SetColors(barycentrics);
			target.SetIndices(indices, MeshTopology.Triangles, 0, false);
			target.bounds = source.bounds;
		}

		/// <summary>A new unwelded, barycentric copy of <paramref name="source"/></summary>
		public static Mesh Create(Mesh source)
		{
			Mesh mesh = new() { name = source ? $"{source.name} (Wireframe)" : "Wireframe" };
			Build(source, mesh);
			return mesh;
		}
	}
}
