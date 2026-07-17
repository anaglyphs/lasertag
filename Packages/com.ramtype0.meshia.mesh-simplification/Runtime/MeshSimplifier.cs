#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.Rendering;

namespace Meshia.MeshSimplification
{
    public struct MeshSimplifier : INativeDisposable
    {
        NativeList<float3> VertexPositionBuffer;

        NativeList<float4> VertexNormalBuffer;
        NativeList<float4> VertexTangentBuffer;

        NativeList<float4> VertexColorBuffer;

        NativeList<float4> VertexTexCoord0Buffer;
        NativeList<float4> VertexTexCoord1Buffer;
        NativeList<float4> VertexTexCoord2Buffer;
        NativeList<float4> VertexTexCoord3Buffer;
        NativeList<float4> VertexTexCoord4Buffer;
        NativeList<float4> VertexTexCoord5Buffer;
        NativeList<float4> VertexTexCoord6Buffer;
        NativeList<float4> VertexTexCoord7Buffer;

        NativeList<float> VertexBlendWeightBuffer;
        NativeList<uint> VertexBlendIndicesBuffer;

        NativeList<uint> VertexContainingSubMeshIndices;

        NativeList<ErrorQuadric> VertexErrorQuadrics;

        NativeList<int> VertexVersions;

        NativeList<int3> Triangles;
        NativeList<float3> TriangleNormals;

        NativeParallelMultiHashMap<int, int> VertexMergeOpponentVertices;
        NativeParallelMultiHashMap<int, int> VertexContainingTriangles;

        NativeBitArray VertexIsDiscardedBits;
        NativeBitArray VertexIsBorderEdgeBits;
        NativeBitArray TriangleIsDiscardedBits;

        NativeHashSet<int2> SmartLinks;

        NativeMinPriorityQueue<VertexMerge> VertexMerges;

        MeshSimplifierOptions Options;

