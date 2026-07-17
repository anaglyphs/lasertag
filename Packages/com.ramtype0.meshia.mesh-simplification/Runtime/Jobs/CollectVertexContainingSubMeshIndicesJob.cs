using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Meshia.MeshSimplification
{
    [BurstCompile]
    struct CollectVertexContainingSubMeshIndicesJob : IJob
    {
        [ReadOnly] public Mesh.MeshData Mesh;

        public NativeList<uint> VertexContainingSubMeshIndices;

        public void Execute()
        {
            VertexContainingSubMeshIndices.Resize(Mesh.vertexCount, NativeArrayOptions.ClearMemory);
            for (int subMeshIndex = 0; subMeshIndex < Mesh.subMeshCount; subMeshIndex++)
            {
                ProcessSubMesh(subMeshIndex);
            }
        }

        private void ProcessSubMesh(int subMeshIndex)
        {
            var subMesh = Mesh.GetSubMesh(subMeshIndex);
            uint bit = 1u << subMeshIndex;

            var subMeshVertexContainingSubMeshIndices = VertexContainingSubMeshIndices.AsArray().GetSubArray(subMesh.firstVertex, subMesh.vertexCount).AsSpan();

            for (int subMeshVertexIndex = 0; subMeshVertexIndex < subMeshVertexContainingSubMeshIndices.Length; subMeshVertexIndex++)
            {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                Loop.ExpectVectorized();
#endif
                subMeshVertexContainingSubMeshIndices[subMeshVertexIndex] |= bit;
            }

        }
    }
}