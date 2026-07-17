using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Meshia.MeshSimplification
{
    [BurstCompile]
    struct RemoveHighCostSmartLinksJob : IJobParallelForDefer
    {
        [ReadOnly]
        public NativeArray<float4> VertexNormalBuffer;
        [ReadOnly]
        public NativeArray<float4> VertexColorBuffer;
        [ReadOnly]
        public NativeArray<float4> VertexTexCoord0Buffer;
        [ReadOnly]
        public NativeArray<float4> VertexTexCoord1Buffer;
        [ReadOnly]
        public NativeArray<float4> VertexTexCoord2Buffer;
        [ReadOnly]
        public NativeArray<float4> VertexTexCoord3Buffer;
        [ReadOnly]
        public NativeArray<float4> VertexTexCoord4Buffer;
        [ReadOnly]
        public NativeArray<float4> VertexTexCoord5Buffer;
        [ReadOnly]
        public NativeArray<float4> VertexTexCoord6Buffer;
        [ReadOnly]
        public NativeArray<float4> VertexTexCoord7Buffer;

        public MeshSimplifierOptions Options;

        public NativeArray<UnsafeList<int2>> SubMeshSmartLinkLists;

        public void Execute(int subMeshIndex)
        {
            ref var subMeshSmartLinkList = ref SubMeshSmartLinkLists.ElementAt(subMeshIndex);
            var subMeshSmartLinkIndex = 0;

            var squaredMaxColorDistance = Options.VertexLinkColorDistance * Options.VertexLinkColorDistance;
            var squaredMaxUvDistance = Options.VertexLinkUvDistance * Options.VertexLinkUvDistance;

            while (subMeshSmartLinkIndex < subMeshSmartLinkList.Length)
            {
                var link = subMeshSmartLinkList[subMeshSmartLinkIndex];
                if (
                       (VertexNormalBuffer.Length == 0 || Options.VertexLinkMinNormalDot <= NormalDot(link))
                    && (VertexColorBuffer.Length == 0 || SquaredDistance(VertexColorBuffer, link) < squaredMaxColorDistance)
                    && (VertexTexCoord0Buffer.Length == 0 || SquaredDistance(VertexTexCoord0Buffer, link) < squaredMaxUvDistance)
                    && (VertexTexCoord1Buffer.Length == 0 || SquaredDistance(VertexTexCoord0Buffer, link) < squaredMaxUvDistance)
                    && (VertexTexCoord2Buffer.Length == 0 || SquaredDistance(VertexTexCoord0Buffer, link) < squaredMaxUvDistance)
                    && (VertexTexCoord3Buffer.Length == 0 || SquaredDistance(VertexTexCoord0Buffer, link) < squaredMaxUvDistance)
                    && (VertexTexCoord4Buffer.Length == 0 || SquaredDistance(VertexTexCoord0Buffer, link) < squaredMaxUvDistance)
                    && (VertexTexCoord5Buffer.Length == 0 || SquaredDistance(VertexTexCoord0Buffer, link) < squaredMaxUvDistance)
                    && (VertexTexCoord6Buffer.Length == 0 || SquaredDistance(VertexTexCoord0Buffer, link) < squaredMaxUvDistance)
                    && (VertexTexCoord7Buffer.Length == 0 || SquaredDistance(VertexTexCoord0Buffer, link) < squaredMaxUvDistance)
                    )
                {
                    subMeshSmartLinkIndex++;
                }
                else
                {
                    subMeshSmartLinkList.RemoveAtSwapBack(subMeshSmartLinkIndex);
                }
            }
        }
        float NormalDot(int2 link)
        {
            return math.dot(VertexNormalBuffer[link.x].xyz, VertexNormalBuffer[link.y].xyz);
        }
        static float SquaredDistance(NativeArray<float4> buffer, int2 link)
        {
            return math.distancesq(buffer[link.x], buffer[link.y]);
        }
    }
}