        AllocatorManager.AllocatorHandle Allocator;
        /// <summary>
        /// Simplifies the given <paramref name="mesh"/> and writes the result to <paramref name="destination"/>.
        /// </summary>
        /// <param name="mesh">The mesh to simplify.</param>
        /// <param name="target">The simplification target for this mesh simplification.</param>
        /// <param name="options">The options for this mesh simplification.</param>
        /// <param name="destination">The destination to write simplified mesh.</param>
        /// <remarks>To process multiple meshes at once, use <see cref="SimplifyBatch(IReadOnlyList{ValueTuple{Mesh, MeshSimplificationTarget, MeshSimplifierOptions, Mesh}})"/> instead.</remarks>
        public static void Simplify(Mesh mesh, MeshSimplificationTarget target, MeshSimplifierOptions options, Mesh destination) 
            => Simplify(mesh, target, options, null, destination);
        /// <summary>
        /// Simplifies the given <paramref name="mesh"/> and writes the result to <paramref name="destination"/>.
        /// </summary>
        /// <param name="mesh">The mesh to simplify.</param>
        /// <param name="target">The simplification target for this mesh simplification.</param>
        /// <param name="options">The options for this mesh simplification.</param>
        /// <param name="destination">The destination to write simplified mesh.</param>
        /// <remarks>To process multiple meshes at once, use <see cref="SimplifyBatch(IReadOnlyList{ValueTuple{Mesh, MeshSimplificationTarget, MeshSimplifierOptions, Mesh}})"/> instead.</remarks>
        public static void Simplify(Mesh mesh, MeshSimplificationTarget target, MeshSimplifierOptions options, BitArray? preserveBorderEdgesBoneIndices, Mesh destination)
        {
            Allocator allocator = Unity.Collections.Allocator.TempJob;
            var originalMeshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            var originalMeshData = originalMeshDataArray[0];
            var blendShapes = BlendShapeData.GetMeshBlendShapes(mesh, allocator);

            var meshSimplifier = new MeshSimplifier(allocator);

            NativeBitArray nativePreserveBorderEdgesBoneIndices = new(preserveBorderEdgesBoneIndices?.Length ?? 0, allocator, NativeArrayOptions.UninitializedMemory);
            if (preserveBorderEdgesBoneIndices is not null)
            {
                for (int i = 0; i < preserveBorderEdgesBoneIndices.Length; i++)
                {
                    nativePreserveBorderEdgesBoneIndices.Set(i, preserveBorderEdgesBoneIndices[i]);
                }
            }

            var load = meshSimplifier.ScheduleLoadMeshData(originalMeshData, options);

            var simplifiedMeshDataArray = Mesh.AllocateWritableMeshData(1);
            NativeList<BlendShapeData> simplifiedBlendShapes = new(allocator);
            var simplify = meshSimplifier.ScheduleSimplify(originalMeshData, blendShapes, target, nativePreserveBorderEdgesBoneIndices, load);
            nativePreserveBorderEdgesBoneIndices.Dispose(simplify);
            var write = meshSimplifier.ScheduleWriteMeshData(originalMeshData, blendShapes, simplifiedMeshDataArray[0], simplifiedBlendShapes, simplify);
            meshSimplifier.Dispose(write);
            JobHandle.ScheduleBatchedJobs();
            write.Complete();

            originalMeshDataArray.Dispose();

            foreach (var blendShape in blendShapes)
            {
                blendShape.Dispose();
            }
            blendShapes.Dispose();

            ApplySimplifiedMesh(mesh, simplifiedMeshDataArray, simplifiedBlendShapes.AsArray(), destination);

            foreach (var simplifiedBlendShape in simplifiedBlendShapes)
            {
                simplifiedBlendShape.Dispose();
            }
            simplifiedBlendShapes.Dispose();
        }
        public static void SimplifyBatch(IReadOnlyList<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options, Mesh Destination)> parameters) 
            => SimplifyBatch(parameters.Select<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options, Mesh Destination), (Mesh, MeshSimplificationTarget, MeshSimplifierOptions, BitArray?, Mesh)>(p => (p.Mesh, p.Target, p.Options, null, p.Destination)).ToList());

        public static void SimplifyBatch(IReadOnlyList<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options, BitArray? PreserveBorderEdgesBoneIndices, Mesh Destination)> parameters)
        {
            Allocator allocator = Unity.Collections.Allocator.TempJob;

            using (ListPool<Mesh>.Get(out var meshes))
            using (ListPool<Mesh>.Get(out var destinations))
            {
                Span<NativeList<BlendShapeData>> blendShapesList = stackalloc NativeList<BlendShapeData>[parameters.Count];
                Span<NativeList<BlendShapeData>> simplifiedBlendShapesList = stackalloc NativeList<BlendShapeData>[parameters.Count];
                Span<MeshSimplifier> meshSimplifiers = stackalloc MeshSimplifier[parameters.Count];
                Span<JobHandle> jobHandles = stackalloc JobHandle[parameters.Count];
                foreach (var parameter in parameters)
                {
                    meshes.Add(parameter.Mesh);
                    destinations.Add(parameter.Destination);
                }

                var originalMeshDataArray = Mesh.AcquireReadOnlyMeshData(meshes);
                var simplifiedMeshDataArray = Mesh.AllocateWritableMeshData(originalMeshDataArray.Length);

                for (int i = 0; i < parameters.Count; i++)
                {
                    var (mesh, target, options, preserveBorderEdgesBoneIndices, destination) = parameters[i];
                    var originalMeshData = originalMeshDataArray[i];
                    var blendShapes = BlendShapeData.GetMeshBlendShapes(mesh, allocator);
                    blendShapesList[i] = blendShapes;
                    var meshSimplifier = new MeshSimplifier(allocator);
                    NativeBitArray nativePreserveBorderEdgesBoneIndices = new(preserveBorderEdgesBoneIndices?.Length ?? 0, allocator, NativeArrayOptions.UninitializedMemory);
                    if (preserveBorderEdgesBoneIndices is not null)
                    {
                        for (int boneIndex = 0; boneIndex < preserveBorderEdgesBoneIndices.Length; boneIndex++)
                        {
                            nativePreserveBorderEdgesBoneIndices.Set(boneIndex, preserveBorderEdgesBoneIndices[boneIndex]);
                        }
                    }
                    var load = meshSimplifier.ScheduleLoadMeshData(originalMeshData, options);
                    NativeList<BlendShapeData> simplifiedBlendShapes = new(allocator);
                    simplifiedBlendShapesList[i] = simplifiedBlendShapes;

                    var simplify = meshSimplifier.ScheduleSimplify(originalMeshData, blendShapes, target, nativePreserveBorderEdgesBoneIndices, load);
                    nativePreserveBorderEdgesBoneIndices.Dispose(simplify);
                    var write = meshSimplifier.ScheduleWriteMeshData(originalMeshData, blendShapes, simplifiedMeshDataArray[i], simplifiedBlendShapes, simplify);
                    meshSimplifier.Dispose(write);
                    jobHandles[i] = write;

                }
                JobHandle.ScheduleBatchedJobs();
                jobHandles.CombineDependencies().Complete();
                originalMeshDataArray.Dispose();
                foreach (var blendShapes in blendShapesList)
                {
                    foreach (var blendShape in blendShapes)
                    {
                        blendShape.Dispose();
                    }
                    blendShapes.Dispose();
                }
                ApplySimplifiedMeshes(meshes, simplifiedMeshDataArray, simplifiedBlendShapesList, destinations);
                foreach (var simplifiedBlendShapes in simplifiedBlendShapesList)
                {
                    foreach (var simplifiedBlendShape in simplifiedBlendShapes)
                    {
                        simplifiedBlendShape.Dispose();
                    }
                    simplifiedBlendShapes.Dispose();
                }
            }


        }
        /// <summary>
        /// Asynchronously simplifies the given <paramref name="mesh"/> and writes the result to <paramref name="destination"/>.
        /// </summary>
        /// <param name="mesh">The mesh to simplify.</param>
        /// <param name="target">The simplification target for this mesh simplification.</param>
        /// <param name="options">The options for this mesh simplification.</param>
        /// <param name="destination">The destination to write simplified mesh.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
        /// <returns>A task that represents the asynchronous mesh simplification operation.</returns>
        public static Task SimplifyAsync(Mesh mesh, MeshSimplificationTarget target, MeshSimplifierOptions options, Mesh destination, CancellationToken cancellationToken = default)
            => SimplifyAsync(mesh, target, options, null, destination, cancellationToken);

        /// <summary>
        /// Asynchronously simplifies the given <paramref name="mesh"/> and writes the result to <paramref name="destination"/>.
        /// </summary>
        /// <param name="mesh">The mesh to simplify.</param>
        /// <param name="target">The simplification target for this mesh simplification.</param>
        /// <param name="options">The options for this mesh simplification.</param>
        /// <param name="destination">The destination to write simplified mesh.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="CancellationToken.None"/>.</param>
        /// <returns>A task that represents the asynchronous mesh simplification operation.</returns>
        public static async Task SimplifyAsync(Mesh mesh, MeshSimplificationTarget target, MeshSimplifierOptions options, BitArray? preserveBorderEdgesBoneIndices, Mesh destination, CancellationToken cancellationToken = default)
        {
            Allocator allocator = Unity.Collections.Allocator.Persistent;
            var originalMeshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            var originalMeshData = originalMeshDataArray[0];
            var blendShapes = BlendShapeData.GetMeshBlendShapes(mesh, allocator);

            var meshSimplifier = new MeshSimplifier(allocator);

            NativeBitArray nativePreserveBorderEdgesBoneIndices = new(preserveBorderEdgesBoneIndices?.Length ?? 0, allocator, NativeArrayOptions.UninitializedMemory);
            if(preserveBorderEdgesBoneIndices is not null)
            {
                for (int i = 0; i < preserveBorderEdgesBoneIndices.Length; i++)
                {
                    nativePreserveBorderEdgesBoneIndices.Set(i, preserveBorderEdgesBoneIndices[i]);
                }
            }

            var load = meshSimplifier.ScheduleLoadMeshData(originalMeshData, options, nativePreserveBorderEdgesBoneIndices);

            var simplifiedMeshDataArray = Mesh.AllocateWritableMeshData(1);
            NativeList<BlendShapeData> simplifiedBlendShapes = new(allocator);
            var simplify = meshSimplifier.ScheduleSimplify(originalMeshData, blendShapes, target, nativePreserveBorderEdgesBoneIndices, load);
            nativePreserveBorderEdgesBoneIndices.Dispose(simplify);
            var write = meshSimplifier.ScheduleWriteMeshData(originalMeshData, blendShapes, simplifiedMeshDataArray[0], simplifiedBlendShapes, simplify);
            meshSimplifier.Dispose(write);
            JobHandle.ScheduleBatchedJobs();
            while (!write.IsCompleted)
            {
                await Task.Yield();
            }
            write.Complete();

            originalMeshDataArray.Dispose();

            foreach (var blendShape in blendShapes)
            {
                blendShape.Dispose();
            }
            blendShapes.Dispose();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ApplySimplifiedMesh(mesh, simplifiedMeshDataArray, simplifiedBlendShapes.AsArray(), destination);
            }
            catch (OperationCanceledException)
            {
                simplifiedMeshDataArray.Dispose();
                throw;
            }
            finally
            {
                foreach (var simplifiedBlendShape in simplifiedBlendShapes)
                {
                    simplifiedBlendShape.Dispose();
                }
                simplifiedBlendShapes.Dispose();
            }
        }

        private static void ApplySimplifiedMesh(Mesh mesh, Mesh.MeshDataArray simplifiedMeshDataArray, ReadOnlySpan<BlendShapeData> simplifiedBlendShapes, Mesh destination)
        {
            Mesh.ApplyAndDisposeWritableMeshData(simplifiedMeshDataArray, destination, MeshUpdateFlags.DontValidateIndices);
            CopyBoundsAndBindposes(mesh, destination);

            // https://github.com/RamType0/Meshia.MeshSimplification/issues/23
            // Setting blend shapes on a mesh with no vertices possibly causes crash.
            if (destination.vertexCount != 0)
            {
                BlendShapeData.SetBlendShapes(destination, simplifiedBlendShapes);
            }
            else
            {
                destination.ClearBlendShapes();
            }
        }

        private static void CopyBoundsAndBindposes(Mesh source, Mesh destination)
        {
            if (source != destination)
            {
                destination.bounds = source.bounds;
#if UNITY_2023_1_OR_NEWER
                var bindposes = source.GetBindposes();
                if (bindposes.Length != 0)
                {
                    destination.SetBindposes(bindposes);
                }
#else
                var bindposes = source.bindposes;
                if (bindposes.Length != 0)
                {
                    destination.bindposes = bindposes;
                }
#endif
            }
        }

        private static void ApplySimplifiedMeshes(List<Mesh> meshes, Mesh.MeshDataArray simplifiedMeshDataArray, ReadOnlySpan<NativeList<BlendShapeData>> simplifiedBlendShapesList, List<Mesh> destinations)
        {
            Mesh.ApplyAndDisposeWritableMeshData(simplifiedMeshDataArray, destinations, MeshUpdateFlags.DontValidateIndices);
            for (int i = 0; i < meshes.Count; i++)
            {
                var mesh = meshes[i];
                var destination = destinations[i];
                var simplifiedBlendShapes = simplifiedBlendShapesList[i];
                CopyBoundsAndBindposes(mesh, destination);
                BlendShapeData.SetBlendShapes(destination, simplifiedBlendShapes.AsArray());
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MeshSimplifier"/> with the given allocator.
        /// </summary>
        /// <param name="allocator">The <see cref="AllocatorManager.AllocatorHandle"/> for this <see cref="MeshSimplifier"/>.</param>
        public MeshSimplifier(AllocatorManager.AllocatorHandle allocator)
        {
            VertexPositionBuffer = new(allocator);

            VertexNormalBuffer = new(allocator);
            VertexTangentBuffer = new(allocator);

            VertexColorBuffer = new(allocator);

            VertexTexCoord0Buffer = new(allocator);
            VertexTexCoord1Buffer = new(allocator);
            VertexTexCoord2Buffer = new(allocator);
            VertexTexCoord3Buffer = new(allocator);
            VertexTexCoord4Buffer = new(allocator);
            VertexTexCoord5Buffer = new(allocator);
            VertexTexCoord6Buffer = new(allocator);
            VertexTexCoord7Buffer = new(allocator);

            VertexBlendWeightBuffer = new(allocator);
            VertexBlendIndicesBuffer = new(allocator);

            VertexContainingSubMeshIndices = new(allocator);

            VertexErrorQuadrics = new(allocator);


            VertexVersions = new(allocator);

            Triangles = new(allocator);
            TriangleNormals = new(allocator);

            VertexMergeOpponentVertices = new(0, allocator);
            VertexContainingTriangles = new(0, allocator);

            VertexIsDiscardedBits = new(0, allocator, NativeArrayOptions.UninitializedMemory);
            VertexIsBorderEdgeBits = new(0, allocator, NativeArrayOptions.UninitializedMemory);
            TriangleIsDiscardedBits = new(0, allocator, NativeArrayOptions.UninitializedMemory);

            SmartLinks = new(0, allocator);

            VertexMerges = new(allocator);
            Options = default;

            Allocator = allocator;

        }

        /// <summary>
        /// Creates and schedules a job that will load mesh data from the <paramref name="meshData"/> into this <see cref="MeshSimplifier"/>.
        /// </summary>
        /// <param name="meshData">The mesh data to load.</param>
        /// <param name="options">The options for this mesh simplification.</param>
        /// <param name="dependency">The handle of a job which the new job will depend upon.</param>
        /// <returns>The handle of a new job that will load mesh data from the <paramref name="meshData"/> into this <see cref="MeshSimplifier"/>.</returns>
        public JobHandle ScheduleLoadMeshData(Mesh.MeshData meshData, MeshSimplifierOptions options, JobHandle dependency = default)
        {
            NativeBitArray preserveBorderEdgesBoneIndices = new(0, Allocator);
            var jobHandle = ScheduleLoadMeshData(meshData, options, preserveBorderEdgesBoneIndices, dependency);

            preserveBorderEdgesBoneIndices.Dispose(jobHandle);
            return jobHandle;
        }
        
        /// <summary>
         /// Creates and schedules a job that will load mesh data from the <paramref name="meshData"/> into this <see cref="MeshSimplifier"/>.
         /// </summary>
         /// <param name="meshData">The mesh data to load.</param>
         /// <param name="options">The options for this mesh simplification.</param>
         /// <param name="dependency">The handle of a job which the new job will depend upon.</param>
         /// <returns>The handle of a new job that will load mesh data from the <paramref name="meshData"/> into this <see cref="MeshSimplifier"/>.</returns>
        public JobHandle ScheduleLoadMeshData(Mesh.MeshData meshData, MeshSimplifierOptions options, NativeBitArray preserveBorderEdgesBoneIndices, JobHandle dependency = default)
        {
            Options = options;
            var constructVertexPositionBuffer = ScheduleCopyVertexPositionBuffer(meshData, dependency);
            var constructVertexNormalBuffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.Normal, dependency);
            var constructVertexTangentBuffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.Tangent, dependency);
            var constructVertexColorBuffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.Color, dependency);
            var constructVertexTexcoord0Buffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.TexCoord0, dependency);
            var constructVertexTexcoord1Buffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.TexCoord1, dependency);
            var constructVertexTexcoord2Buffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.TexCoord2, dependency);
            var constructVertexTexcoord3Buffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.TexCoord3, dependency);
            var constructVertexTexcoord4Buffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.TexCoord4, dependency);
            var constructVertexTexcoord5Buffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.TexCoord5, dependency);
            var constructVertexTexcoord6Buffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.TexCoord6, dependency);
            var constructVertexTexcoord7Buffer = ScheduleCopyVertexAttributeBufferAsFloat4(meshData, VertexAttribute.TexCoord7, dependency);
            var constructVertexBlendWeightBuffer = ScheduleCopyVertexBlendWeightBuffer(meshData, dependency);
            var constructVertexBlendIndicesBuffer = ScheduleCopyVertexBlendIndicesBuffer(meshData, dependency);

            var collectVertexContainingSubMeshIndices = ScheduleCollectVertexContainingSubMeshIndices(meshData, dependency);
            var initializeVertexVersions = ScheduleInitializeVertexVersions(meshData, dependency);

            var constructTriangles = ScheduleCopyTriangles(meshData, dependency);

            var constructVertexContainingTrianglesAndTriangleDiscardedBits = ScheduleInitializeVertexContainingTrianglesAndTriangleIsDiscardedBits(constructTriangles);

            var constructEdges = ScheduleConstructEdges(out var edges, Triangles, constructTriangles, Allocator);

            var constructVertexIsDiscardedBits = ScheduleInitializeVertexIsDiscardedBits(meshData, dependency, constructVertexContainingTrianglesAndTriangleDiscardedBits);
            var collectSmartLinks = options.EnableSmartLink
                ? ScheduleCollectSmartLinks(
                    meshData,
                    VertexPositionBuffer,
                    VertexNormalBuffer,
                    VertexColorBuffer,
                    VertexTexCoord0Buffer,
                    VertexTexCoord1Buffer,
                    VertexTexCoord2Buffer,
                    VertexTexCoord3Buffer,
                    VertexTexCoord4Buffer,
                    VertexTexCoord5Buffer,
                    VertexTexCoord6Buffer,
                    VertexTexCoord7Buffer,
                    VertexIsDiscardedBits,
                    options,
                    SmartLinks,
                    dependency,
                    constructVertexPositionBuffer,
                    stackalloc[]
                    {
                        constructVertexNormalBuffer,
                        constructVertexColorBuffer,
                        constructVertexTexcoord0Buffer,
                        constructVertexTexcoord1Buffer,
                        constructVertexTexcoord2Buffer,
                        constructVertexTexcoord3Buffer,
                        constructVertexTexcoord4Buffer,
                        constructVertexTexcoord5Buffer,
                        constructVertexTexcoord6Buffer,
                        constructVertexTexcoord7Buffer,
                    }.CombineDependencies(),
                    constructVertexIsDiscardedBits,
                    Allocator
                    )
                : new JobHandle();


            var constructMergePairs = ScheduleConstructMergePairs(out var mergePairs, edges, SmartLinks, JobHandle.CombineDependencies(constructEdges, collectSmartLinks), Allocator);

            var constructVertexIsBorderEdgeBits = ScheduleInitializeVertexIsBorderEdgeBits(meshData, edges, dependency, constructEdges);
            NativeList<ErrorQuadric> triangleErrorQuadrics = new(Allocator);
            var constructTriangleNormalsAndErrorQuadrics = ScheduleInitializeTriangleNormalsAndTriangleErrorQuadrics(meshData, triangleErrorQuadrics, dependency, constructVertexPositionBuffer, constructTriangles);

            var constructVertexErrorQuadrics = ScheduleInitializeVertexErrorQuadrics(meshData, edges, triangleErrorQuadrics, dependency, constructVertexPositionBuffer, constructTriangles, constructVertexContainingTrianglesAndTriangleDiscardedBits, constructEdges, constructTriangleNormalsAndErrorQuadrics);

            edges.Dispose(JobHandle.CombineDependencies(constructMergePairs, constructVertexIsBorderEdgeBits, constructVertexErrorQuadrics));

            triangleErrorQuadrics.Dispose(constructVertexErrorQuadrics);

            var constructVertexMerges = ScheduleInitializeVertexMerges(mergePairs, preserveBorderEdgesBoneIndices, constructVertexPositionBuffer, constructVertexBlendIndicesBuffer, constructVertexErrorQuadrics, constructTriangleNormalsAndErrorQuadrics, constructVertexContainingTrianglesAndTriangleDiscardedBits, constructVertexIsBorderEdgeBits, constructMergePairs);

            mergePairs.Dispose(constructVertexMerges);

            return stackalloc[]
            {
                dependency,

                constructVertexPositionBuffer,

                constructVertexNormalBuffer,
                constructVertexTangentBuffer,

                constructVertexColorBuffer,

                constructVertexTexcoord0Buffer,
                constructVertexTexcoord1Buffer,
                constructVertexTexcoord2Buffer,
                constructVertexTexcoord3Buffer,
                constructVertexTexcoord4Buffer,
                constructVertexTexcoord5Buffer,
                constructVertexTexcoord6Buffer,
                constructVertexTexcoord7Buffer,

                constructVertexBlendWeightBuffer,
                constructVertexBlendIndicesBuffer,

                collectVertexContainingSubMeshIndices,

                initializeVertexVersions,

                constructVertexErrorQuadrics,

                constructTriangles,
                constructTriangleNormalsAndErrorQuadrics,

                constructVertexContainingTrianglesAndTriangleDiscardedBits,

                constructVertexIsDiscardedBits,
                constructVertexIsBorderEdgeBits,

                collectSmartLinks,
                constructVertexMerges,

            }.CombineDependencies();

        }
        /// <summary>
        /// Creates and schedules a job that will simplify the mesh data.
        /// </summary>
        /// <param name="meshData">The mesh data to simplify. It must be the same with the mesh which was passed to <see cref="ScheduleLoadMeshData(Mesh.MeshData, MeshSimplifierOptions, JobHandle)"/>.</param>
        /// <param name="blendShapes">The blend shapes of the <paramref name="meshData"/>.</param>
        /// <param name="target">The simplification target for this mesh simplification.</param>
        /// <param name="dependency">The handle of a job which the new job will depend upon.</param>
        /// <returns>The handle of the new job.</returns>
        /// <remarks>
        /// After you call <see cref="ScheduleLoadMeshData(Mesh.MeshData, MeshSimplifierOptions, JobHandle)"/>, you can call this method repeatedly to incrementally simplify the same mesh data with different targets.
        /// </remarks>
        public JobHandle ScheduleSimplify(Mesh.MeshData meshData, NativeList<BlendShapeData> blendShapes, MeshSimplificationTarget target, JobHandle dependency)
        {

            NativeBitArray preserveBorderEdgesBoneIndices = new(0, Allocator);
            var jobHandle = ScheduleSimplify(meshData, blendShapes, target, preserveBorderEdgesBoneIndices, dependency);

            preserveBorderEdgesBoneIndices.Dispose(jobHandle);

            return jobHandle;
        }/// <summary>
         /// Creates and schedules a job that will simplify the mesh data.
         /// </summary>
         /// <param name="meshData">The mesh data to simplify. It must be the same with the mesh which was passed to <see cref="ScheduleLoadMeshData(Mesh.MeshData, MeshSimplifierOptions, JobHandle)"/>.</param>
         /// <param name="blendShapes">The blend shapes of the <paramref name="meshData"/>.</param>
         /// <param name="target">The simplification target for this mesh simplification.</param>
         /// <param name="dependency">The handle of a job which the new job will depend upon.</param>
         /// <returns>The handle of the new job.</returns>
         /// <remarks>
         /// After you call <see cref="ScheduleLoadMeshData(Mesh.MeshData, MeshSimplifierOptions, JobHandle)"/>, you can call this method repeatedly to incrementally simplify the same mesh data with different targets.
         /// </remarks>
        public JobHandle ScheduleSimplify(Mesh.MeshData meshData, NativeList<BlendShapeData> blendShapes, MeshSimplificationTarget target, NativeBitArray preserveBorderEdgesBoneIndices, JobHandle dependency)
        {
            return new SimplifyJob
            {
                Mesh = meshData,
                SimplificationTarget = target,
                VertexPositionBuffer = VertexPositionBuffer.AsDeferredJobArray(),
                VertexNormalBuffer = VertexNormalBuffer.AsDeferredJobArray(),
                VertexTangentBuffer = VertexTangentBuffer.AsDeferredJobArray(),
                VertexColorBuffer = VertexColorBuffer.AsDeferredJobArray(),
                VertexTexCoord0Buffer = VertexTexCoord0Buffer.AsDeferredJobArray(),
                VertexTexCoord1Buffer = VertexTexCoord1Buffer.AsDeferredJobArray(),
                VertexTexCoord2Buffer = VertexTexCoord2Buffer.AsDeferredJobArray(),
                VertexTexCoord3Buffer = VertexTexCoord3Buffer.AsDeferredJobArray(),
                VertexTexCoord4Buffer = VertexTexCoord4Buffer.AsDeferredJobArray(),
                VertexTexCoord5Buffer = VertexTexCoord5Buffer.AsDeferredJobArray(),
                VertexTexCoord6Buffer = VertexTexCoord6Buffer.AsDeferredJobArray(),
                VertexTexCoord7Buffer = VertexTexCoord7Buffer.AsDeferredJobArray(),
                BlendShapes = blendShapes,
                VertexBlendWeightBuffer = VertexBlendWeightBuffer.AsDeferredJobArray(),
                VertexBlendIndicesBuffer = VertexBlendIndicesBuffer.AsDeferredJobArray(),
                VertexContainingSubMeshIndices = VertexContainingSubMeshIndices.AsDeferredJobArray(),
                VertexVersions = VertexVersions.AsDeferredJobArray(),
                Triangles = Triangles.AsDeferredJobArray(),
                TriangleNormals = TriangleNormals.AsDeferredJobArray(),
                VertexContainingTriangles = VertexContainingTriangles,
                VertexErrorQuadrics = VertexErrorQuadrics.AsDeferredJobArray(),
                VertexMergeOpponentVertices = VertexMergeOpponentVertices,
                DiscardedTriangle = TriangleIsDiscardedBits,
                DiscardedVertex = VertexIsDiscardedBits,
                VertexIsBorderEdgeBits = VertexIsBorderEdgeBits,
                Options = Options,
                VertexMerges = VertexMerges,
                PreserveBorderEdgesBoneIndices = preserveBorderEdgesBoneIndices,
                SmartLinks = SmartLinks,
            }.Schedule(dependency);
        }


        /// <summary>
        /// Creates and schedules a job that will simplify the mesh data..
        /// </summary>
        /// <param name="meshData">The original mesh data.</param>
        /// <param name="blendShapes">The blend shapes of the <paramref name="meshData"/>.</param>
        /// <param name="destinationMeshData">The destination to write simplified mesh data.</param>
        /// <param name="destinationBlendShapes">The destination to write simplified blend shapes.</param>
        /// <param name="dependency">The handle of a job which the new job will depend upon.</param>
        /// <returns>The handle of the new job.</returns>
        /// <remarks>
        /// After you call <see cref="ScheduleLoadMeshData(Mesh.MeshData, MeshSimplifierOptions, JobHandle)"/>, you can call this method repeatedly to incrementally simplify the same mesh data with different targets.
        /// </remarks>
        public JobHandle ScheduleWriteMeshData(Mesh.MeshData meshData, NativeList<BlendShapeData> blendShapes, Mesh.MeshData destinationMeshData, NativeList<BlendShapeData> destinationBlendShapes, JobHandle dependency)
        {
            return new WriteToMeshDataJob
            {
                SourceMesh = meshData,
                DestinationMesh = destinationMeshData,
                DestinationBlendShapes = destinationBlendShapes,
                VertexPositionBuffer = VertexPositionBuffer.AsDeferredJobArray(),
                VertexNormalBuffer = VertexNormalBuffer.AsDeferredJobArray(),
                VertexTangentBuffer = VertexTangentBuffer.AsDeferredJobArray(),
                VertexColorBuffer = VertexColorBuffer.AsDeferredJobArray(),
                VertexTexCoord0Buffer = VertexTexCoord0Buffer.AsDeferredJobArray(),
                VertexTexCoord1Buffer = VertexTexCoord1Buffer.AsDeferredJobArray(),
                VertexTexCoord2Buffer = VertexTexCoord2Buffer.AsDeferredJobArray(),
                VertexTexCoord3Buffer = VertexTexCoord3Buffer.AsDeferredJobArray(),
                VertexTexCoord4Buffer = VertexTexCoord4Buffer.AsDeferredJobArray(),
                VertexTexCoord5Buffer = VertexTexCoord5Buffer.AsDeferredJobArray(),
                VertexTexCoord6Buffer = VertexTexCoord6Buffer.AsDeferredJobArray(),
                VertexTexCoord7Buffer = VertexTexCoord7Buffer.AsDeferredJobArray(),
                BlendShapes = blendShapes,
                VertexBlendWeightBuffer = VertexBlendWeightBuffer.AsDeferredJobArray(),
                VertexBlendIndicesBuffer = VertexBlendIndicesBuffer.AsDeferredJobArray(),
                VertexContainingSubMeshIndices = VertexContainingSubMeshIndices.AsDeferredJobArray(),
                Triangles = Triangles.AsDeferredJobArray(),
                DiscardedTriangle = TriangleIsDiscardedBits,
                DiscardedVertex = VertexIsDiscardedBits,
                Allocator = Allocator,
            }.Schedule(dependency);
        }
        /// <summary>
        /// Creates and schedules a job that will dispose this <see cref="MeshSimplifier"/>.
        /// </summary>
        /// <param name="inputDeps">The handle of a job which the new job will depend upon.</param>
        /// <returns>The handle of a new job that will dispose this <see cref="MeshSimplifier"/>. The new job depends upon <paramref name="inputDeps"/>.</returns>
        public JobHandle Dispose(JobHandle inputDeps)
        {
            return stackalloc[]
            {
                VertexPositionBuffer.Dispose(inputDeps),
                VertexNormalBuffer.Dispose(inputDeps),
                VertexTangentBuffer.Dispose(inputDeps),
                VertexColorBuffer.Dispose(inputDeps),
                VertexTexCoord0Buffer.Dispose(inputDeps),
                VertexTexCoord1Buffer.Dispose(inputDeps),
                VertexTexCoord2Buffer.Dispose(inputDeps),
                VertexTexCoord3Buffer.Dispose(inputDeps),
                VertexTexCoord4Buffer.Dispose(inputDeps),
                VertexTexCoord5Buffer.Dispose(inputDeps),

                VertexTexCoord6Buffer.Dispose(inputDeps),
                VertexTexCoord7Buffer.Dispose(inputDeps),

                VertexBlendWeightBuffer.Dispose(inputDeps),
                VertexBlendIndicesBuffer.Dispose(inputDeps),

                VertexContainingSubMeshIndices.Dispose(inputDeps),

                VertexErrorQuadrics.Dispose(inputDeps),
                VertexVersions.Dispose(inputDeps),
                Triangles.Dispose(inputDeps),
                TriangleNormals.Dispose(inputDeps),
                VertexMergeOpponentVertices.Dispose(inputDeps),
                VertexContainingTriangles.Dispose(inputDeps),
                VertexIsDiscardedBits.Dispose(inputDeps),

                VertexIsBorderEdgeBits.Dispose(inputDeps),
                TriangleIsDiscardedBits.Dispose (inputDeps),
                SmartLinks.Dispose(inputDeps),
                VertexMerges.Dispose(inputDeps),
            }.CombineDependencies();
        }
        /// <summary>
        /// Disposes this <see cref="MeshSimplifier"/> and releases all its resources.
        /// </summary>
        public void Dispose()
        {
            VertexPositionBuffer.Dispose();
            VertexNormalBuffer.Dispose();
            VertexTangentBuffer.Dispose();
            VertexColorBuffer.Dispose();
            VertexTexCoord0Buffer.Dispose();
            VertexTexCoord1Buffer.Dispose();
            VertexTexCoord2Buffer.Dispose();
            VertexTexCoord3Buffer.Dispose();
            VertexTexCoord4Buffer.Dispose();
            VertexTexCoord5Buffer.Dispose();

            VertexTexCoord6Buffer.Dispose();
            VertexTexCoord7Buffer.Dispose();

            VertexBlendWeightBuffer.Dispose();
            VertexBlendIndicesBuffer.Dispose();

            VertexContainingSubMeshIndices.Dispose();

            VertexErrorQuadrics.Dispose();
            VertexVersions.Dispose();
            Triangles.Dispose();
            TriangleNormals.Dispose();
            VertexMergeOpponentVertices.Dispose();
            VertexContainingTriangles.Dispose();
            VertexIsDiscardedBits.Dispose();

            VertexIsBorderEdgeBits.Dispose();
            TriangleIsDiscardedBits.Dispose();
            SmartLinks.Dispose();
            VertexMerges.Dispose();
        }


        JobHandle ScheduleCopyVertexPositionBuffer(Mesh.MeshData meshData, JobHandle meshDependency)
        {
            return new CopyVertexPositionBufferJob
            {
                Mesh = meshData,
                VertexPositionBuffer = VertexPositionBuffer,
            }.Schedule(meshDependency);
        }
        JobHandle ScheduleCopyVertexAttributeBufferAsFloat4(Mesh.MeshData meshData, VertexAttribute vertexAttribute, JobHandle meshDependency)
        {
            var targetBuffer = vertexAttribute switch
            {
                VertexAttribute.Normal => VertexNormalBuffer,
                VertexAttribute.Tangent => VertexTangentBuffer,
                VertexAttribute.Color => VertexColorBuffer,
                VertexAttribute.TexCoord0 => VertexTexCoord0Buffer,
                VertexAttribute.TexCoord1 => VertexTexCoord1Buffer,
                VertexAttribute.TexCoord2 => VertexTexCoord2Buffer,
                VertexAttribute.TexCoord3 => VertexTexCoord3Buffer,
                VertexAttribute.TexCoord4 => VertexTexCoord4Buffer,
                VertexAttribute.TexCoord5 => VertexTexCoord5Buffer,
                VertexAttribute.TexCoord6 => VertexTexCoord6Buffer,
                VertexAttribute.TexCoord7 => VertexTexCoord7Buffer,
                _ => throw new ArgumentOutOfRangeException(nameof(vertexAttribute)),
            };
            return new CopyVertexAttributeBufferAsFloat4Job
            {
                Mesh = meshData,
                VertexAttribute = vertexAttribute,
                VertexAttributeBuffer = targetBuffer,
            }.Schedule(meshDependency);
        }
        JobHandle ScheduleCopyVertexBlendWeightBuffer(Mesh.MeshData meshData, JobHandle meshDependency)
        {
            return new CopyVertexBlendWeightBufferJob
            {
                Mesh = meshData,
                VertexBlendWeightBuffer = VertexBlendWeightBuffer,
            }.Schedule(meshDependency);
        }
        JobHandle ScheduleCopyVertexBlendIndicesBuffer(Mesh.MeshData meshData, JobHandle meshDependency)
        {
            return new CopyVertexBlendIndicesBufferJob
            {
                Mesh = meshData,
                VertexBlendIndicesBuffer = VertexBlendIndicesBuffer,
            }.Schedule(meshDependency);
        }
        JobHandle ScheduleCollectVertexContainingSubMeshIndices(Mesh.MeshData mesh, JobHandle meshDependency)
        {
            return new CollectVertexContainingSubMeshIndicesJob
            {
                Mesh = mesh,
                VertexContainingSubMeshIndices = VertexContainingSubMeshIndices,
            }.Schedule(meshDependency);
        }

        JobHandle ScheduleInitializeVertexVersions(Mesh.MeshData mesh, JobHandle meshDependency)
        {
            return new InitializeVertexListJob<int>
            {
                MeshData = mesh,
                Buffer = VertexVersions,
                Options = NativeArrayOptions.ClearMemory,
            }.Schedule(meshDependency);
        }

        JobHandle ScheduleCopyTriangles(Mesh.MeshData mesh, JobHandle meshDependency)
        {
            return new CopyTrianglesJob
            {
                Mesh = mesh,
                Triangles = Triangles,
            }.Schedule(meshDependency);
        }
        JobHandle ScheduleInitializeVertexContainingTrianglesAndTriangleIsDiscardedBits(JobHandle trianglesDependency)
        {
            return new CollectVertexContainingTrianglesAndMarkInvalidTrianglesJob
            {
                Triangles = Triangles.AsDeferredJobArray(),
                VertexContainingTriangles = VertexContainingTriangles,
                TriangleIsDiscardedBits = TriangleIsDiscardedBits,
            }.Schedule(trianglesDependency);
        }
        static JobHandle ScheduleConstructEdges(out NativeHashSet<int2> edges, NativeList<int3> triangles, JobHandle dependency, AllocatorManager.AllocatorHandle allocator)
        {
            edges = new(0, allocator);
            return new CollectEdgesJob
            {
                Triangles = triangles.AsDeferredJobArray(),
                Edges = edges,
            }.Schedule(dependency);
        }
        JobHandle ScheduleInitializeVertexIsDiscardedBits(
            Mesh.MeshData mesh,
            JobHandle meshDependency,
            JobHandle vertexContainingTrianglesDependency)
        {
            return new FindNonReferencedVerticesJob
            {
                Mesh = mesh,
                VertexContainingTriangles = VertexContainingTriangles,
                VertexIsDiscardedBits = VertexIsDiscardedBits,
            }.Schedule(JobHandle.CombineDependencies(meshDependency, vertexContainingTrianglesDependency));
        }
        static JobHandle ScheduleCollectSmartLinks(
            Mesh.MeshData mesh,

            NativeList<float3> vertexPositionBuffer,
            NativeList<float4> vertexNormalBuffer,
            NativeList<float4> vertexColorBuffer,
            NativeList<float4> vertexTexcoord0Buffer,
            NativeList<float4> vertexTexcoord1Buffer,
            NativeList<float4> vertexTexcoord2Buffer,
            NativeList<float4> vertexTexcoord3Buffer,
            NativeList<float4> vertexTexcoord4Buffer,
            NativeList<float4> vertexTexcoord5Buffer,
            NativeList<float4> vertexTexcoord6Buffer,
            NativeList<float4> vertexTexcoord7Buffer,
            NativeBitArray vertexIsDiscardedBits,
            MeshSimplifierOptions options,
            NativeHashSet<int2> smartLinks,
            JobHandle meshDependency,
            JobHandle vertexPositionBufferDependency,
            JobHandle otherVertexAttributeBufferDependency,
            JobHandle vertexIsDiscardedBitsDependency,
            AllocatorManager.AllocatorHandle allocator
            )
        {

            NativeList<UnsafeList<int2>> subMeshSmartLinkLists = new(allocator);
            var initializeSubMeshSmartLinkLists = new InitializeSubMeshListJob<UnsafeList<int2>>
            {
                MeshData = mesh,
                Options = NativeArrayOptions.UninitializedMemory,
                Buffer = subMeshSmartLinkLists,
            }.Schedule(meshDependency);
            var collectNeighborVertexPairs = new CollectNeighborVertexPairsJob
            {
                Mesh = mesh,
                VertexPositionBuffer = vertexPositionBuffer.AsDeferredJobArray(),
                VertexIsDiscardedBits = vertexIsDiscardedBits,
                Options = options,
                SubMeshSmartLinkListAllocator = allocator,
                SubMeshSmartLinkLists = subMeshSmartLinkLists.AsDeferredJobArray(),
            }.Schedule(subMeshSmartLinkLists, 1, JobHandle.CombineDependencies(initializeSubMeshSmartLinkLists, vertexPositionBufferDependency, vertexIsDiscardedBitsDependency));
            var removeHighCostSmartLinks = new RemoveHighCostSmartLinksJob
            {
                VertexNormalBuffer = vertexNormalBuffer.AsDeferredJobArray(),
                VertexColorBuffer = vertexColorBuffer.AsDeferredJobArray(),
                VertexTexCoord0Buffer = vertexTexcoord0Buffer.AsDeferredJobArray(),
                VertexTexCoord1Buffer = vertexTexcoord1Buffer.AsDeferredJobArray(),
                VertexTexCoord2Buffer = vertexTexcoord2Buffer.AsDeferredJobArray(),
                VertexTexCoord3Buffer = vertexTexcoord3Buffer.AsDeferredJobArray(),
                VertexTexCoord4Buffer = vertexTexcoord4Buffer.AsDeferredJobArray(),
                VertexTexCoord5Buffer = vertexTexcoord5Buffer.AsDeferredJobArray(),
                VertexTexCoord6Buffer = vertexTexcoord6Buffer.AsDeferredJobArray(),
                VertexTexCoord7Buffer = vertexTexcoord7Buffer.AsDeferredJobArray(),

                Options = options,
                SubMeshSmartLinkLists = subMeshSmartLinkLists.AsDeferredJobArray(),
            }.Schedule(subMeshSmartLinkLists, 1, JobHandle.CombineDependencies(collectNeighborVertexPairs, otherVertexAttributeBufferDependency));

            var collectSmartLinks = new CollectSmartLinksJob
            {
                SubMeshSmartLinkLists = subMeshSmartLinkLists.AsDeferredJobArray(),
                SmartLinks = smartLinks,
            }.Schedule(removeHighCostSmartLinks);
            subMeshSmartLinkLists.Dispose(collectSmartLinks);
            return collectSmartLinks;
        }
        static JobHandle ScheduleConstructMergePairs(out NativeList<int2> mergePairs, NativeHashSet<int2> edges, NativeHashSet<int2> smartLinks, JobHandle dependency, AllocatorManager.AllocatorHandle allocator)
        {
            mergePairs = new(allocator);
            return new CollectMergePairsJob
            {
                Edges = edges,
                SmartLinks = smartLinks,
                MergePairs = mergePairs,
            }.Schedule(dependency);
        }

        JobHandle ScheduleInitializeVertexIsBorderEdgeBits(Mesh.MeshData mesh, NativeHashSet<int2> edges, JobHandle meshDependency, JobHandle edgesDependency)
        {
            return new MarkBorderEdgeVerticesJob
            {
                Mesh = mesh,
                Edges = edges,
                VertexIsBorderEdgeBits = VertexIsBorderEdgeBits,
            }.Schedule(JobHandle.CombineDependencies(meshDependency, edgesDependency));
        }


        JobHandle ScheduleInitializeTriangleNormalsAndTriangleErrorQuadrics(
            Mesh.MeshData mesh,
            NativeList<ErrorQuadric> triangleErrorQuadrics,
            JobHandle meshDependency,
            JobHandle vertexPositionBufferDependency,
            JobHandle trianglesDependency)
        {

            var initializeTriangleNormalsJob = new InitializeTriangleListJob<float3>
            {
                MeshData = mesh,
                Options = NativeArrayOptions.UninitializedMemory,
                Buffer = TriangleNormals,
            }.Schedule(meshDependency);

            var initializeTriangleErrorQuadricsJob = new InitializeTriangleListJob<ErrorQuadric>
            {
                MeshData = mesh,
                Options = NativeArrayOptions.UninitializedMemory,
                Buffer = triangleErrorQuadrics,
            }.Schedule(meshDependency);

            var computeTriangleNormalsAndErrorQuadrics = new ComputeTriangleNormalsAndErrorQuadricsJob
            {
                VertexPositionBuffer = VertexPositionBuffer.AsDeferredJobArray(),
                Triangles = Triangles.AsDeferredJobArray(),
                TriangleNormals = TriangleNormals.AsDeferredJobArray(),
                TriangleErrorQuadrics = triangleErrorQuadrics.AsDeferredJobArray(),
            }.Schedule(
                Triangles,
            JobsUtility.CacheLineSize,
            stackalloc[]
            {
                vertexPositionBufferDependency,
                trianglesDependency,
                initializeTriangleNormalsJob,
                initializeTriangleErrorQuadricsJob,
            }.CombineDependencies());

            return computeTriangleNormalsAndErrorQuadrics;
        }

        JobHandle ScheduleInitializeVertexErrorQuadrics(
            Mesh.MeshData mesh,
            NativeHashSet<int2> edges,
            NativeList<ErrorQuadric> triangleErrorQuadrics,
            JobHandle meshDependency,
            JobHandle vertexPositionBufferDependency,
            JobHandle trianglesDependency,
            JobHandle vertexContainingTrianglesDependency,
            JobHandle edgesDependency,
            JobHandle triangleErrorQuadricsDependency)
        {

            var initializeVertexErrorQuadricsJob = new InitializeVertexListJob<ErrorQuadric>
            {
                MeshData = mesh,
                Options = NativeArrayOptions.UninitializedMemory,
                Buffer = VertexErrorQuadrics,
            }.Schedule(meshDependency);

            var computeVertexErrorQuadricsJob = new ComputeVertexErrorQuadricsJob
            {
                VertexPositionBuffer = VertexPositionBuffer.AsDeferredJobArray(),
                Triangles = Triangles.AsDeferredJobArray(),
                VertexContainingTriangles = VertexContainingTriangles,
                Edges = edges,
                TriangleErrorQuadrics = triangleErrorQuadrics.AsDeferredJobArray(),
                VertexErrorQuadrics = VertexErrorQuadrics.AsDeferredJobArray(),
            }.Schedule(VertexErrorQuadrics, JobsUtility.CacheLineSize,
            stackalloc[]
            {
                vertexPositionBufferDependency,
                trianglesDependency,
                vertexContainingTrianglesDependency,
                edgesDependency,
                triangleErrorQuadricsDependency,
                initializeVertexErrorQuadricsJob,
            }.CombineDependencies());

            return computeVertexErrorQuadricsJob;
        }
        JobHandle ScheduleInitializeVertexMerges(
            NativeList<int2> edges,
            NativeBitArray preserveBorderEdgesBoneIndices,
            JobHandle vertexPositionBufferDependency,
            JobHandle vertexBlendIndicesBufferDependency,
            JobHandle vertexErrorQuadricsDependency,
            JobHandle triangleNormalsDependency,
            JobHandle vertexContainingTrianglesDependency,
            JobHandle vertexIsBorderEdgeBitsDependency,
            JobHandle edgesDependency)
        {
            NativeList<VertexMerge> unorderedDirtyVertexMerges = new(Allocator);
            var initializeVertexMergesJob = new InitializeUnorderedDirtyVertexMergesJob
            {
                Edges = edges.AsDeferredJobArray(),
                UnorderedDirtyVertexMerges = unorderedDirtyVertexMerges,
            }.Schedule(edgesDependency);

            var computeMergesJob = new ComputeMergesJob
            {
                VertexPositionBuffer = VertexPositionBuffer.AsDeferredJobArray(),
                VertexErrorQuadrics = VertexErrorQuadrics.AsDeferredJobArray(),
                TriangleNormals = TriangleNormals.AsDeferredJobArray(),
                VertexContainingTriangles = VertexContainingTriangles,
                VertexIsBorderEdgeBits = VertexIsBorderEdgeBits,
                Edges = edges.AsDeferredJobArray(),
                UnorderedDirtyVertexMerges = unorderedDirtyVertexMerges.AsDeferredJobArray(),
                
                PreserveBorderEdges = Options.PreserveBorderEdges,
                PreserveSurfaceCurvature = Options.PreserveSurfaceCurvature,
                VertexBlendIndicesBuffer = VertexBlendIndicesBuffer.AsDeferredJobArray(),
                PreserveBorderEdgesBoneIndices = preserveBorderEdgesBoneIndices,
            }.Schedule(edges, JobsUtility.CacheLineSize,
            stackalloc[]
            {
                vertexPositionBufferDependency,
                vertexBlendIndicesBufferDependency,
                vertexErrorQuadricsDependency,
                triangleNormalsDependency,
                vertexContainingTrianglesDependency,
                vertexIsBorderEdgeBitsDependency,
                edgesDependency,
                initializeVertexMergesJob,
            }.CombineDependencies());
            var collectVertexMergeOpponentsJob = new CollectVertexMergeOpponmentsJob
            {
                UnorderedDirtyVertexMerges = unorderedDirtyVertexMerges.AsDeferredJobArray(),
                VertexMergeOpponentVertices = VertexMergeOpponentVertices,
            }.Schedule(computeMergesJob);
            var collectVertexMergesJob = new CollectVertexMergesJob
            {
                UnorderedDirtyVertexMerges = unorderedDirtyVertexMerges.AsDeferredJobArray(),
                VertexMerges = VertexMerges,
            }.Schedule(computeMergesJob);
            var jobHandle = JobHandle.CombineDependencies(collectVertexMergeOpponentsJob, collectVertexMergesJob);
            unorderedDirtyVertexMerges.Dispose(jobHandle);
            return jobHandle;
        }


    }
}
