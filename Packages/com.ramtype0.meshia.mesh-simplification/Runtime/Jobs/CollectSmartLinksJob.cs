using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    [BurstCompile]
    struct CollectSmartLinksJob : IJob
    {
        public NativeArray<UnsafeList<int2>> SubMeshSmartLinkLists;
        public NativeHashSet<int2> SmartLinks;
        public void Execute()
        {
            var smartLinkCount = 0;
            for (int subMeshIndex = 0; subMeshIndex < SubMeshSmartLinkLists.Length; subMeshIndex++)
            {
                smartLinkCount += SubMeshSmartLinkLists[subMeshIndex].Length;
            }
            SmartLinks.Clear();
            if (SmartLinks.Capacity < smartLinkCount)
            {
                SmartLinks.Capacity = smartLinkCount;
            }
            for (int subMeshIndex = 0; subMeshIndex < SubMeshSmartLinkLists.Length; subMeshIndex++)
            {
                ref var subMeshSmartLinkList = ref SubMeshSmartLinkLists.ElementAt(subMeshIndex);
                for (int subMeshSmartLinkIndex = 0; subMeshSmartLinkIndex < subMeshSmartLinkList.Length; subMeshSmartLinkIndex++)
                {
                    var link = subMeshSmartLinkList[subMeshSmartLinkIndex];
                    SmartLinks.Add(link);
                }
                subMeshSmartLinkList.Dispose();
            }
        }
    }
}


