using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
namespace Meshia.MeshSimplification
{
    [BurstCompile]
    unsafe struct CollectVertexMergesJob : IJob
    {
        [ReadOnly]
        public NativeArray<VertexMerge> UnorderedDirtyVertexMerges;
        public NativeMinPriorityQueue<VertexMerge> VertexMerges;
        public void Execute()
        {
            ref var vertexMerges = ref VertexMerges.GetUnsafePriorityQueue()->nodes;
            vertexMerges.Clear();
            vertexMerges.SetCapacity(UnorderedDirtyVertexMerges.Length);

            for (int i = 0; i < UnorderedDirtyVertexMerges.Length; i++)
            {
                var merge = UnorderedDirtyVertexMerges[i];
                if (!float.IsPositiveInfinity(merge.Cost))
                {
                    vertexMerges.AddNoResize(merge);
                }
            }
            VertexMerges.GetUnsafePriorityQueue()->Heapify();
        }
    }
}


