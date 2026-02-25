using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meshia.MeshSimplification;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.TestTools;

namespace Meshia.MeshSimplification.Tests
{
    public class MeshSimplifierTests
    {
        static Mesh GetPrimitiveMesh(PrimitiveType type)
        {
            var gameObject = GameObject.CreatePrimitive(type);
            var mesh = gameObject.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(gameObject);
            return mesh;
        }
        [TestCase(PrimitiveType.Sphere)]
        [TestCase(PrimitiveType.Capsule)]
        [TestCase(PrimitiveType.Cylinder)]
        public async Task ShouldSimplifyPrimitive(PrimitiveType type)
        {
            var mesh = GetPrimitiveMesh(type);

            MeshSimplificationTarget target = new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.5f,
            };
            Mesh simplifiedMesh = new();
            await MeshSimplifier.SimplifyAsync(mesh, target, MeshSimplifierOptions.Default, simplifiedMesh);
            Assert.LessOrEqual(simplifiedMesh.vertexCount, mesh.vertexCount * 0.5f);
            Object.Destroy(simplifiedMesh);
        }
        [TestCase(PrimitiveType.Sphere)]
        [TestCase(PrimitiveType.Capsule)]
        [TestCase(PrimitiveType.Cylinder)]
        public async Task ShouldSimplifyPrimitiveIncrementally(PrimitiveType type)
        {
            var allocator = Allocator.Persistent;
            var mesh = GetPrimitiveMesh(type);
            using var blendShapes = BlendShapeData.GetMeshBlendShapes(mesh, allocator);

            Assert.Zero(blendShapes.Length, "Primitive meshes should not have blend shapes by default.");
            var meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
            var meshData = meshDataArray[0];

            using MeshSimplifier meshSimplifier = new(allocator);

            var load = meshSimplifier.ScheduleLoadMeshData(meshData, MeshSimplifierOptions.Default);

            while (!load.IsCompleted)
            {
                await Task.Yield();
            }

            load.Complete();

            Mesh simplifiedMesh1 = await SimplifyToTarget(new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.5f,
            });

            Assert.LessOrEqual(simplifiedMesh1.vertexCount, mesh.vertexCount * 0.5f);

            Object.Destroy(simplifiedMesh1);
            Mesh simplifiedMesh2 = await SimplifyToTarget(new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.3f,
            });

            Assert.LessOrEqual(simplifiedMesh2.vertexCount, mesh.vertexCount * 0.3f);
            Object.Destroy(simplifiedMesh2);

            async Task<Mesh> SimplifyToTarget(MeshSimplificationTarget target)
            {
                var destinationMeshDataArray = Mesh.AllocateWritableMeshData(1);
                var destinationMeshData = destinationMeshDataArray[0];

                Mesh simplifiedMesh = new();
                var simplify = meshSimplifier.ScheduleSimplify(meshData, blendShapes, target, new JobHandle());

                using NativeList<BlendShapeData> destinationBlendShapes = new(allocator);
                var write = meshSimplifier.ScheduleWriteMeshData(meshData, blendShapes, destinationMeshData, destinationBlendShapes, simplify);

                while (!write.IsCompleted)
                {
                    await Task.Yield();
                }

                write.Complete();

                Assert.Zero(destinationBlendShapes.Length, "Primitive meshes should not have blend shapes after simplification.");

                Mesh.ApplyAndDisposeWritableMeshData(destinationMeshDataArray, simplifiedMesh);
                return simplifiedMesh;
            }
        }
        [TestCase(PrimitiveType.Sphere)]
        [TestCase(PrimitiveType.Capsule)]
        [TestCase(PrimitiveType.Cylinder)]
        public async Task ShouldSimplifyPrimitiveWithDuplicatedSubMeshes(PrimitiveType type)
        {
            var mesh = Object.Instantiate(GetPrimitiveMesh(type));
            var originalSubMeshCount = mesh.subMeshCount;
            mesh.subMeshCount += 1;
            mesh.SetTriangles(mesh.GetTriangles(originalSubMeshCount - 1), originalSubMeshCount);

            MeshSimplificationTarget target = new()
            {
                Kind = MeshSimplificationTargetKind.RelativeVertexCount,
                Value = 0.5f,
            };
            Mesh simplifiedMesh = new();
            await MeshSimplifier.SimplifyAsync(mesh, target, MeshSimplifierOptions.Default, simplifiedMesh);
            Assert.LessOrEqual(simplifiedMesh.vertexCount, mesh.vertexCount * 0.5f);
            Object.Destroy(mesh);
            Object.Destroy(simplifiedMesh);
        }
    }

}
