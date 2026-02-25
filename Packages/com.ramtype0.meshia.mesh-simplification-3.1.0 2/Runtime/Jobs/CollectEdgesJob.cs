using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
namespace Meshia.MeshSimplification
{
    [BurstCompile]
    struct CollectEdgesJob : IJob
    {
        [ReadOnly]
        public NativeArray<int3> Triangles;

        public NativeHashSet<int2> Edges;

        public void Execute()
        {
            Edges.Clear();
            var maxEdgeCount = Triangles.Length * 3;
            if (Edges.Capacity < maxEdgeCount)
            {
                Edges.Capacity = maxEdgeCount;
            }
            foreach (var triangle in Triangles)
            {
                Edges.Add(triangle.xy);
                Edges.Add(triangle.yz);
                Edges.Add(triangle.zx);
            }
        }
    }
}


