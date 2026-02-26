using System;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Mathematics.Geometry;
using Unity.Profiling;
using UnityEngine;

namespace Meshia.MeshSimplification
{
	[BurstCompile(DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
	internal struct SimplifyJob : IJob
	{
		[BurstCompile]
		internal static class ProfilerMarkers
		{
			public static readonly ProfilerMarker MergeVertexAttributeData = new(nameof(MergeVertexAttributeData));

			public static readonly ProfilerMarker ResolveMergedVertexReferences =
				new(nameof(ResolveMergedVertexReferences));

			public static readonly ProfilerMarker CollectMergedVertexContainingTriangles =
				new(nameof(CollectMergedVertexContainingTriangles));

			public static readonly ProfilerMarker RecomputeMerges = new(nameof(RecomputeMerges));
			public static readonly ProfilerMarker ApplyMerge = new(nameof(ApplyMerge));

			public static readonly ProfilerMarker DiscardNonReferencedVertices =
				new(nameof(DiscardNonReferencedVertices));
		}

		public NativeArray<float3> VertexPositionBuffer;
		public NativeArray<float4> VertexNormalBuffer;
		public NativeArray<float4> VertexTangentBuffer;
		public NativeArray<float4> VertexColorBuffer;
		public NativeArray<float4> VertexTexCoord0Buffer;
		public NativeArray<float4> VertexTexCoord1Buffer;
		public NativeArray<float4> VertexTexCoord2Buffer;
		public NativeArray<float4> VertexTexCoord3Buffer;
		public NativeArray<float4> VertexTexCoord4Buffer;
		public NativeArray<float4> VertexTexCoord5Buffer;
		public NativeArray<float4> VertexTexCoord6Buffer;
		public NativeArray<float4> VertexTexCoord7Buffer;

		public NativeArray<float> VertexBlendWeightBuffer;
		public NativeArray<uint> VertexBlendIndicesBuffer;

		public NativeArray<uint> VertexContainingSubMeshIndices;


		public NativeList<BlendShapeData> BlendShapes;

		public NativeArray<int3> Triangles;
		public NativeArray<int> VertexVersions;
		public NativeArray<ErrorQuadric> VertexErrorQuadrics;
		public NativeParallelMultiHashMap<int, int> VertexContainingTriangles;
		public NativeParallelMultiHashMap<int, int> VertexMergeOpponentVertices;
		public NativeBitArray VertexIsBorderEdgeBits;
		public NativeMinPriorityQueue<VertexMerge> VertexMerges;
		public MeshSimplifierOptions Options;

		public NativeBitArray DiscardedVertex;

		public NativeBitArray DiscardedTriangle;
		public NativeBitArray PreserveBorderEdgesBoneIndices;

		public NativeArray<float3> TriangleNormals;
		public NativeHashSet<int2> SmartLinks;
		[ReadOnly] public Mesh.MeshData Mesh;

		public MeshSimplificationTarget SimplificationTarget;
		private int VertexCount;
		private int TriangleCount;

		private MergeFactory MergeFactory => new()
		{
			VertexPositionBuffer = VertexPositionBuffer,
			VertexBlendIndicesBuffer = VertexBlendIndicesBuffer,
			VertexErrorQuadrics = VertexErrorQuadrics,
			TriangleNormals = TriangleNormals,
			VertexContainingTriangles = VertexContainingTriangles,

			VertexIsBorderEdgeBits = VertexIsBorderEdgeBits,
			PreserveBorderEdgesBoneIndices = PreserveBorderEdgesBoneIndices,
			PreserveBorderEdges = Options.PreserveBorderEdges,
			PreserveSurfaceCurvature = Options.PreserveSurfaceCurvature
		};

		private PreservedVertexPredicator PreservedVertexPredicator => new()
		{
			VertexBlendIndicesBuffer = VertexBlendIndicesBuffer,
			VertexIsBorderEdgeBits = VertexIsBorderEdgeBits,
			PreserveBorderEdgesBoneIndices = PreserveBorderEdgesBoneIndices,
			VertexBoneCount = VertexBlendIndicesBuffer.Length / VertexPositionBuffer.Length,
			PreserveBorderEdges = Options.PreserveBorderEdges
		};

		public void Execute()
		{
			VertexCount = DiscardedVertex.Length - DiscardedVertex.CountBits(0, DiscardedVertex.Length);
			TriangleCount = DiscardedTriangle.Length - DiscardedTriangle.CountBits(0, DiscardedTriangle.Length);
			switch (SimplificationTarget.Kind)
			{
				case MeshSimplificationTargetKind.RelativeVertexCount:
				{
					int targetVertexCount = (int)(Mesh.vertexCount * SimplificationTarget.Value);
					while (targetVertexCount < VertexCount && VertexMerges.TryDequeue(out VertexMerge merge))
						if (IsValidMerge(merge))
							ApplyMerge(merge);
				}
					break;
				case MeshSimplificationTargetKind.AbsoluteVertexCount:
				{
					int targetVertexCount = (int)SimplificationTarget.Value;
					while (targetVertexCount < VertexCount && VertexMerges.TryDequeue(out VertexMerge merge))
						if (IsValidMerge(merge))
							ApplyMerge(merge);
				}
					break;
				case MeshSimplificationTargetKind.ScaledTotalError:
				{
					NativeSlice<float3> vertexPositions = Mesh.GetVertexPositions();

					MinMaxAABB bounds = new()
					{
						Max = float.NegativeInfinity,
						Min = float.PositiveInfinity
					};

					for (int vertexIndex = 0; vertexIndex < vertexPositions.Length; vertexIndex++)
					{
						if (DiscardedVertex.IsSet(vertexIndex)) continue;
						bounds.Encapsulate(vertexPositions[vertexIndex]);
					}

					if (!bounds.IsValid) return;

					float boundsScale = math.lengthsq(bounds.Extents);
					int vertexCountScale = Mesh.vertexCount;
					float maxTotalError = SimplificationTarget.Value * boundsScale * vertexCountScale;
					float totalError = 0f;

					while (VertexMerges.TryPeek(out VertexMerge merge) && totalError + merge.Cost < maxTotalError)
					{
						VertexMerges.Dequeue();
						if (IsValidMerge(merge))
						{
							ApplyMerge(merge);
							totalError += merge.Cost;
						}
					}
				}
					break;
				case MeshSimplificationTargetKind.AbsoluteTotalError:
				{
					float maxTotalError = SimplificationTarget.Value;
					float totalError = 0f;

					while (VertexMerges.TryPeek(out VertexMerge merge) && totalError + merge.Cost < maxTotalError)
					{
						VertexMerges.Dequeue();
						if (IsValidMerge(merge))
						{
							ApplyMerge(merge);
							totalError += merge.Cost;
						}
					}
				}
					break;

				case MeshSimplificationTargetKind.RelativeTriangleCount:
				{
					int targetTriangleCount = (int)(Triangles.Length * SimplificationTarget.Value);
					while (targetTriangleCount < TriangleCount && VertexMerges.TryDequeue(out VertexMerge merge))
						if (IsValidMerge(merge))
							ApplyMerge(merge);
				}
					break;
				case MeshSimplificationTargetKind.AbsoluteTriangleCount:
				{
					int targetTriangleCount = (int)SimplificationTarget.Value;
					while (targetTriangleCount < TriangleCount && VertexMerges.TryDequeue(out VertexMerge merge))
						if (IsValidMerge(merge))
							ApplyMerge(merge);
				}
					break;

				case MeshSimplificationTargetKind.AbsoluteIndividualError:
				{
					float maxIndividualError = SimplificationTarget.Value;

					while (VertexMerges.TryPeek(out VertexMerge merge) && merge.Cost < maxIndividualError)
					{
						VertexMerges.Dequeue();
						if (IsValidMerge(merge))
							ApplyMerge(merge);
					}
				}
					break;
			}
		}

		private readonly bool IsValidMerge(VertexMerge merge)
		{
			int vertexA = merge.VertexAIndex;
			int vertexB = merge.VertexBIndex;
			bool versionCheck = HasValidVersion(merge);
			if (!versionCheck) return false;
			if (WillMakeContainingTriangleFlipped(merge, vertexA, vertexB)) return false;
			if (WillMakeContainingTriangleFlipped(merge, vertexB, vertexA)) return false;
			return true;
		}

		private readonly bool HasValidVersion(VertexMerge merge)
		{
			return (merge.VertexAVersion == VertexVersions[merge.VertexAIndex]) &
			       (merge.VertexBVersion == VertexVersions[merge.VertexBIndex]);
		}

		private readonly bool WillMakeContainingTriangleFlipped(VertexMerge merge, int vertex, int opponentVertex)
		{
			foreach (int vertexAContainingTriangleIndex in VertexContainingTriangles.GetValuesForKey(vertex))
			{
				int3 triangle = Triangles[vertexAContainingTriangleIndex];

				if (math.any(triangle == opponentVertex)) continue;
				int vertex1, vertex2;

				if (triangle.x == vertex)
				{
					vertex1 = triangle.y;
					vertex2 = triangle.z;
				}
				else if (triangle.y == vertex)
				{
					vertex1 = triangle.z;
					vertex2 = triangle.x;
				}
				else
				{
					vertex1 = triangle.x;
					vertex2 = triangle.y;
				}

				float3x3 triangleVertexPositions = new()
				{
					c0 = merge.Position,
					c1 = VertexPositionBuffer[vertex1],
					c2 = VertexPositionBuffer[vertex2]
				};


				float3 originalTriangleNormal = TriangleNormals[vertexAContainingTriangleIndex];
				float3 triangleNormalAfterMerge = math.cross(triangleVertexPositions.c1 - triangleVertexPositions.c0,
					triangleVertexPositions.c2 - triangleVertexPositions.c0);
				triangleNormalAfterMerge = math.normalize(triangleNormalAfterMerge);
				float dot = math.dot(originalTriangleNormal, triangleNormalAfterMerge);

				if (dot < Options.MinNormalDot) return true;
			}

			return false;
		}

		private readonly bool IsDiscardedVertex(int vertex)
		{
			return DiscardedVertex.IsSet(vertex);
		}

		private void DiscardVertex(int vertex)
		{
			if (!IsDiscardedVertex(vertex))
			{
				DiscardedVertex.Set(vertex, true);
				VertexCount--;
				VertexVersions[vertex]++;
			}
		}

		private readonly bool IsDiscardedTriangle(int triangleIndex)
		{
			return DiscardedTriangle.IsSet(triangleIndex);
		}

		private void DiscardTriangle(int triangleIndex)
		{
			if (!IsDiscardedTriangle(triangleIndex))
			{
				DiscardedTriangle.Set(triangleIndex, true);
				TriangleCount--;
			}
		}

		public void ApplyMerge(VertexMerge merge)
		{
			using (ProfilerMarkers.ApplyMerge.Auto())
			{
				int vertexA = merge.VertexAIndex;
				int vertexB = merge.VertexBIndex;
				int2 vertexPair = new(math.min(vertexA, vertexB), math.max(vertexA, vertexB));

				bool isSmartLink = SmartLinks.Contains(vertexPair);

				bool vertexAIsBorderEdge = VertexIsBorderEdgeBits.IsSet(vertexA);
				bool vertexBIsBorderEdge = VertexIsBorderEdgeBits.IsSet(vertexB);

				bool containsBorderEdge = vertexAIsBorderEdge | vertexBIsBorderEdge;

				PreservedVertexPredicator preservedVertexPredicator = PreservedVertexPredicator;

				bool shouldPreserveVertexA = preservedVertexPredicator.IsPreserved(vertexA);

				bool shouldPreserveVertexB = preservedVertexPredicator.IsPreserved(vertexB);

				if (shouldPreserveVertexB) (vertexA, vertexB) = (vertexB, vertexA);

				if (!(shouldPreserveVertexA | shouldPreserveVertexB))
					MergeVertexAttributeData(vertexA, vertexB, merge.Position);

				VertexIsBorderEdgeBits.Set(vertexA, containsBorderEdge);
				VertexIsBorderEdgeBits.Set(vertexB, containsBorderEdge);


				VertexErrorQuadrics.ElementAt(vertexA) += VertexErrorQuadrics[vertexB];

				VertexVersions.ElementAt(vertexA)++;

				using UnsafeList<int> nonReferencedVertices = new(16, Allocator.Temp);

				using UnsafeList<int> vertexBContainingTriangles = new(16, Allocator.Temp);
				foreach (int triangleIndex in VertexContainingTriangles.GetValuesForKey(vertexB))
					vertexBContainingTriangles.Add(triangleIndex);

				VertexContainingTriangles.Remove(vertexB);
				nonReferencedVertices.Add(vertexB);

				// Replace reference to vertexB in triangles to vertexA.
				using (ProfilerMarkers.ResolveMergedVertexReferences.Auto())
				{
					using UnsafeList<int> discardingTriangles = new(vertexBContainingTriangles.Length, Allocator.Temp);
					using (ProfilerMarkers.CollectMergedVertexContainingTriangles.Auto())
					{
						foreach (int triangleIndex in vertexBContainingTriangles)
						{
							ref int3 triangleVertices = ref GetTriangleVertices(triangleIndex);


							// Replace vertexB in triangle to vertexA.
							if (math.any(triangleVertices == vertexA))
							{
								// Triangle vertices (a, b, ?) => (a, a, ?)
								// The triangle has only 2 vertices... discarding it
								discardingTriangles.Add(triangleIndex);
							}
							else
							{
								// Triangle vertices (b, ?, ?) => (a, ?, ?)
								triangleVertices = math.select(triangleVertices, vertexA, triangleVertices == vertexB);
								VertexContainingTriangles.Add(vertexA, triangleIndex);
							}
						}
					}


					foreach (int triangleIndex in discardingTriangles)
					{
						// Discard vertex which doesn't belong to any triangle.

						// We need to collect them to discardingTriangles temporary
						// because the vertex potentially gets new belonging triangle
						// while iterating.
						int3 triangleVertices = GetTriangleVertices(triangleIndex);
						for (int i = 0; i < 3; i++)
						{
							int vertex = triangleVertices[i];
							if (vertex == vertexB) continue;
							VertexContainingTriangles.Remove(vertex, triangleIndex);
							if (!VertexContainingTriangles.ContainsKey(vertex)) nonReferencedVertices.Add(vertex);
						}

						DiscardTriangle(triangleIndex);
					}
				}


				// Replace all reference to vertexB in merge opponents lookup.
				// Also, we need to recompute merges.

				if (!nonReferencedVertices.Contains(vertexA))
					using (ProfilerMarkers.RecomputeMerges.Auto())
					{
						foreach (int vertexAOpponentVertex in VertexMergeOpponentVertices.GetValuesForKey(vertexA))
							if (MergeFactory.TryComputeMerge(new int2(vertexA, vertexAOpponentVertex),
								    out float3 position, out float cost))
								VertexMerges.Enqueue(new VertexMerge
								{
									VertexAIndex = vertexA,
									VertexBIndex = vertexAOpponentVertex,
									VertexAVersion = VertexVersions[vertexA],
									VertexBVersion = VertexVersions[vertexAOpponentVertex],
									Position = position,
									Cost = cost
								});

						// Recompute merge with vertexB since it was merged into vertexA
						{
							using UnsafeList<int> vertexBOpponentVertices = new(16, Allocator.Temp);
							foreach (int vertexBOpponentVertex in VertexMergeOpponentVertices.GetValuesForKey(vertexB))
								vertexBOpponentVertices.Add(vertexBOpponentVertex);
							foreach (int vertexBOpponentVertex in vertexBOpponentVertices)
							{
								if (vertexBOpponentVertex == vertexA) continue;

								foreach (int vertexAOpponentVertex in VertexMergeOpponentVertices.GetValuesForKey(
									         vertexA))
									if (vertexBOpponentVertex == vertexAOpponentVertex)
										goto NextVertexBOpponent;

								if (MergeFactory.TryComputeMerge(new int2(vertexA, vertexBOpponentVertex),
									    out float3 position, out float cost))
								{
									VertexMerges.Enqueue(new VertexMerge
									{
										VertexAIndex = vertexA,
										VertexBIndex = vertexBOpponentVertex,
										VertexAVersion = VertexVersions[vertexA],
										VertexBVersion = VertexVersions[vertexBOpponentVertex],
										Position = position,
										Cost = cost
									});
									VertexMergeOpponentVertices.Add(vertexA, vertexBOpponentVertex);
									VertexMergeOpponentVertices.Add(vertexBOpponentVertex, vertexA);
								}

								NextVertexBOpponent: ;
							}
						}
					}

				using (ProfilerMarkers.DiscardNonReferencedVertices.Auto())
				{
					foreach (int nonReferencedVertex in nonReferencedVertices)
					{
						if (IsDiscardedVertex(nonReferencedVertex)) continue;
						using UnsafeList<int> opponentVertices = new(16, Allocator.Temp);
						foreach (int opponentVertex in VertexMergeOpponentVertices.GetValuesForKey(nonReferencedVertex))
							opponentVertices.Add(opponentVertex);
						VertexMergeOpponentVertices.Remove(nonReferencedVertex);
						foreach (int opponentVertex in opponentVertices)
							VertexMergeOpponentVertices.Remove(opponentVertex, nonReferencedVertex);
						DiscardVertex(nonReferencedVertex);
					}
				}
			}
		}

		private readonly ref int3 GetTriangleVertices(int triangleIndex)
		{
			return ref Triangles.ElementAt(triangleIndex);
		}

		private void MergeVertexAttributeData(int vertexA, int vertexB, float3 mergePosition)
		{
			using (ProfilerMarkers.MergeVertexAttributeData.Auto())
			{
				if (Options.UseBarycentricCoordinateInterpolation)
				{
					foreach (int vertexAContainingTriangleIndex in VertexContainingTriangles.GetValuesForKey(vertexA))
					{
						int3 triangle = Triangles[vertexAContainingTriangleIndex];
						if (math.any(triangle == vertexB))
						{
							float3x3 triangleVertexPositions = new()
							{
								c0 = VertexPositionBuffer[triangle.x],
								c1 = VertexPositionBuffer[triangle.y],
								c2 = VertexPositionBuffer[triangle.z]
							};


							float lerpFactor = ComputeLerpFactor(vertexA, vertexB, mergePosition);
							float3 barycentricCoordinate =
								ComputeBarycentricCoordinate(triangleVertexPositions, mergePosition);


							VertexPositionBuffer[vertexA] = mergePosition;
							MergeNormalVertexAttribute(VertexNormalBuffer, triangle, vertexA, barycentricCoordinate);
							MergeNormalVertexAttribute(VertexTangentBuffer, triangle, vertexA, barycentricCoordinate);

							MergeVectorVertexAttribute(VertexColorBuffer, triangle, vertexA, barycentricCoordinate);
							MergeVectorVertexAttribute(VertexTexCoord0Buffer, triangle, vertexA, barycentricCoordinate);
							MergeVectorVertexAttribute(VertexTexCoord1Buffer, triangle, vertexA, barycentricCoordinate);
							MergeVectorVertexAttribute(VertexTexCoord2Buffer, triangle, vertexA, barycentricCoordinate);
							MergeVectorVertexAttribute(VertexTexCoord3Buffer, triangle, vertexA, barycentricCoordinate);
							MergeVectorVertexAttribute(VertexTexCoord4Buffer, triangle, vertexA, barycentricCoordinate);
							MergeVectorVertexAttribute(VertexTexCoord5Buffer, triangle, vertexA, barycentricCoordinate);
							MergeVectorVertexAttribute(VertexTexCoord6Buffer, triangle, vertexA, barycentricCoordinate);
							MergeVectorVertexAttribute(VertexTexCoord7Buffer, triangle, vertexA, barycentricCoordinate);


							MergeBlendWeightAndIndices(vertexA, vertexB, lerpFactor);

							MergeBlendShapes(triangle, vertexA, barycentricCoordinate);
							return;
						}
					}
				}
				else
				{
					float lerpFactor = ComputeLerpFactor(vertexA, vertexB, mergePosition);

					VertexPositionBuffer[vertexA] = mergePosition;

					MergeNormalVertexAttribute(VertexNormalBuffer, vertexA, vertexB, lerpFactor);
					MergeNormalVertexAttribute(VertexTangentBuffer, vertexA, vertexB, lerpFactor);

					MergeVectorVertexAttribute(VertexColorBuffer, vertexA, vertexB, lerpFactor);
					MergeVectorVertexAttribute(VertexTexCoord0Buffer, vertexA, vertexB, lerpFactor);
					MergeVectorVertexAttribute(VertexTexCoord1Buffer, vertexA, vertexB, lerpFactor);
					MergeVectorVertexAttribute(VertexTexCoord2Buffer, vertexA, vertexB, lerpFactor);
					MergeVectorVertexAttribute(VertexTexCoord3Buffer, vertexA, vertexB, lerpFactor);
					MergeVectorVertexAttribute(VertexTexCoord4Buffer, vertexA, vertexB, lerpFactor);
					MergeVectorVertexAttribute(VertexTexCoord5Buffer, vertexA, vertexB, lerpFactor);
					MergeVectorVertexAttribute(VertexTexCoord6Buffer, vertexA, vertexB, lerpFactor);
					MergeVectorVertexAttribute(VertexTexCoord7Buffer, vertexA, vertexB, lerpFactor);

					MergeBlendWeightAndIndices(vertexA, vertexB, lerpFactor);

					MergeBlendShapes(vertexA, vertexB, lerpFactor);
				}
			}
		}

		private readonly float ComputeLerpFactor(int vertexA, int vertexB, float3 mergePosition)
		{
			float3 a = VertexPositionBuffer[vertexA];
			float3 b = VertexPositionBuffer[vertexB];
			float3 c = mergePosition;
			float3 ab = b - a;
			float3 ac = c - a;
			return math.saturate(math.dot(ab, ac) / math.lengthsq(ab));
		}

		private readonly float3 ComputeBarycentricCoordinate(float3x3 triangleVertexPositions, float3 position)
		{
			float3 AB = triangleVertexPositions.c1 - triangleVertexPositions.c0;
			float3 AC = triangleVertexPositions.c2 - triangleVertexPositions.c0;
			float3 AP = position - triangleVertexPositions.c0;

			float dotABAB = math.dot(AB, AB);
			float dotABAC = math.dot(AB, AC);
			float dotACAC = math.dot(AC, AC);
			float dotAPAB = math.dot(AP, AB);
			float dotAPAC = math.dot(AP, AC);
			float denom = dotABAB * dotACAC - dotABAC * dotABAC;

			// Make sure the denominator is not too small to cause math problems
			const float DenomEpilson = 0.00000001f;
			if (math.abs(denom) < DenomEpilson) denom = DenomEpilson;

			float y = (dotACAC * dotAPAB - dotABAC * dotAPAC) / denom;
			float z = (dotABAB * dotAPAC - dotABAC * dotAPAB) / denom;
			float x = 1 - y - z;
			return new float3(x, y, z);
		}

		private static void MergeVectorVertexAttribute(Span<float4> vertexAttributeData, int vertexA, int vertexB,
			float lerpFactor)
		{
			if (!vertexAttributeData.IsEmpty)
				vertexAttributeData[vertexA] =
					math.lerp(vertexAttributeData[vertexA], vertexAttributeData[vertexB], lerpFactor);
		}

		private static void MergeVectorVertexAttribute(Span<float4> vertexAttributeData, int3 triangle,
			int destinationVertex, float3 barycentricCoordinate)
		{
			if (!vertexAttributeData.IsEmpty)
				vertexAttributeData[destinationVertex] = vertexAttributeData[triangle.x] * barycentricCoordinate.x +
				                                         vertexAttributeData[triangle.y] * barycentricCoordinate.y +
				                                         vertexAttributeData[triangle.z] * barycentricCoordinate.z;
		}

		private static void MergeNormalVertexAttribute(Span<float4> vertexAttributeData, int vertexA, int vertexB,
			float lerpFactor)
		{
			if (!vertexAttributeData.IsEmpty)
				vertexAttributeData[vertexA].xyz = math.normalizesafe(math.lerp(vertexAttributeData[vertexA].xyz,
					vertexAttributeData[vertexB].xyz, lerpFactor));
		}

		private static void MergeNormalVertexAttribute(Span<float4> vertexAttributeData, int3 triangle,
			int destinationVertex, float3 barycentricCoordinate)
		{
			if (!vertexAttributeData.IsEmpty)
				vertexAttributeData[destinationVertex].xyz = math.normalizesafe(
					vertexAttributeData[triangle.x].xyz * barycentricCoordinate.x +
					vertexAttributeData[triangle.y].xyz * barycentricCoordinate.y +
					vertexAttributeData[triangle.z].xyz * barycentricCoordinate.z);
		}

		private void MergeBlendShapes(int vertexA, int vertexB, float lerpFactor)
		{
			for (int shapeIndex = 0; shapeIndex < BlendShapes.Length; shapeIndex++)
			{
				UnsafeList<BlendShapeFrameData> frames = BlendShapes[shapeIndex].Frames;
				for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
				{
					BlendShapeFrameData frame = frames[frameIndex];
					UnsafeList<float3> deltaVertices = frame.DeltaVertices;
					UnsafeList<float3> deltaNormals = frame.DeltaNormals;
					UnsafeList<float3> deltaTangents = frame.DeltaTangents;
					deltaVertices[vertexA] = math.lerp(deltaVertices[vertexA], deltaVertices[vertexB], lerpFactor);


					deltaNormals[vertexA] =
						math.normalizesafe(math.lerp(deltaNormals[vertexA], deltaNormals[vertexB], lerpFactor));

					deltaTangents[vertexA] =
						math.normalizesafe(math.lerp(deltaTangents[vertexA], deltaTangents[vertexB], lerpFactor));
				}
			}
		}


		private void MergeBlendShapes(int3 triangle, int destinationVertex, float3 barycentricCoordinate)
		{
			for (int shapeIndex = 0; shapeIndex < BlendShapes.Length; shapeIndex++)
			{
				UnsafeList<BlendShapeFrameData> frames = BlendShapes[shapeIndex].Frames;
				for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
				{
					BlendShapeFrameData frame = frames[frameIndex];
					UnsafeList<float3> deltaVertices = frame.DeltaVertices;
					UnsafeList<float3> deltaNormals = frame.DeltaNormals;
					UnsafeList<float3> deltaTangents = frame.DeltaTangents;
					deltaVertices[destinationVertex] = deltaVertices[triangle.x] * barycentricCoordinate.x +
					                                   deltaVertices[triangle.y] * barycentricCoordinate.y +
					                                   deltaVertices[triangle.z] * barycentricCoordinate.z;
					deltaNormals[destinationVertex] = math.normalizesafe(
						deltaNormals[triangle.x] * barycentricCoordinate.x +
						deltaNormals[triangle.y] * barycentricCoordinate.y +
						deltaNormals[triangle.z] * barycentricCoordinate.z);
					deltaTangents[destinationVertex] = math.normalizesafe(
						deltaTangents[triangle.x] * barycentricCoordinate.x +
						deltaTangents[triangle.y] * barycentricCoordinate.y +
						deltaTangents[triangle.z] * barycentricCoordinate.z);
				}
			}
		}

		[SkipLocalsInit]
		private void MergeBlendWeightAndIndices(int vertexA, int vertexB, float lerpFactor)
		{
			if (VertexBlendWeightBuffer.Length != 0 && VertexBlendIndicesBuffer.Length != 0)
			{
				Span<float> vertexBlendWeights = VertexBlendWeightBuffer.AsSpan();
				Span<uint> vertexBlendIndices = VertexBlendIndicesBuffer.AsSpan();

				int dimension = VertexBlendIndicesBuffer.Length / VertexPositionBuffer.Length;

				Span<float> blendWeightsAB = stackalloc float[dimension * 2];
				Span<uint> blendIndicesAB = stackalloc uint[dimension * 2];

				Span<float> blendWeightsA = blendWeightsAB[..dimension];
				vertexBlendWeights.Slice(vertexA * dimension, dimension).CopyTo(blendWeightsA);
				Span<float> blendWeightsB = blendWeightsAB.Slice(dimension, dimension);
				vertexBlendWeights.Slice(vertexB * dimension, dimension).CopyTo(blendWeightsB);

				Span<uint> blendIndicesA = blendIndicesAB[..dimension];
				vertexBlendIndices.Slice(vertexA * dimension, dimension).CopyTo(blendIndicesA);
				Span<uint> blendIndicesB = blendIndicesAB.Slice(dimension, dimension);
				vertexBlendIndices.Slice(vertexB * dimension, dimension).CopyTo(blendIndicesB);

				foreach (ref float weightA in blendWeightsA) weightA *= 1 - lerpFactor;
				foreach (ref float weightB in blendWeightsB) weightB *= lerpFactor;

				for (int a = 0; a < blendIndicesA.Length; a++)
				{
					uint indexA = blendIndicesA[a];

					for (int b = 0; b < blendIndicesB.Length; b++)
					{
						uint indexB = blendIndicesB[b];

						if (indexA == indexB)
						{
							ref float weightB = ref blendWeightsB[b];

							blendWeightsA[a] += weightB;
							weightB = float.NegativeInfinity;

							break;
						}
					}
				}

				Span<float> mergedBlendWeights = vertexBlendWeights.Slice(vertexA * dimension, dimension);
				Span<uint> mergedBlendIndices = vertexBlendIndices.Slice(vertexA * dimension, dimension);

				for (int mergedBlendWeightIndex = 0; mergedBlendWeightIndex < dimension; mergedBlendWeightIndex++)
				{
					int maxBlendWeightIndex = 0;
					float maxBlendWeight = blendWeightsAB[maxBlendWeightIndex];
					for (int i = 1; i < blendWeightsAB.Length; i++)
					{
						float blendWeight = blendWeightsAB[i];

						if (blendWeight > maxBlendWeight)
						{
							maxBlendWeight = blendWeight;
							maxBlendWeightIndex = i;
						}
					}

					mergedBlendWeights[mergedBlendWeightIndex] = maxBlendWeight;
					mergedBlendIndices[mergedBlendWeightIndex] = blendIndicesAB[maxBlendWeightIndex];

					blendWeightsAB[maxBlendWeightIndex] = float.NegativeInfinity;
				}

				float mergedBlendWeightSum = 0f;
				foreach (float weight in mergedBlendWeights) mergedBlendWeightSum += weight;

				float mergedBlendWeightNormalizer = math.rcp(mergedBlendWeightSum);

				foreach (ref float weight in mergedBlendWeights) weight *= mergedBlendWeightNormalizer;
			}
		}
	}


	internal struct PreservedVertexPredicator
	{
		public NativeArray<uint> VertexBlendIndicesBuffer;
		public NativeBitArray VertexIsBorderEdgeBits;
		public NativeBitArray PreserveBorderEdgesBoneIndices;
		public int VertexBoneCount;
		public bool PreserveBorderEdges;

		public readonly bool IsPreserved(int vertexIndex)
		{
			if (VertexIsBorderEdgeBits.IsSet(vertexIndex))
			{
				if (PreserveBorderEdges) return true;
				if (VertexBlendIndicesBuffer.Length > 0 && PreserveBorderEdgesBoneIndices.Length > 0)
				{
					NativeArray<uint> vertexBlendIndices =
						VertexBlendIndicesBuffer.GetSubArray(vertexIndex * VertexBoneCount, VertexBoneCount);
					for (int i = 0; i < vertexBlendIndices.Length; i++)
						if (PreserveBorderEdgesBoneIndices.IsSet((int)vertexBlendIndices[i]))
							return true;
				}
			}

			return false;
		}
	}
}