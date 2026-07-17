#nullable enable
using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
namespace Meshia.MeshSimplification
{
    public struct BlendShapeData : IDisposable
    {
        public UnsafeText Name;
        public UnsafeList<BlendShapeFrameData> Frames;

        /// <summary>
        /// Initializes the new list of <see cref="BlendShapeData"/> from the given <paramref name="mesh"/>.
        /// </summary>
        /// <param name="mesh">The mesh from which blend shapes are loaded.</param>
        /// <param name="allocator">The allocator for returned list of <see cref="BlendShapeData"/>.</param>
        /// <returns></returns>
        public static NativeList<BlendShapeData> GetMeshBlendShapes(Mesh mesh, AllocatorManager.AllocatorHandle allocator)
        {
            NativeList<BlendShapeData> blendShapes = new(mesh.blendShapeCount, allocator);
            if (mesh.blendShapeCount > 0)
            {
                var deltaVerticesBuffer = new Vector3[mesh.vertexCount];
                var deltaNormalsBuffer = new Vector3[mesh.vertexCount];
                var deltaTangentsBuffer = new Vector3[mesh.vertexCount];
                for (int shapeIndex = 0; shapeIndex < mesh.blendShapeCount; shapeIndex++)
                {
                    var blendShape = Create(mesh, shapeIndex, deltaVerticesBuffer, deltaNormalsBuffer, deltaTangentsBuffer, allocator);
                    blendShapes.Add(blendShape);
                }
            }

            return blendShapes;
        }
        /// <summary>
        /// Sets the blend shapes of the given <paramref name="mesh"/> from the given <paramref^@ name="blendShapes"/>.
        /// </summary>
        /// <param name="mesh">The mesh to set its blend shapes.</param>
        /// <param name="blendShapes">The blend shapes to set.</param>
        public static void SetBlendShapes(Mesh mesh, ReadOnlySpan<BlendShapeData> blendShapes)
        {
            var vertexCount = mesh.vertexCount;
            var deltaVerticesBuffer = new Vector3[vertexCount];
            var deltaNormalsBuffer = new Vector3[vertexCount];
            var deltaTangentsBuffer = new Vector3[vertexCount];

            mesh.ClearBlendShapes();
            for (int shapeIndex = 0; shapeIndex < blendShapes.Length; shapeIndex++)
            {
                var blendShape = blendShapes[shapeIndex];
                var name = blendShape.Name.ConvertToString();
                var frames = blendShape.Frames;
                for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
                {
                    var frame = frames[frameIndex];
                    MemoryMarshal.Cast<float3, Vector3>(frame.DeltaVertices.AsSpan()).CopyTo(deltaVerticesBuffer);
                    MemoryMarshal.Cast<float3, Vector3>(frame.DeltaNormals.AsSpan()).CopyTo(deltaNormalsBuffer);
                    MemoryMarshal.Cast<float3, Vector3>(frame.DeltaTangents.AsSpan()).CopyTo(deltaTangentsBuffer);

                    mesh.AddBlendShapeFrame(name, frame.Weight, deltaVerticesBuffer, deltaNormalsBuffer, deltaTangentsBuffer);
                }
            }
        }

        internal static BlendShapeData Create(Mesh mesh, int shapeIndex, Vector3[] deltaVerticesBuffer, Vector3[] deltaNormalsBuffer, Vector3[] deltaTangentsBuffer, AllocatorManager.AllocatorHandle allocator)
        {
            var nameString = mesh.GetBlendShapeName(shapeIndex);
            UnsafeText name = new(nameString.Length, allocator);

            name.CopyFrom(nameString);
            var frameCount = mesh.GetBlendShapeFrameCount(shapeIndex);
            UnsafeList<BlendShapeFrameData> frames = new(frameCount, allocator);
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var frame = BlendShapeFrameData.Create(mesh, shapeIndex, frameIndex, deltaVerticesBuffer, deltaNormalsBuffer, deltaTangentsBuffer, allocator);
                frames.Add(frame);
            }

            return new()
            {
                Name = name,
                Frames = frames,
            };
        }
        /// <summary>
        /// Disposes the <see cref="BlendShapeData"/> and its frames.
        /// </summary>
        public void Dispose()
        {
            Name.Dispose();
            for (int i = 0; i < Frames.Length; i++)
            {
                Frames[i].Dispose();
            }
            Frames.Dispose();

        }
    }
}


