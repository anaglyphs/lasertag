using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
namespace Meshia.MeshSimplification
{
    [BurstCompile]
    struct MarkBorderEdgeVerticesJob : IJob
    {
        [ReadOnly]
        public Mesh.MeshData Mesh;
        [ReadOnly]
        public NativeHashSet<int2> Edges;
        public NativeBitArray VertexIsBorderEdgeBits;
        public void Execute()
        {
            VertexIsBorderEdgeBits.Resize(Mesh.vertexCount);
            VertexIsBorderEdgeBits.Clear();
            foreach (var pair in Edges)
            {
                var x = pair.x;
                var y = pair.y;
                if (!Edges.Contains(new(y, x)))
                {
                    VertexIsBorderEdgeBits.Set(x, true);
                    VertexIsBorderEdgeBits.Set(y, true);
                }
            }
        }
    }
}


