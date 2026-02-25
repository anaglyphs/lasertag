using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;

namespace Meshia.MeshSimplification
{
	internal struct MergeFactory
	{
		public NativeArray<float3> VertexPositionBuffer;
		public NativeArray<uint> VertexBlendIndicesBuffer;
		public NativeArray<ErrorQuadric> VertexErrorQuadrics;
		public NativeParallelMultiHashMap<int, int> VertexContainingTriangles;
		public NativeBitArray VertexIsBorderEdgeBits;
		public NativeBitArray PreserveBorderEdgesBoneIndices;
		public NativeArray<float3> TriangleNormals;
		public bool PreserveBorderEdges;
		public bool PreserveSurfaceCurvature;

		private PreservedVertexPredicator PreservedVertexPredicator => new()
		{
			VertexBlendIndicesBuffer = VertexBlendIndicesBuffer,
			VertexIsBorderEdgeBits = VertexIsBorderEdgeBits,
			PreserveBorderEdgesBoneIndices = PreserveBorderEdgesBoneIndices,
			VertexBoneCount = VertexBlendIndicesBuffer.Length / VertexPositionBuffer.Length,
			PreserveBorderEdges = PreserveBorderEdges
		};

		[BurstCompile]
		private static class ProfilerMarkers
		{
			public static readonly ProfilerMarker TryComputeMerge = new(nameof(TryComputeMerge));
			public static readonly ProfilerMarker ComputeCurvatureError = new(nameof(ComputeCurvatureError));
		}

		public bool TryComputeMerge(int2 vertices, out float3 position, out float cost)
		{
			using (ProfilerMarkers.TryComputeMerge.Auto())
			{
				ErrorQuadric q = VertexErrorQuadrics[vertices.x] + VertexErrorQuadrics[vertices.y];

				float3 positionX = VertexPositionBuffer[vertices.x];
				float3 positionY = VertexPositionBuffer[vertices.y];

				float vertexError;

				PreservedVertexPredicator preservedVertexPredicator = PreservedVertexPredicator;

				bool preserveX = preservedVertexPredicator.IsPreserved(vertices.x);
				bool preserveY = preservedVertexPredicator.IsPreserved(vertices.y);
				if (preserveX && preserveY)
				{
					position = float.NaN;
					cost = float.PositiveInfinity;
					return false;
				}
				else if (preserveX)
				{
					position = positionX;
					goto ComputeVertexError;
				}
				else if (preserveY)
				{
					position = positionY;
					goto ComputeVertexError;
				}

				float determinant = q.Determinant1();
				if (determinant != 0)
				{
					position = new float3
					{
						x = -1 / determinant * q.Determinant2(),
						y = 1 / determinant * q.Determinant3(),
						z = -1 / determinant * q.Determinant4()
					};

					goto ComputeVertexError;
				}
				else
				{
					float3 positionZ = (positionX + positionY) * 0.5f;
					float errorX = q.ComputeError(positionX);
					float errorY = q.ComputeError(positionY);
					float errorZ = q.ComputeError(positionZ);

					if (errorX < errorY)
					{
						if (errorX < errorZ)
						{
							position = positionX;
							vertexError = errorX;
						}
						else
						{
							position = positionZ;
							vertexError = errorZ;
						}
					}
					else
					{
						if (errorY < errorZ)
						{
							position = positionY;
							vertexError = errorY;
						}
						else
						{
							position = positionZ;
							vertexError = errorZ;
						}
					}

					goto ApplyCurvatureError;
				}


				ComputeVertexError:
				vertexError = q.ComputeError(position);

				ApplyCurvatureError:
				float curvatureError = PreserveSurfaceCurvature ? ComputeCurvatureError(vertices) : 0;

				cost = vertexError + curvatureError;

				return true;
			}
		}

		private float ComputeCurvatureError(int2 vertices)
		{
			using (ProfilerMarkers.ComputeCurvatureError.Auto())
			{
				float distance = math.distance(VertexPositionBuffer[vertices.x], VertexPositionBuffer[vertices.y]);
				using UnsafeHashSet<int> vertexXContainingTriangles = new(8, Allocator.Temp);

				using UnsafeList<int> vertexXOrYContainingTriangles = new(16, Allocator.Temp);


				foreach (int vertexXContainingTriangle in VertexContainingTriangles.GetValuesForKey(vertices.x))
				{
					vertexXContainingTriangles.Add(vertexXContainingTriangle);
					vertexXOrYContainingTriangles.Add(vertexXContainingTriangle);
				}


				using UnsafeList<int> vertexXAndYContainingTriangles = new(8, Allocator.Temp);

				foreach (int vertexYContainingTriangle in VertexContainingTriangles.GetValuesForKey(vertices.y))
					if (vertexXContainingTriangles.Contains(vertexYContainingTriangle))
						vertexXAndYContainingTriangles.Add(vertexYContainingTriangle);
					else
						vertexXOrYContainingTriangles.Add(vertexYContainingTriangle);

				vertexXContainingTriangles.Dispose();

				float maxDot = 0f;

				foreach (int vertexXOrYContainingTriangle in vertexXOrYContainingTriangles)
				{
					float3 vertexXOrYContainingTriangleNormal = TriangleNormals[vertexXOrYContainingTriangle];

					foreach (int vertexXAndYContainingTriangle in vertexXAndYContainingTriangles)
					{
						float3 vertexXAndYContainingTriangleNormal = TriangleNormals[vertexXAndYContainingTriangle];
						float dot = math.dot(vertexXOrYContainingTriangleNormal, vertexXAndYContainingTriangleNormal);
						maxDot = math.max(dot, maxDot);
					}
				}

				return distance * maxDot;
			}
		}
	}
}