using System;
using Unity.Mathematics;
namespace Meshia.MeshSimplification
{
    struct VertexMerge : IComparable<VertexMerge>
    {
        public int VertexAIndex, VertexBIndex;
        public int VertexAVersion, VertexBVersion;
        public float3 Position;
        public float Cost;

        public int CompareTo(VertexMerge other)
        {
            return Cost.CompareTo(other.Cost);
        }
    }
}


