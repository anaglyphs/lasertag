using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Profiling;
namespace Meshia.MeshSimplification
{
    struct MergeFactory
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

        PreservedVertexPredicator PreservedVertexPredicator => new()
        {
            VertexBlendIndicesBuffer = VertexBlendIndicesBuffer,
            VertexIsBorderEdgeBits = VertexIsBorderEdgeBits,
            PreserveBorderEdgesBoneIndices = PreserveBorderEdgesBoneIndices,
            VertexBoneCount = VertexBlendIndicesBuffer.Length / VertexPositionBuffer.Length,
            PreserveBorderEdges = PreserveBorderEdges,
        };

        [BurstCompile]
        static class ProfilerMarkers
        {
            public static readonly ProfilerMarker TryComputeMerge = new(nameof(TryComputeMerge));
            public static readonly ProfilerMarker ComputeCurvatureError = new(nameof(ComputeCurvatureError));
        }

        public bool TryComputeMerge(int2 vertices, out float3 position, out float cost)
        {
            using (ProfilerMarkers.TryComputeMerge.Auto())
            {
                var q = VertexErrorQuadrics[vertices.x] + VertexErrorQuadrics[vertices.y];

                var positionX = VertexPositionBuffer[vertices.x];
                var positionY = VertexPositionBuffer[vertices.y];

                float vertexError;

                var preservedVertexPredicator = PreservedVertexPredicator;

                var preserveX = preservedVertexPredicator.IsPreserved(vertices.x);
                var preserveY = preservedVertexPredicator.IsPreserved(vertices.y);
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

                var determinant = q.Determinant1();
                if (determinant != 0)
                {
                    position = new float3
                    {
                        x = -1 / determinant * q.Determinant2(),
                        y = 1 / determinant * q.Determinant3(),
                        z = -1 / determinant * q.Determinant4(),
                    };

                    goto ComputeVertexError;
                }
                else
                {
                    var positionZ = (positionX + positionY) * 0.5f;
                    var errorX = q.ComputeError(positionX);
                    var errorY = q.ComputeError(positionY);
                    var errorZ = q.ComputeError(positionZ);

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
                var curvatureError = PreserveSurfaceCurvature ? ComputeCurvatureError(vertices) : 0;

                cost = vertexError + curvatureError;
                return true;
            }

        }
        float ComputeCurvatureError(int2 vertices)
        {

            using (ProfilerMarkers.ComputeCurvatureError.Auto())
            {
                var distance = math.distance(VertexPositionBuffer[vertices.x], VertexPositionBuffer[vertices.y]);
                using UnsafeHashSet<int> vertexXContainingTriangles = new(8, Allocator.Temp);

                using UnsafeList<int> vertexXOrYContainingTriangles = new(16, Allocator.Temp);


                foreach (var vertexXContainingTriangle in VertexContainingTriangles.GetValuesForKey(vertices.x))
                {
                    vertexXContainingTriangles.Add(vertexXContainingTriangle);
                    vertexXOrYContainingTriangles.Add(vertexXContainingTriangle);
                }


                using UnsafeList<int> vertexXAndYContainingTriangles = new(8, Allocator.Temp);

                foreach (var vertexYContainingTriangle in VertexContainingTriangles.GetValuesForKey(vertices.y))
                {
                    if (vertexXContainingTriangles.Contains(vertexYContainingTriangle))
                    {
                        vertexXAndYContainingTriangles.Add(vertexYContainingTriangle);
                    }
                    else
                    {
                        vertexXOrYContainingTriangles.Add(vertexYContainingTriangle);
                    }
                }

                vertexXContainingTriangles.Dispose();

                var maxDot = 0f;

                foreach (var vertexXOrYContainingTriangle in vertexXOrYContainingTriangles)
                {
                    var vertexXOrYContainingTriangleNormal = TriangleNormals[vertexXOrYContainingTriangle];

                    foreach (var vertexXAndYContainingTriangle in vertexXAndYContainingTriangles)
                    {
                        var vertexXAndYContainingTriangleNormal = TriangleNormals[vertexXAndYContainingTriangle];
                        var dot = math.dot(vertexXOrYContainingTriangleNormal, vertexXAndYContainingTriangleNormal);
                        maxDot = math.max(dot, maxDot);
                    }
                }
                return distance * maxDot;
            }
        }
    }
}


