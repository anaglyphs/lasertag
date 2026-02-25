using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
namespace Meshia.MeshSimplification
{
    [BurstCompile]
    struct CollectVertexContainingTrianglesAndMarkInvalidTrianglesJob : IJob
    {
        [ReadOnly] public NativeArray<int3> Triangles;
        public NativeParallelMultiHashMap<int, int> VertexContainingTriangles;
        public NativeBitArray TriangleIsDiscardedBits;
        public void Execute()
        {
            VertexContainingTriangles.Clear();
            VertexContainingTriangles.Capacity = Triangles.Length * 3;
            TriangleIsDiscardedBits.Resize(Triangles.Length);

            for (int triangle = 0; triangle < Triangles.Length; triangle++)
            {
                var vertices = Triangles[triangle];

                if (vertices.x == vertices.y | vertices.y == vertices.z | vertices.z == vertices.x)
                {
                    TriangleIsDiscardedBits.Set(triangle, true);
                }
                else
                {
                    TriangleIsDiscardedBits.Set(triangle, false);

                    VertexContainingTriangles.Add(vertices.x, triangle);
                    VertexContainingTriangles.Add(vertices.y, triangle);
                    VertexContainingTriangles.Add(vertices.z, triangle);
                }
            }
        }
    }
}


