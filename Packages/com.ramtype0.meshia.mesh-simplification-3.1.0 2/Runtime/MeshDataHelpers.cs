#nullable enable
using System;
using Unity.Burst.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Meshia.MeshSimplification
{
    internal static class MeshDataHelpers
    {

        [return: AssumeRange(0, int.MaxValue)]
        public static int GetIndexCount(this Mesh.MeshData mesh)
        {
            return mesh.indexFormat == IndexFormat.UInt16 ? mesh.GetIndexData<ushort>().Length : mesh.GetIndexData<uint>().Length;
        }
        [return: AssumeRange(0, int.MaxValue / 3)]
        public static int GetTriangleCount(this Mesh.MeshData mesh)
        {
            var indexCount = 0;
            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                var subMesh = mesh.GetSubMesh(subMeshIndex);
                if (subMesh.topology == MeshTopology.Triangles)
                {
                    indexCount += subMesh.indexCount;
                }
            }
            return indexCount / 3;
        }
        public static int GetTriangleCount(this Mesh mesh)
        {
            var indexCount = 0;
            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                var subMesh = mesh.GetSubMesh(subMeshIndex);
                if (subMesh.topology == MeshTopology.Triangles)
                {
                    indexCount += subMesh.indexCount;
                }
            }
            return indexCount / 3;
        }
        [return: AssumeRange(0, 4 * 4)]
        public static int GetVertexAttributeSize(this Mesh.MeshData mesh, VertexAttribute vertexAttribute) => GetVertexAttributeSize(mesh.GetVertexAttributeFormat(vertexAttribute), mesh.GetVertexAttributeDimension(vertexAttribute));


        [return: AssumeRange(1, 4)]
        unsafe static int GetVertexAttributeSize(VertexAttributeFormat format, int dimension)
        {
            var componentSize = format switch
            {
                VertexAttributeFormat.Float32 => sizeof(float),
                VertexAttributeFormat.Float16 => sizeof(half),
                VertexAttributeFormat.UNorm8 => sizeof(byte),
                VertexAttributeFormat.SNorm8 => sizeof(sbyte),
                VertexAttributeFormat.UNorm16 => sizeof(ushort),
                VertexAttributeFormat.SNorm16 => sizeof(short),
                VertexAttributeFormat.UInt8 => sizeof(byte),
                VertexAttributeFormat.SInt8 => sizeof(sbyte),
                VertexAttributeFormat.UInt16 => sizeof(ushort),
                VertexAttributeFormat.SInt16 => sizeof(short),
                VertexAttributeFormat.UInt32 => sizeof(uint),
                VertexAttributeFormat.SInt32 => sizeof(int),
                _ => throw new ArgumentException("Unexpected format!"),
            };
            return componentSize * dimension;
        }

        public static unsafe NativeSlice<T> GetVertexAttributeData<T>(this Mesh.MeshData mesh, VertexAttribute vertexAttribute)
            where T : unmanaged
        {
            var streamIndex = mesh.GetVertexAttributeStream(vertexAttribute);
            var stride = mesh.GetVertexBufferStride(streamIndex);
            var offset = mesh.GetVertexAttributeOffset(vertexAttribute);
            var size = mesh.GetVertexAttributeSize(vertexAttribute);
            if (size != sizeof(T))
            {
                throw new ArgumentException("Element size mismatch!");
            }
            var vertexStream = mesh.GetVertexData<byte>(streamIndex);

            var slice = NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<T>((byte*)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(vertexStream) + offset, stride, mesh.vertexCount);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref slice, NativeArrayUnsafeUtility.GetAtomicSafetyHandle(vertexStream));
