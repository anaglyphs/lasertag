using System;
using Unity.Burst;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
namespace Meshia.MeshSimplification
{
    [BurstCompile]
    struct WriteToMeshDataJob : IJob
    {
        [ReadOnly]
        public NativeArray<float3> VertexPositionBuffer;
        [ReadOnly]
        public NativeArray<float4> VertexNormalBuffer;
        [ReadOnly]
        public NativeArray<float4> VertexTangentBuffer;
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

        public NativeArray<float> VertexBlendWeightBuffer;
        [ReadOnly]
        public NativeArray<uint> VertexBlendIndicesBuffer;
        [ReadOnly]

        public NativeArray<uint> VertexContainingSubMeshIndices;
        [ReadOnly]


        public NativeList<BlendShapeData> BlendShapes;
        [ReadOnly]

        public NativeArray<int3> Triangles;
        [ReadOnly]

        public NativeBitArray DiscardedVertex;
        [ReadOnly]

        public NativeBitArray DiscardedTriangle;
        [ReadOnly]

        public Mesh.MeshData SourceMesh;

        public Mesh.MeshData DestinationMesh;
        public NativeList<BlendShapeData> DestinationBlendShapes;
        public AllocatorManager.AllocatorHandle Allocator;
        public void Execute()
        {
            {
                var sourceVertexCount = SourceMesh.vertexCount;
                var destinationVertexCount = sourceVertexCount - DiscardedVertex.CountBits(0, DiscardedVertex.Length);
                int destinationTriangleIndexCount = 0;

                for (int subMeshIndex = 0, triangleIndex = 0; subMeshIndex < SourceMesh.subMeshCount; subMeshIndex++)
                {
                    var sourceSubMeshDescriptor = SourceMesh.GetSubMesh(subMeshIndex);
                    if (sourceSubMeshDescriptor.topology is MeshTopology.Triangles)
                    {
                        int triangleCount = sourceSubMeshDescriptor.indexCount / 3;
                        destinationTriangleIndexCount += triangleCount - DiscardedTriangle.CountBits(triangleIndex, triangleCount);
                        triangleIndex += triangleCount;
                    }
                }

                var destinationToSourceVertexIndex = new NativeArray<int>(destinationVertexCount, Unity.Collections.Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                var sourceToDestinationVertexIndex = new NativeArray<int>(sourceVertexCount, Unity.Collections.Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                var destinationToSourceTriangleIndex = new NativeArray<int>(destinationTriangleIndexCount, Unity.Collections.Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                var destinationIndexFormat = IndexFormat.UInt16;

                var destinationSubMeshes = new NativeArray<SubMeshDescriptor>(SourceMesh.subMeshCount, Unity.Collections.Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                {
                    var destinationIndexBufferIndex = 0;
                    var destinationTriangleIndex = 0;
                    var sourceTriangleIndex = 0;
                    for (int subMeshIndex = 0; subMeshIndex < SourceMesh.subMeshCount; subMeshIndex++)
                    {
                        var sourceSubMeshDescriptor = SourceMesh.GetSubMesh(subMeshIndex);
                        var destinationSubMesh = new SubMeshDescriptor
                        {
                            bounds = sourceSubMeshDescriptor.bounds,
                            topology = sourceSubMeshDescriptor.topology,
                            indexStart = destinationIndexBufferIndex,
                            firstVertex = int.MaxValue,
                            vertexCount = 0,
                        };

                        if (sourceSubMeshDescriptor.topology is MeshTopology.Triangles)
                        {
                            var sourceSubMeshTriangleCount = sourceSubMeshDescriptor.indexCount / 3;

                            var sourceSubMeshTriangleEnd = sourceTriangleIndex + sourceSubMeshTriangleCount;
                            for (; sourceTriangleIndex < sourceSubMeshTriangleEnd; sourceTriangleIndex++)
                            {
                                if (!DiscardedTriangle.IsSet(sourceTriangleIndex))
                                {
                                    destinationToSourceTriangleIndex[destinationTriangleIndex++] = sourceTriangleIndex;

                                    destinationIndexBufferIndex += 3;
                                }
                            }
                        }
                        else
                        {
                            destinationIndexBufferIndex += sourceSubMeshDescriptor.indexCount;
                        }

                        destinationSubMesh.indexCount = destinationIndexBufferIndex - destinationSubMesh.indexStart;

                        // baseVertex is not set yet
                        destinationSubMeshes[subMeshIndex] = destinationSubMesh;
                    }
                }

                for (int sourceVertexIndex = 0, destinationVertexIndex = 0; sourceVertexIndex < sourceVertexCount; sourceVertexIndex++)
                {
                    if (DiscardedVertex.IsSet(sourceVertexIndex))
                    {
                        continue;
                    }
                    destinationToSourceVertexIndex[destinationVertexIndex] = sourceVertexIndex;
                    sourceToDestinationVertexIndex[sourceVertexIndex] = destinationVertexIndex;

                    for (uint indexMask = VertexContainingSubMeshIndices[sourceVertexIndex]; indexMask != 0u; indexMask &= indexMask - 1)
                    {
                        var subMeshIndex = math.tzcnt(indexMask);
                        ref var destinationSubMesh = ref destinationSubMeshes.ElementAt(subMeshIndex);
                        destinationSubMesh.firstVertex = math.min(
                            destinationSubMesh.firstVertex,
                            destinationVertexIndex
                        );
                        destinationSubMesh.vertexCount = math.max(
                            destinationSubMesh.vertexCount,
                            destinationVertexIndex - destinationSubMesh.firstVertex + 1
                        );
                    }
                    destinationVertexIndex++;
                }

                for (int subMeshIndex = 0; subMeshIndex < SourceMesh.subMeshCount; subMeshIndex++)
                {
                    ref var destinationSubMesh = ref destinationSubMeshes.ElementAt(subMeshIndex);
                    if (destinationSubMesh.vertexCount == 0)
                    {
                        destinationSubMesh.firstVertex = 0;
                    }
                    else if (destinationSubMesh.vertexCount >= ushort.MaxValue)
                    {
                        destinationIndexFormat = IndexFormat.UInt32;
                    }
                }

                var maxIndexBufferValue = destinationIndexFormat switch
                {
                    IndexFormat.UInt16 => ushort.MaxValue,
                    IndexFormat.UInt32 => uint.MaxValue,
                    _ => throw new Exception($"Unexpected index format:{destinationIndexFormat}"),
                };
                var currentDestinationBaseVertex = 0;
                for (int subMeshIndex = 0; subMeshIndex < SourceMesh.subMeshCount; subMeshIndex++)
                {
                    ref var destinationSubMesh = ref destinationSubMeshes.ElementAt(subMeshIndex);

                    if (destinationSubMesh.firstVertex + destinationSubMesh.vertexCount - currentDestinationBaseVertex >= maxIndexBufferValue)
                    {
                        currentDestinationBaseVertex = destinationSubMesh.firstVertex;
                    }
                    destinationSubMesh.baseVertex = currentDestinationBaseVertex;
                }

                using var vertexAttributeDescriptors = SourceMesh.GetVertexAttributeDescriptors(Unity.Collections.Allocator.Temp);

                DestinationMesh.SetVertexBufferParams(destinationVertexCount, vertexAttributeDescriptors.AsArray());

                var destinationVertexPositions = DestinationMesh.GetVertexPositions();

                for (int i = 0; i < destinationVertexPositions.Length; i++)
                {
                    destinationVertexPositions[i] = VertexPositionBuffer[destinationToSourceVertexIndex[i]];
                }

                SetVertexAttributeData(DestinationMesh, VertexAttribute.Normal, VertexNormalBuffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.Tangent, VertexTangentBuffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.Color, VertexColorBuffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.TexCoord0, VertexTexCoord0Buffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.TexCoord1, VertexTexCoord1Buffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.TexCoord2, VertexTexCoord2Buffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.TexCoord3, VertexTexCoord3Buffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.TexCoord4, VertexTexCoord4Buffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.TexCoord5, VertexTexCoord5Buffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.TexCoord6, VertexTexCoord6Buffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.TexCoord7, VertexTexCoord7Buffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.BlendWeight, VertexBlendWeightBuffer, destinationToSourceVertexIndex);
                SetVertexAttributeData(DestinationMesh, VertexAttribute.BlendIndices, VertexBlendIndicesBuffer.Reinterpret<int>(), destinationToSourceVertexIndex);

                if (DestinationBlendShapes.Capacity < BlendShapes.Length)
                {
                    DestinationBlendShapes.Capacity = BlendShapes.Length;
                }

                for (int shapeIndex = 0; shapeIndex < BlendShapes.Length; shapeIndex++)
                {
                    var sourceBlendShape = BlendShapes[shapeIndex];
                    var sourceFrames = sourceBlendShape.Frames;

                    UnsafeText destinationBlendShapeName = new(sourceBlendShape.Name.Length, Allocator);
                    destinationBlendShapeName.CopyFrom(sourceBlendShape.Name);
                    UnsafeList<BlendShapeFrameData> destinationFrames = new(sourceFrames.Length, Allocator);

                    for (int frameIndex = 0; frameIndex < sourceFrames.Length; frameIndex++)
                    {
                        var sourceFrame = sourceFrames[frameIndex];
                        var weight = sourceFrame.Weight;
                        var sourceDeltaVertices = sourceFrame.DeltaVertices;
                        var sourceDeltaNormals = sourceFrame.DeltaNormals;
                        var sourceDeltaTangents = sourceFrame.DeltaTangents;

                        UnsafeList<float3> destinationDeltaVertices = new(destinationVertexCount, Allocator);

                        for (int destinationVertexIndex = 0; destinationVertexIndex < destinationVertexCount; destinationVertexIndex++)
                        {
                            var sourceVertexIndex = destinationToSourceVertexIndex[destinationVertexIndex];
                            var deltaVertex = sourceDeltaVertices[sourceVertexIndex];
                            destinationDeltaVertices.Add(deltaVertex);
                        }
                        UnsafeList<float3> destinationDeltaNormals = new(destinationVertexCount, Allocator);
                        for (int destinationVertexIndex = 0; destinationVertexIndex < destinationVertexCount; destinationVertexIndex++)
                        {
                            var sourceVertexIndex = destinationToSourceVertexIndex[destinationVertexIndex];
                            var deltaNormal = sourceDeltaNormals[sourceVertexIndex];
                            destinationDeltaNormals.Add(deltaNormal);
                        }
                        UnsafeList<float3> destinationDeltaTangents = new(destinationVertexCount, Allocator);
                        for (int destinationVertexIndex = 0; destinationVertexIndex < destinationVertexCount; destinationVertexIndex++)
                        {
                            var sourceVertexIndex = destinationToSourceVertexIndex[destinationVertexIndex];
                            var deltaTangent = sourceDeltaTangents[sourceVertexIndex];
                            destinationDeltaTangents.Add(deltaTangent);
                        }
                        destinationFrames.Add(new()
                        {
                            Weight = weight,
                            DeltaVertices = destinationDeltaVertices,
                            DeltaNormals = destinationDeltaNormals,
                            DeltaTangents = destinationDeltaTangents,
                        });
                    }
                    DestinationBlendShapes.Add(new()
                    {
                        Name = destinationBlendShapeName,
                        Frames = destinationFrames,
                    });
                }

                DestinationMesh.SetIndexBufferParams(SourceMesh.GetIndexCount() - DiscardedTriangle.CountBits(0, DiscardedTriangle.Length) * 3, destinationIndexFormat);

                switch (destinationIndexFormat)
                {
                    case IndexFormat.UInt16:
                        {
                            var destinationSubMeshTriangleStart = 0;
                            var destinationIndices = DestinationMesh.GetIndexData<ushort>();
                            for (int subMeshIndex = 0; subMeshIndex < SourceMesh.subMeshCount; subMeshIndex++)
                            {
                                var destinationSubMesh = destinationSubMeshes[subMeshIndex];
                                var destinationSubMeshIndexEnd = destinationSubMesh.indexStart + destinationSubMesh.indexCount;

                                if (destinationSubMesh.topology is MeshTopology.Triangles)
                                {
                                    var destinationSubMeshTriangleCount = destinationSubMesh.indexCount / 3;

                                    var destinationSubMeshTriangleEnd = destinationSubMeshTriangleStart + destinationSubMeshTriangleCount;

                                    for (int destinationSubMeshTriangleIndex = 0; destinationSubMeshTriangleIndex < destinationSubMeshTriangleCount; destinationSubMeshTriangleIndex++)
                                    {
                                        var destinationTriangleIndex = destinationSubMeshTriangleStart + destinationSubMeshTriangleIndex;


                                        var sourceTriangleIndex = destinationToSourceTriangleIndex[destinationTriangleIndex];

                                        var sourceTriangle = Triangles[sourceTriangleIndex];
                                        int3 destinationTriangle = new()
                                        {
                                            x = sourceToDestinationVertexIndex[sourceTriangle.x],
                                            y = sourceToDestinationVertexIndex[sourceTriangle.y],
                                            z = sourceToDestinationVertexIndex[sourceTriangle.z],
                                        };

                                        destinationTriangle -= destinationSubMesh.baseVertex;

                                        var destinationTriangleStart = destinationSubMeshTriangleIndex * 3 + destinationSubMesh.indexStart;

                                        destinationIndices[destinationTriangleStart + 0] = (ushort)destinationTriangle.x;
                                        destinationIndices[destinationTriangleStart + 1] = (ushort)destinationTriangle.y;
                                        destinationIndices[destinationTriangleStart + 2] = (ushort)destinationTriangle.z;
                                    }
                                    destinationSubMeshTriangleStart += destinationSubMeshTriangleCount;
                                }
                                else
                                {
                                    var sourceSubMesh = SourceMesh.GetSubMesh(subMeshIndex);

                                    var subMeshVertexIndexOffset = destinationSubMesh.firstVertex - sourceSubMesh.firstVertex;

                                    var destinationSubMeshIndexBuffer = destinationIndices.GetSubArray(destinationSubMesh.indexStart, destinationSubMesh.indexCount);
                                    switch (SourceMesh.indexFormat)
                                    {
                                        case IndexFormat.UInt16:
                                            {
                                                var sourceIndexBuffer = SourceMesh.GetIndexData<ushort>();

                                                var sourceSubMeshIndexBuffer = sourceIndexBuffer.GetSubArray(sourceSubMesh.indexStart, sourceSubMesh.indexCount);
                                                for (int subMeshIndexBufferIndex = 0; subMeshIndexBufferIndex < destinationSubMesh.indexCount; subMeshIndexBufferIndex++)
                                                {
                                                    var sourceVertexIndex = sourceSubMeshIndexBuffer[subMeshIndexBufferIndex] + sourceSubMesh.baseVertex;
                                                    var destinationVertexIndex = sourceVertexIndex + subMeshVertexIndexOffset;
                                                    var destinationIndexBufferValue = destinationVertexIndex - destinationSubMesh.baseVertex;
                                                    destinationSubMeshIndexBuffer[subMeshIndexBufferIndex] = (ushort)destinationIndexBufferValue;
                                                }
                                            }
                                            break;
                                        case IndexFormat.UInt32:
                                            {
                                                var sourceIndexBuffer = SourceMesh.GetIndexData<uint>();

                                                var sourceSubMeshIndexBuffer = sourceIndexBuffer.GetSubArray(sourceSubMesh.indexStart, sourceSubMesh.indexCount);
                                                for (int subMeshIndexBufferIndex = 0; subMeshIndexBufferIndex < destinationSubMesh.indexCount; subMeshIndexBufferIndex++)
                                                {
                                                    var sourceVertexIndex = sourceSubMeshIndexBuffer[subMeshIndexBufferIndex] + sourceSubMesh.baseVertex;
                                                    var destinationVertexIndex = sourceVertexIndex + subMeshVertexIndexOffset;
                                                    var destinationIndexBufferValue = destinationVertexIndex - destinationSubMesh.baseVertex;
                                                    destinationSubMeshIndexBuffer[subMeshIndexBufferIndex] = (ushort)destinationIndexBufferValue;
                                                }
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                        break;
                    case IndexFormat.UInt32:
                        {
                            var destinationSubMeshTriangleStart = 0;
                            var destinationIndices = DestinationMesh.GetIndexData<int>();
                            for (int subMeshIndex = 0; subMeshIndex < SourceMesh.subMeshCount; subMeshIndex++)
                            {
                                var destinationSubMesh = destinationSubMeshes[subMeshIndex];
                                var destinationSubMeshIndexEnd = destinationSubMesh.indexStart + destinationSubMesh.indexCount;

                                if (destinationSubMesh.topology is MeshTopology.Triangles)
                                {
                                    var destinationSubMeshTriangleCount = destinationSubMesh.indexCount / 3;

                                    var destinationSubMeshTriangleEnd = destinationSubMeshTriangleStart + destinationSubMeshTriangleCount;

                                    for (int destinationSubMeshTriangleIndex = 0; destinationSubMeshTriangleIndex < destinationSubMeshTriangleCount; destinationSubMeshTriangleIndex++)
                                    {
                                        var destinationTriangleIndex = destinationSubMeshTriangleStart + destinationSubMeshTriangleIndex;


                                        var sourceTriangleIndex = destinationToSourceTriangleIndex[destinationTriangleIndex];

                                        var sourceTriangle = Triangles[sourceTriangleIndex];
                                        int3 destinationTriangle = new()
                                        {
                                            x = sourceToDestinationVertexIndex[sourceTriangle.x],
                                            y = sourceToDestinationVertexIndex[sourceTriangle.y],
                                            z = sourceToDestinationVertexIndex[sourceTriangle.z],
                                        };

                                        destinationTriangle -= destinationSubMesh.baseVertex;

                                        var destinationTriangleStart = destinationSubMeshTriangleIndex * 3 + destinationSubMesh.indexStart;
                                        destinationIndices[destinationTriangleStart + 0] = destinationTriangle.x;
                                        destinationIndices[destinationTriangleStart + 1] = destinationTriangle.y;
                                        destinationIndices[destinationTriangleStart + 2] = destinationTriangle.z;
                                    }
                                    destinationSubMeshTriangleStart += destinationSubMeshTriangleCount;
                                }
                                else
                                {
                                    var sourceSubMesh = SourceMesh.GetSubMesh(subMeshIndex);

                                    var subMeshVertexIndexOffset = destinationSubMesh.firstVertex - sourceSubMesh.firstVertex;


                                    var destinationSubMeshIndexBuffer = destinationIndices.GetSubArray(destinationSubMesh.indexStart, destinationSubMesh.indexCount);
                                    switch (SourceMesh.indexFormat)
                                    {
                                        case IndexFormat.UInt16:
                                            {
                                                var sourceIndexBuffer = SourceMesh.GetIndexData<ushort>();
                                                var sourceSubMeshIndexBuffer = sourceIndexBuffer.GetSubArray(sourceSubMesh.indexStart, sourceSubMesh.indexCount);
                                                for (int subMeshIndexBufferIndex = 0; subMeshIndexBufferIndex < destinationSubMesh.indexCount; subMeshIndexBufferIndex++)
                                                {
                                                    var sourceVertexIndex = sourceSubMeshIndexBuffer[subMeshIndexBufferIndex] + sourceSubMesh.baseVertex;
                                                    var destinationVertexIndex = sourceVertexIndex + subMeshVertexIndexOffset;
                                                    var destinationIndexBufferValue = destinationVertexIndex - destinationSubMesh.baseVertex;
                                                    destinationSubMeshIndexBuffer[subMeshIndexBufferIndex] = destinationIndexBufferValue;
                                                }
                                            }
                                            break;
                                        case IndexFormat.UInt32:
                                            {
                                                var sourceIndexBuffer = SourceMesh.GetIndexData<int>();
                                                var sourceSubMeshIndexBuffer = sourceIndexBuffer.GetSubArray(sourceSubMesh.indexStart, sourceSubMesh.indexCount);
                                                for (int subMeshIndexBufferIndex = 0; subMeshIndexBufferIndex < destinationSubMesh.indexCount; subMeshIndexBufferIndex++)
                                                {
                                                    var sourceVertexIndex = sourceSubMeshIndexBuffer[subMeshIndexBufferIndex] + sourceSubMesh.baseVertex;
                                                    var destinationVertexIndex = sourceVertexIndex + subMeshVertexIndexOffset;
                                                    var destinationIndexBufferValue = destinationVertexIndex - destinationSubMesh.baseVertex;
                                                    destinationSubMeshIndexBuffer[subMeshIndexBufferIndex] = destinationIndexBufferValue;
                                                }
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                        break;
                }

                DestinationMesh.subMeshCount = destinationSubMeshes.Length;
                for (int subMeshIndex = 0; subMeshIndex < destinationSubMeshes.Length; subMeshIndex++)
                {
                    DestinationMesh.SetSubMesh(subMeshIndex, destinationSubMeshes[subMeshIndex]);
                }
            }

        }
        static void SetVertexAttributeData(Mesh.MeshData mesh, VertexAttribute vertexAttribute, NativeArray<float4> vertexAttributeData, NativeArray<int> destinationToSourceVertexIndex)
        {
            if (!mesh.HasVertexAttribute(vertexAttribute))
            {
                return;
            }
            var format = mesh.GetVertexAttributeFormat(vertexAttribute);
            var dimension = mesh.GetVertexAttributeDimension(vertexAttribute);

            var streamIndex = mesh.GetVertexAttributeStream(vertexAttribute);
            var stream = mesh.GetVertexData<byte>(streamIndex);
            var offset = mesh.GetVertexAttributeOffset(vertexAttribute);
            var stride = mesh.GetVertexBufferStride(streamIndex);

            for (int i = 0; i < mesh.vertexCount; i++)
            {
                SetVertexAttributeDataElement(stream, stride, offset, format, dimension, i, vertexAttributeData[destinationToSourceVertexIndex[i]]);
            }
        }
        static void SetVertexAttributeData(Mesh.MeshData mesh, VertexAttribute vertexAttribute, ReadOnlySpan<float> vertexAttributeData, NativeArray<int> destinationToSourceVertexIndex)
        {
            if (!mesh.HasVertexAttribute(vertexAttribute))
            {
                return;
            }
            var format = mesh.GetVertexAttributeFormat(vertexAttribute);
            var dimension = mesh.GetVertexAttributeDimension(vertexAttribute);

            var streamIndex = mesh.GetVertexAttributeStream(vertexAttribute);
            var stream = mesh.GetVertexData<byte>(streamIndex);
            var offset = mesh.GetVertexAttributeOffset(vertexAttribute);
            var stride = mesh.GetVertexBufferStride(streamIndex);
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                var sourceIndex = destinationToSourceVertexIndex[i];
                SetVertexAttributeDataElement(stream, stride, offset, format, i, vertexAttributeData.Slice(dimension * sourceIndex, dimension));
            }
        }
        static void SetVertexAttributeData(Mesh.MeshData mesh, VertexAttribute vertexAttribute, ReadOnlySpan<int> vertexAttributeData, NativeArray<int> destinationToSourceVertexIndex)
        {
            if (!mesh.HasVertexAttribute(vertexAttribute))
            {
                return;
            }
            var format = mesh.GetVertexAttributeFormat(vertexAttribute);
            var dimension = mesh.GetVertexAttributeDimension(vertexAttribute);

            var streamIndex = mesh.GetVertexAttributeStream(vertexAttribute);
            var stream = mesh.GetVertexData<byte>(streamIndex);
            var offset = mesh.GetVertexAttributeOffset(vertexAttribute);
            var stride = mesh.GetVertexBufferStride(streamIndex);
            for (int i = 0; i < mesh.vertexCount; i++)
            {
                var sourceIndex = destinationToSourceVertexIndex[i];
                SetVertexAttributeDataElement(stream, stride, offset, format, i, vertexAttributeData.Slice(dimension * sourceIndex, dimension));
            }
        }
        public static unsafe void SetVertexAttributeDataElement(NativeArray<byte> stream, int stride, int offset, VertexAttributeFormat format, int dimension, int vertexIndex, float4 value)
        {
            var ptr = (byte*)stream.GetUnsafePtr() + (stride * vertexIndex + offset);
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                    {
                        var vertexComponents = (float*)ptr;

                        for (int i = 0; i < dimension; i++)
                        {
                            // This loop is optimized away instead of vectorized
                            vertexComponents[i] = value[i];
                        }

                    }
                    break;
                case VertexAttributeFormat.Float16:
                    {
                        var vertexComponents = (half*)ptr;

                        for (int i = 0; i < dimension; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (half)value[i];
                        }

                    }
                    break;
                case VertexAttributeFormat.UNorm8:
                    {
                        var vertexComponents = ptr;

                        for (int i = 0; i < dimension; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (byte)(value[i] * byte.MaxValue);
                        }

                    }
                    break;
                case VertexAttributeFormat.SNorm8:
                    {
                        var vertexComponents = (sbyte*)ptr;

                        for (int i = 0; i < dimension; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (sbyte)(value[i] * sbyte.MaxValue);
                        }

                    }
                    break;
                case VertexAttributeFormat.UNorm16:
                    {
                        var vertexComponents = (ushort*)ptr;
                        for (int i = 0; i < dimension; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (ushort)(value[i] * ushort.MaxValue);
                        }
                    }
                    break;
                case VertexAttributeFormat.SNorm16:
                    {
                        var vertexComponents = (short*)ptr;
                        for (int i = 0; i < dimension; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (short)(value[i] * short.MaxValue);
                        }

                    }
                    break;
                default:
                    throw new NotSupportedException($"The assigned {nameof(VertexAttributeFormat)} is not supported yet.");
            }
        }
        public static unsafe void SetVertexAttributeDataElement(NativeArray<byte> stream, int stride, int offset, VertexAttributeFormat format, int vertexIndex, ReadOnlySpan<float> value)
        {
            var ptr = (byte*)stream.GetUnsafePtr() + (stride * vertexIndex + offset);
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                    {
                        var vertexComponents = (float*)ptr;

                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = value[i];
                        }

                    }
                    break;
                case VertexAttributeFormat.Float16:
                    {
                        var vertexComponents = (half*)ptr;

                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (half)value[i];
                        }

                    }
                    break;
                case VertexAttributeFormat.UNorm8:
                    {
                        var vertexComponents = ptr;

                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (byte)(value[i] * byte.MaxValue);
                        }

                    }
                    break;
                case VertexAttributeFormat.SNorm8:
                    {
                        var vertexComponents = (sbyte*)ptr;

                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (sbyte)(value[i] * sbyte.MaxValue);
                        }

                    }
                    break;
                case VertexAttributeFormat.UNorm16:
                    {
                        var vertexComponents = (ushort*)ptr;
                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (ushort)(value[i] * ushort.MaxValue);
                        }
                    }
                    break;
                case VertexAttributeFormat.SNorm16:
                    {
                        var vertexComponents = (short*)ptr;
                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (short)(value[i] * short.MaxValue);
                        }

                    }
                    break;
                default:
                    throw new NotSupportedException($"The assigned {nameof(VertexAttributeFormat)} is not supported yet.");
            }
        }
        public static unsafe void SetVertexAttributeDataElement(NativeArray<byte> stream, int stride, int offset, VertexAttributeFormat format, int vertexIndex, ReadOnlySpan<int> value)
        {
            var ptr = (byte*)stream.GetUnsafePtr() + (stride * vertexIndex + offset);

            switch (format)
            {
                case VertexAttributeFormat.UInt8:
                    {
                        var vertexComponents = (byte*)ptr;

                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (byte)value[i];
                        }
                    }
                    break;
                case VertexAttributeFormat.SInt8:
                    {
                        var vertexComponents = (sbyte*)ptr;

                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (sbyte)value[i];
                        }
                    }
                    break;
                case VertexAttributeFormat.UInt16:
                    {
                        var vertexComponents = (ushort*)ptr;

                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (ushort)value[i];
                        }
                    }
                    break;
                case VertexAttributeFormat.SInt16:
                    {
                        var vertexComponents = (short*)ptr;

                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (short)value[i];
                        }
                    }
                    break;
                case VertexAttributeFormat.UInt32:
                    {
                        var vertexComponents = (uint*)ptr;

                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (uint)value[i];
                        }
                    }
                    break;
                case VertexAttributeFormat.SInt32:
                    {
                        var vertexComponents = (int*)ptr;

                        for (int i = 0; i < value.Length; i++)
                        {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                            Loop.ExpectVectorized();
#endif

                            vertexComponents[i] = (int)value[i];
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"The assigned {nameof(VertexAttributeFormat)} is not supported yet.");
            }
        }

    }
}


