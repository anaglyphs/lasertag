#nullable enable
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
namespace Meshia.MeshSimplification
{
    public struct BlendShapeFrameData : IDisposable
    {
        public float Weight;
        public UnsafeList<float3> DeltaVertices;
        public UnsafeList<float3> DeltaNormals;
        public UnsafeList<float3> DeltaTangents;

        internal unsafe static BlendShapeFrameData Create(Mesh mesh, int shapeIndex, int frameIndex, Vector3[] deltaVerticesBuffer, Vector3[] deltaNormalsBuffer, Vector3[] deltaTangentsBuffer, AllocatorManager.AllocatorHandle allocator)
        {
            var weight = mesh.GetBlendShapeFrameWeight(shapeIndex, frameIndex);
            mesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVerticesBuffer, deltaNormalsBuffer, deltaTangentsBuffer);

            UnsafeList<float3> deltaVertices = new(deltaVerticesBuffer.Length, allocator);
            deltaVertices.Resize(deltaVerticesBuffer.Length);
            deltaVerticesBuffer.AsSpan().CopyTo(new(deltaVertices.Ptr, deltaVertices.Length));

            UnsafeList<float3> deltaNormals = new(deltaNormalsBuffer.Length, allocator);
            deltaNormals.Resize(deltaNormalsBuffer.Length);
            deltaNormalsBuffer.AsSpan().CopyTo(new(deltaNormals.Ptr, deltaNormals.Length));

            UnsafeList<float3> deltaTangents = new(deltaTangentsBuffer.Length, allocator);
            deltaTangents.Resize(deltaTangentsBuffer.Length);
            deltaTangentsBuffer.AsSpan().CopyTo(new(deltaTangents.Ptr, deltaTangents.Length));
            return new()
            {
                Weight = weight,
                DeltaVertices = deltaVertices,
                DeltaNormals = deltaNormals,
                DeltaTangents = deltaTangents,
            };
        }
        /// <summary>
        /// Disposes the <see cref="BlendShapeFrameData"/> and its internal buffers.
        /// </summary>
        public void Dispose()
        {
            DeltaVertices.Dispose();
            DeltaNormals.Dispose();
            DeltaTangents.Dispose();
        }
    }
}


