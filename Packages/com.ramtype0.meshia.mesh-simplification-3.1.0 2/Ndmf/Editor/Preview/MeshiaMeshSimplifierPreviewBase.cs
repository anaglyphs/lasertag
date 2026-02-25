#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using nadena.dev.ndmf.preview;
using System.Collections;

namespace Meshia.MeshSimplification.Ndmf.Editor.Preview
{
    internal abstract class MeshiaMeshSimplifierPreviewBase<TDerived> : IRenderFilter
        where TDerived : MeshiaMeshSimplifierPreviewBase<TDerived>
    {
        public static readonly Dictionary<Renderer, (int proxy, int simplified)> TriangleCountCache = new();

        public static TogglablePreviewNode PreviewControlNode { get; } = TogglablePreviewNode.Create(
                () => typeof(TDerived).Name,
                qualifiedName: typeof(TDerived).FullName
            );

        static TogglablePreviewNode[] PreviewControlNodes { get; } = { PreviewControlNode };

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes() => PreviewControlNodes;

        public bool IsEnabled(ComputeContext context)
        {
            return context.Observe(PreviewControlNode.IsEnabled);
        }

        public static bool IsEnabled() => PreviewControlNode.IsEnabled.Value;

        public abstract ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context);

        async Task<IRenderFilterNode> IRenderFilter.Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs, ComputeContext context)
        {
            var original = proxyPairs.First().Item1;
            var proxy = proxyPairs.First().Item2;
            var proxyMesh = RendererUtility.GetRequiredMesh(proxy);

            var (target, options, preserveBorderEdgesBoneIndices) = QueryTarget(context, group, original, proxy);

            Mesh simplifiedMesh = new();
            try
            {
                await MeshSimplifier.SimplifyAsync(proxyMesh, target, options, preserveBorderEdgesBoneIndices, simplifiedMesh);
            }
            catch (Exception)
            {
                UnityEngine.Object.DestroyImmediate(simplifiedMesh);
                throw;
            }

            TriangleCountCache[original] = (proxyMesh.GetTriangleCount(), simplifiedMesh.GetTriangleCount());
         
            return new NdmfMeshSimplifierPreviewNode(simplifiedMesh);
        }

        protected abstract (MeshSimplificationTarget, MeshSimplifierOptions, BitArray?) QueryTarget(ComputeContext context, RenderGroup group, Renderer original, Renderer proxy);
    }

    internal class NdmfMeshSimplifierPreviewNode : IRenderFilterNode
    {
        public RenderAspects WhatChanged => RenderAspects.Mesh;
        private readonly Mesh _simplifiedMesh;

        public NdmfMeshSimplifierPreviewNode(Mesh mesh)
        {
            _simplifiedMesh = mesh;
        }

        public void OnFrame(Renderer original, Renderer proxy)
        {
            RendererUtility.SetMesh(proxy, _simplifiedMesh);
        }

        void IDisposable.Dispose() => UnityEngine.Object.DestroyImmediate(_simplifiedMesh);
    }
}
