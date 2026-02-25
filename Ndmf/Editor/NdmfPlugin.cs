#nullable enable
using Meshia.MeshSimplification.Ndmf.Editor;
using Meshia.MeshSimplification.Ndmf.Editor.Preview;
using nadena.dev.ndmf;
using nadena.dev.ndmf.preview;
using System;
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;


[assembly: ExportsPlugin(typeof(NdmfPlugin))]

namespace Meshia.MeshSimplification.Ndmf.Editor
{
    class NdmfPlugin : Plugin<NdmfPlugin>
    {
        public override string DisplayName => "Meshia NDMF Mesh Simplifier";

        protected override void Configure()
        {
#if ENABLE_MODULAR_AVATAR

            InPhase(BuildPhase.Resolving)
                .Run("Resolve References", context =>
                {
                    var meshiaCascadingMeshSimplifiers = context.AvatarRootObject.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(true);
                    foreach (var cascadingMeshSimplifier in meshiaCascadingMeshSimplifiers)
                    {
                        cascadingMeshSimplifier.ResolveReferences();
                    }
                });

#endif

            InPhase(BuildPhase.Optimizing)
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Simplify meshes", context =>
                {
                    var meshiaMeshSimplifiers = context.AvatarRootObject.GetComponentsInChildren<MeshiaMeshSimplifier>(true);
#if ENABLE_MODULAR_AVATAR
                    
                    var meshiaCascadingMeshSimplifiers = context.AvatarRootObject.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(true);
#endif

                    using (ListPool<(Mesh Mesh, MeshSimplificationTarget Target, MeshSimplifierOptions Options, BitArray? preserveBorderEdgesBoneIndices, Mesh Destination)>.Get(out var parameters))
                    {
                        foreach (var meshiaMeshSimplifier in meshiaMeshSimplifiers)
                        {
                            if(meshiaMeshSimplifier.enabled && meshiaMeshSimplifier.TryGetComponent<Renderer>(out var renderer))
                            {
                                var sourceMesh = RendererUtility.GetRequiredMesh(renderer);
                                Mesh simplifiedMesh = new();
                                parameters.Add((sourceMesh, meshiaMeshSimplifier.target, meshiaMeshSimplifier.options, null, simplifiedMesh));
                            }
                        }
#if ENABLE_MODULAR_AVATAR

                        foreach (var meshiaCascadingMeshSimplifier in meshiaCascadingMeshSimplifiers)
                        {
                            foreach (var entry in meshiaCascadingMeshSimplifier.Entries)
                            {
                                if (!entry.IsValid(meshiaCascadingMeshSimplifier) || !entry.Enabled) continue;
                                var mesh = RendererUtility.GetRequiredMesh(entry.GetTargetRenderer(meshiaCascadingMeshSimplifier)!);
                                var target = new MeshSimplificationTarget() { Kind = MeshSimplificationTargetKind.AbsoluteTriangleCount, Value = entry.TargetTriangleCount };
                                Mesh simplifiedMesh = new();

                                var preserveBorderEdgesBoneIndices = MeshiaCascadingAvatarMeshSimplifier.GetPreserveBorderEdgesBoneIndices(context.AvatarRootObject, meshiaCascadingMeshSimplifier, entry);

                                parameters.Add((mesh, target, entry.Options, preserveBorderEdgesBoneIndices, simplifiedMesh));
                            }
                        }

#endif

                        MeshSimplifier.SimplifyBatch(parameters);
                        {
                            var i = 0;

                            foreach (var meshiaMeshSimplifier in meshiaMeshSimplifiers)
                            {
                                if(meshiaMeshSimplifier.enabled && meshiaMeshSimplifier.TryGetComponent<Renderer>(out var renderer))
                                {
                                    var (mesh, target, options, _, simplifiedMesh) = parameters[i++];
                                    AssetDatabase.AddObjectToAsset(simplifiedMesh, context.AssetContainer);
                                    RendererUtility.SetMesh(renderer, simplifiedMesh);
                                }
                            }
                            foreach (var meshiaMeshSimplifier in meshiaMeshSimplifiers)
                            {
                                UnityEngine.Object.DestroyImmediate(meshiaMeshSimplifier);
                            }

#if ENABLE_MODULAR_AVATAR

                            foreach (var meshiaCascadingMeshSimplifier in meshiaCascadingMeshSimplifiers)
                            {
                                foreach (var cascadingTarget in meshiaCascadingMeshSimplifier.Entries)
                                {
                                    if (!cascadingTarget.IsValid(meshiaCascadingMeshSimplifier) || !cascadingTarget.Enabled) continue;
                                    var renderer = cascadingTarget.GetTargetRenderer(meshiaCascadingMeshSimplifier)!;
                                    var (mesh, target, options, _, simplifiedMesh) = parameters[i++];
                                    AssetDatabase.AddObjectToAsset(simplifiedMesh, context.AssetContainer);
                                    RendererUtility.SetMesh(renderer, simplifiedMesh);

                                }
                            }

                            foreach (var meshiaCascadingMeshSimplifier in meshiaCascadingMeshSimplifiers)
                            {
                                UnityEngine.Object.DestroyImmediate(meshiaCascadingMeshSimplifier);
                            }
#endif

                        }
                    }
                }).PreviewingWith(new IRenderFilter[]
                {
                    new MeshiaMeshSimplifierPreview(),
#if ENABLE_MODULAR_AVATAR
                    new MeshiaCascadingAvatarMeshSimplifierPreview(),
#endif
                })
            ;
        }
    }
}
