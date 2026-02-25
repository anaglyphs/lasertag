using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Plane = Unity.Mathematics.Geometry.Plane;
namespace Meshia.MeshSimplification
{
    [BurstCompile]
    struct ComputeTriangleNormalsAndErrorQuadricsJob : IJobParallelForDefer
    {
        [ReadOnly]
        public NativeArray<float3> VertexPositionBuffer;
        [ReadOnly]
        public NativeArray<int3> Triangles;
        [WriteOnly]
        public NativeArray<float3> TriangleNormals;
        [WriteOnly]
        public NativeArray<ErrorQuadric> TriangleErrorQuadrics;
        public void Execute(int triangleIndex)
        {
            var triangle = Triangles[triangleIndex];
            var x = VertexPositionBuffer[triangle.x];
            var y = VertexPositionBuffer[triangle.y];
            var z = VertexPositionBuffer[triangle.z];

            var xy = y - x;
            var xz = z - x;
            var plane = new Plane(math.cross(xy, xz), x);

            TriangleNormals[triangleIndex] = plane.Normal;

            TriangleErrorQuadrics[triangleIndex] = new ErrorQuadric(plane);
        }
    }
}


