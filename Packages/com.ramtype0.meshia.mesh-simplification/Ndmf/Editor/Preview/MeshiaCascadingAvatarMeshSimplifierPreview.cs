#nullable enable
#if ENABLE_MODULAR_AVATAR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf.preview;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf.Editor.Preview
{
    internal class MeshiaCascadingAvatarMeshSimplifierPreview : MeshiaMeshSimplifierPreviewBase<MeshiaCascadingAvatarMeshSimplifierPreview>
    {
        public override ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            var groups = new List<RenderGroup>();
            foreach (var root in context.GetAvatarRoots())
            {
                if (context.ActiveInHierarchy(root) is false) continue;
                foreach (var component in context.GetComponentsInChildren<MeshiaCascadingAvatarMeshSimplifier>(root, true))
                {
                    var componentEnabled = context.Observe(component.gameObject, g => g.activeInHierarchy);
                    if (!componentEnabled) continue;

                    var targetCount = context.Observe(component, c => c.Entries.Count());
                    for (int i = 0; i < targetCount; i++)
                    {
                        var index = i;
                        var targetEnabled = context.Observe(component, c => c.Entries[index].IsValid(c) && c.Entries[index].Enabled);
                        if (!targetEnabled) continue;

                        var renderer = component.Entries[index].GetTargetRenderer(component)!;
                        groups.Add(RenderGroup.For(renderer).WithData<(MeshiaCascadingAvatarMeshSimplifier, int)>((component, index)));
                    }
                }
            }
            return groups.ToImmutableList();
        }
        
        protected override (MeshSimplificationTarget, MeshSimplifierOptions, BitArray?) QueryTarget(ComputeContext context, RenderGroup group, Renderer original, Renderer proxy)
        {
            var data = group.GetData<(MeshiaCascadingAvatarMeshSimplifier, int)>();
            var component = data.Item1;
            var index = data.Item2;

            var cascadingTarget = context.Observe(component, c => c.Entries[index] with { }, (a, b) => a.Equals(b));
            var target = new MeshSimplificationTarget() { Kind = MeshSimplificationTargetKind.AbsoluteTriangleCount, Value = cascadingTarget.TargetTriangleCount };

            var avatarRoot = context.GetAvatarRoot(original.gameObject);
            var preserveBorderEdgeBoneIndices = MeshiaCascadingAvatarMeshSimplifier.GetPreserveBorderEdgesBoneIndices(avatarRoot, component, cascadingTarget);
            return (target, cascadingTarget.Options, preserveBorderEdgeBoneIndices);
        }

        
    }
}

#endif
