using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
namespace Meshia.MeshSimplification
{

    [BurstCompile]
    struct InitializeVertexListJob<T> : IJob
           where T : unmanaged
    {
        [ReadOnly]
        public Mesh.MeshData MeshData;
        public NativeArrayOptions Options;
        public NativeList<T> Buffer;
        public void Execute()
        {
            Buffer.Clear();
            Buffer.Resize(MeshData.vertexCount, Options);
        }
    }
    [BurstCompile]
    struct InitializeSubMeshListJob<T> : IJob
           where T : unmanaged
    {
        [ReadOnly]
        public Mesh.MeshData MeshData;
        public NativeArrayOptions Options;
        public NativeList<T> Buffer;
        public void Execute()
        {
            Buffer.Clear();
            Buffer.Resize(MeshData.subMeshCount, Options);
        }
    }

    [BurstCompile]
    struct InitializeVertexBitArrayJob : IJob
    {
        [ReadOnly]
        public Mesh.MeshData MeshData;
        public NativeBitArray Buffer;
        public void Execute()
        {
            Buffer.Resize(MeshData.vertexCount, NativeArrayOptions.UninitializedMemory);
            Buffer.Clear();
        }
    }

    [BurstCompile]
    struct InitializeTriangleListJob<T> : IJob
        where T : unmanaged
    {
        [ReadOnly]
        public Mesh.MeshData MeshData;
        public NativeArrayOptions Options;
        public NativeList<T> Buffer;
        public void Execute()
        {
            var triangleCount = MeshData.GetTriangleCount();
            Buffer.Clear();
            Buffer.Resize(triangleCount, Options);
        }
    }
    [BurstCompile]
    struct InitializeUnorderedDirtyVertexMergesJob : IJob
    {

        [ReadOnly]
        public NativeArray<int2> Edges;
        public NativeList<VertexMerge> UnorderedDirtyVertexMerges;

        public void Execute()
        {
            UnorderedDirtyVertexMerges.ResizeUninitialized(Edges.Length);
        }
    }
}