#endif
            return slice;
        }

        public static void AssertVertexPositionIsFloat3(Mesh.MeshData mesh)
        {
            var format = mesh.GetVertexAttributeFormat(VertexAttribute.Position);
            var dimension = mesh.GetVertexAttributeDimension(VertexAttribute.Position);
            if (format != VertexAttributeFormat.Float32 || dimension != 3)
            {
                throw new ArgumentException($"Vertex position type must be float3. but format was: {format}, dimension was {dimension}");
            }
        }
        public static NativeSlice<float3> GetVertexPositions(this Mesh.MeshData mesh)
        {
            AssertVertexPositionIsFloat3(mesh);
            return GetVertexAttributeData<float3>(mesh, VertexAttribute.Position);
        }
        public static NativeList<VertexAttributeDescriptor> GetVertexAttributeDescriptors(this Mesh.MeshData mesh, Allocator allocator)
        {
            var vertexAttributes = new NativeList<VertexAttributeDescriptor>(14, allocator);
            for (var vertexAttribute = VertexAttribute.Position; vertexAttribute <= VertexAttribute.BlendIndices; vertexAttribute++)
            {
                if (mesh.HasVertexAttribute(vertexAttribute))
                {
                    var vertexAttributeDescriptor = new VertexAttributeDescriptor
                    {
                        attribute = vertexAttribute,
                        format = mesh.GetVertexAttributeFormat(vertexAttribute),
                        dimension = mesh.GetVertexAttributeDimension(vertexAttribute),
                        stream = mesh.GetVertexAttributeStream(vertexAttribute),
                    };
                    vertexAttributes.Add(vertexAttributeDescriptor);
                }
            }
            return vertexAttributes;
        }
        public static unsafe void GetVertexAttributeDataAsFloat4(this Mesh.MeshData mesh, VertexAttribute vertexAttribute, Span<float4> vertexAttributeData, int firstVertex = 0)
        {
            if (firstVertex + vertexAttributeData.Length > mesh.vertexCount)
            {
                throw new ArgumentOutOfRangeException("firstVertex + vertexAttributeData.Length > mesh.vertexCount");
            }

            var format = mesh.GetVertexAttributeFormat(vertexAttribute);
            var dimension = mesh.GetVertexAttributeDimension(vertexAttribute);
            var value = float4.zero;

            var streamIndex = mesh.GetVertexAttributeStream(vertexAttribute);
            var stream = mesh.GetVertexData<byte>(streamIndex);
            var offset = mesh.GetVertexAttributeOffset(vertexAttribute);
            var stride = mesh.GetVertexBufferStride(streamIndex);

            var sourcePtr = (byte*)stream.GetUnsafeReadOnlyPtr() + offset + stride * firstVertex;
            switch (format)
            {
                case VertexAttributeFormat.Float32:
                    {
                        fixed (float4* destinationPtr = vertexAttributeData)
                        {
                            if (dimension != 4)
                            {
                                UnsafeUtility.MemClear(destinationPtr, sizeof(float4) * vertexAttributeData.Length);
                            }
                            UnsafeUtility.MemCpyStride(destinationPtr, sizeof(float4), sourcePtr, stride, sizeof(float) * dimension, vertexAttributeData.Length);
                        }
                    }
                    break;
                case VertexAttributeFormat.Float16:
                    {
                        for (int vertexIndex = 0; vertexIndex < vertexAttributeData.Length; vertexIndex++)
                        {
                            var sourceElement = (half*)(sourcePtr + stride * vertexIndex);
                            for (int i = 0; i < dimension; i++)
                            {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                Loop.ExpectVectorized();
#endif

                                value[i] = sourceElement[i];
                            }
                            vertexAttributeData[vertexIndex] = value;
                        }
                    }
                    break;
                case VertexAttributeFormat.UNorm8:
                    {
                        for (int vertexIndex = 0; vertexIndex < vertexAttributeData.Length; vertexIndex++)
                        {
                            var sourceElement = (byte*)(sourcePtr + stride * vertexIndex);
                            for (int i = 0; i < dimension; i++)
                            {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                Loop.ExpectVectorized();
#endif

                                value[i] = sourceElement[i] / (float)byte.MaxValue;
                            }
                            vertexAttributeData[vertexIndex] = value;
                        }
                    }
                    break;
                case VertexAttributeFormat.SNorm8:
                    {
                        for (int vertexIndex = 0; vertexIndex < vertexAttributeData.Length; vertexIndex++)
                        {
                            var sourceElement = (sbyte*)(sourcePtr + stride * vertexIndex);
                            for (int i = 0; i < dimension; i++)
                            {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                Loop.ExpectVectorized();
#endif

                                value[i] = math.saturate(sourceElement[i] / (float)sbyte.MaxValue);
                            }
                            vertexAttributeData[vertexIndex] = value;
                        }
                    }
                    break;
                case VertexAttributeFormat.UNorm16:
                    {
                        for (int vertexIndex = 0; vertexIndex < vertexAttributeData.Length; vertexIndex++)
                        {
                            var sourceElement = (ushort*)(sourcePtr + stride * vertexIndex);
                            for (int i = 0; i < dimension; i++)
                            {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                Loop.ExpectVectorized();
#endif

                                value[i] = sourceElement[i] / (float)ushort.MaxValue;
                            }
                            vertexAttributeData[vertexIndex] = value;
                        }
                    }
                    break;
                case VertexAttributeFormat.SNorm16:
                    {
                        for (int vertexIndex = 0; vertexIndex < vertexAttributeData.Length; vertexIndex++)
                        {
                            var sourceElement = (short*)(sourcePtr + stride * vertexIndex);
                            for (int i = 0; i < dimension; i++)
                            {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                Loop.ExpectVectorized();
#endif

                                value[i] = math.saturate(sourceElement[i] / (float)short.MaxValue);
                            }
                            vertexAttributeData[vertexIndex] = value;
                        }
                    }
                    break;
                default:
                    throw new NotSupportedException($"The assigned {nameof(VertexAttributeFormat)} is not supported yet.");
            }




        }
        public static unsafe void GetVertexAttributeDataAsFloats(this Mesh.MeshData mesh, VertexAttribute vertexAttribute, Span<float> vertexAttributeData, int firstVertex = 0)
        {
            var format = mesh.GetVertexAttributeFormat(vertexAttribute);
            var dimension = mesh.GetVertexAttributeDimension(vertexAttribute);

            var elementCount = vertexAttributeData.Length / dimension;

            if (firstVertex + elementCount > mesh.vertexCount)
            {
                throw new ArgumentOutOfRangeException("firstVertex + vertexAttributeData.Length > mesh.vertexCount");
            }

            var streamIndex = mesh.GetVertexAttributeStream(vertexAttribute);
            var stream = mesh.GetVertexData<byte>(streamIndex);
            var offset = mesh.GetVertexAttributeOffset(vertexAttribute);
            var stride = mesh.GetVertexBufferStride(streamIndex);

            var sourcePtr = (byte*)stream.GetUnsafeReadOnlyPtr() + offset + stride * firstVertex;
            fixed (float* destinationPtr = vertexAttributeData)
            {
                switch (format)
                {
                    case VertexAttributeFormat.Float32:
                        {
                            UnsafeUtility.MemCpyStride(destinationPtr, sizeof(float) * dimension, sourcePtr, stride, sizeof(float) * dimension, elementCount);
                        }
                        break;
                    case VertexAttributeFormat.Float16:
                        {
                            for (int vertexIndex = 0; vertexIndex < vertexAttributeData.Length; vertexIndex++)
                            {
                                var sourceElement = (half*)(sourcePtr + stride * vertexIndex);
                                var destinationElement = destinationPtr + dimension * vertexIndex;
                                for (int i = 0; i < dimension; i++)
                                {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                    Loop.ExpectVectorized();
#endif

                                    destinationElement[i] = sourceElement[i];
                                }
                            }
                        }
                        break;
                    case VertexAttributeFormat.UNorm8:
                        {
                            for (int vertexIndex = 0; vertexIndex < vertexAttributeData.Length; vertexIndex++)
                            {
                                var sourceElement = (byte*)(sourcePtr + stride * vertexIndex);
                                var destinationElement = destinationPtr + dimension * vertexIndex;
                                for (int i = 0; i < dimension; i++)
                                {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                    Loop.ExpectVectorized();
#endif

                                    destinationElement[i] = sourceElement[i] / (float)byte.MaxValue;
                                }
                            }
                        }
                        break;
                    case VertexAttributeFormat.SNorm8:
                        {
                            for (int vertexIndex = 0; vertexIndex < vertexAttributeData.Length; vertexIndex++)
                            {
                                var sourceElement = (sbyte*)(sourcePtr + stride * vertexIndex);
                                var destinationElement = destinationPtr + dimension * vertexIndex;
                                for (int i = 0; i < dimension; i++)
                                {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                    Loop.ExpectVectorized();
#endif

                                    destinationElement[i] = math.saturate(sourceElement[i] / (float)sbyte.MaxValue);
                                }
                            }
                        }
                        break;
                    case VertexAttributeFormat.UNorm16:
                        {
                            for (int vertexIndex = 0; vertexIndex < vertexAttributeData.Length; vertexIndex++)
                            {
                                var sourceElement = (ushort*)(sourcePtr + stride * vertexIndex);
                                var destinationElement = destinationPtr + dimension * vertexIndex;
                                for (int i = 0; i < dimension; i++)
                                {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                    Loop.ExpectVectorized();
#endif

                                    destinationElement[i] = sourceElement[i] / (float)ushort.MaxValue;
                                }
                            }
                        }
                        break;
                    case VertexAttributeFormat.SNorm16:
                        {
                            for (int vertexIndex = 0; vertexIndex < vertexAttributeData.Length; vertexIndex++)
                            {
                                var sourceElement = (short*)(sourcePtr + stride * vertexIndex);
                                var destinationElement = destinationPtr + dimension * vertexIndex;
                                for (int i = 0; i < dimension; i++)
                                {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                    Loop.ExpectVectorized();
#endif
                                    destinationElement[i] = math.saturate(sourceElement[i] / (float)short.MaxValue);
                                }
                            }
                        }
                        break;
                    default:
                        throw new NotSupportedException($"The assigned {nameof(VertexAttributeFormat)} is not supported yet.");
                }



            }


        }
        public static unsafe void GetVertexAttributeDataAsInts(this Mesh.MeshData mesh, VertexAttribute vertexAttribute, Span<int> vertexAttributeData, int firstVertex = 0)
        {

            var format = mesh.GetVertexAttributeFormat(vertexAttribute);
            var dimension = mesh.GetVertexAttributeDimension(vertexAttribute);

            var vertexAttributeElementCount = vertexAttributeData.Length / dimension;
            if (firstVertex + vertexAttributeElementCount > mesh.vertexCount)
            {
                throw new ArgumentOutOfRangeException(nameof(vertexAttributeData));
            }

            var streamIndex = mesh.GetVertexAttributeStream(vertexAttribute);
            var stream = mesh.GetVertexData<byte>(streamIndex);
            var offset = mesh.GetVertexAttributeOffset(vertexAttribute);
            var stride = mesh.GetVertexBufferStride(streamIndex);

            var sourcePtr = (byte*)stream.GetUnsafeReadOnlyPtr() + offset + stride * firstVertex;
            fixed (int* destinationPtr = vertexAttributeData)
            {

                switch (format)
                {
                    case VertexAttributeFormat.UInt8:
                        {
                            for (int vertexIndex = 0; vertexIndex < vertexAttributeElementCount; vertexIndex++)
                            {
                                var sourceElement = (byte*)(sourcePtr + stride * vertexIndex);
                                var destinationElement = destinationPtr + dimension * vertexIndex;
                                for (int i = 0; i < dimension; i++)
                                {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                    Loop.ExpectVectorized();
#endif
                                    destinationElement[i] = sourceElement[i];
                                }
                            }
                        }
                        break;
                    case VertexAttributeFormat.SInt8:
                        {
                            for (int vertexIndex = 0; vertexIndex < vertexAttributeElementCount; vertexIndex++)
                            {
                                var sourceElement = (sbyte*)(sourcePtr + stride * vertexIndex);
                                var destinationElement = destinationPtr + dimension * vertexIndex;
                                for (int i = 0; i < dimension; i++)
                                {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                    Loop.ExpectVectorized();
#endif
                                    destinationElement[i] = sourceElement[i];
                                }
                            }
                        }
                        break;
                    case VertexAttributeFormat.UInt16:
                        {
                            for (int vertexIndex = 0; vertexIndex < vertexAttributeElementCount; vertexIndex++)
                            {
                                var sourceElement = (ushort*)(sourcePtr + stride * vertexIndex);
                                var destinationElement = destinationPtr + dimension * vertexIndex;
                                for (int i = 0; i < dimension; i++)
                                {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                    Loop.ExpectVectorized();
#endif
                                    destinationElement[i] = sourceElement[i];
                                }
                            }
                        }
                        break;
                    case VertexAttributeFormat.SInt16:
                        {
                            for (int vertexIndex = 0; vertexIndex < vertexAttributeElementCount; vertexIndex++)
                            {
                                var sourceElement = (short*)(sourcePtr + stride * vertexIndex);
                                var destinationElement = destinationPtr + dimension * vertexIndex;
                                for (int i = 0; i < dimension; i++)
                                {
#if UNITY_BURST_EXPERIMENTAL_LOOP_INTRINSICS
                                    Loop.ExpectVectorized();
#endif
                                    destinationElement[i] = sourceElement[i];
                                }
                            }
                        }
                        break;
                    case VertexAttributeFormat.UInt32:
                    case VertexAttributeFormat.SInt32:
                        {
                            UnsafeUtility.MemCpyStride(destinationPtr, sizeof(int) * dimension, sourcePtr, stride, sizeof(int) * dimension, vertexAttributeElementCount);
                        }
                        break;
                    default:
                        throw new NotSupportedException($"The assigned {nameof(VertexAttributeFormat)} is not supported yet.");
                }

            }
        }

    }
}

