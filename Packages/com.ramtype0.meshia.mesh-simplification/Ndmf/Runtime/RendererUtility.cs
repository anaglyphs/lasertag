#nullable enable

using System;
using UnityEngine;

namespace Meshia.MeshSimplification.Ndmf
{
    public class RendererUtility
    {
        public static Mesh GetRequiredMesh(Renderer renderer) => GetMesh(renderer) ?? throw new ArgumentException("The associated renderer does not have mesh.");
        public static Mesh? GetMesh(Renderer renderer)
        {
            switch (renderer)
            {
                case MeshRenderer meshRenderer:
                    {
                        if (meshRenderer.TryGetComponent<MeshFilter>(out var meshFilter))
                        {
                            var mesh = meshFilter.sharedMesh;
                            if (mesh == null) return null;
                            return mesh;
                        }
                        else
                        {
                            return null;
                        }
                    }
                case SkinnedMeshRenderer skinnedMeshRenderer:
                    {
                        var mesh = skinnedMeshRenderer.sharedMesh;
                        if (mesh == null) return null;
                        return mesh;
                    }
                default:
                    throw new ArgumentException($"Unsupported type of renderer: {renderer.GetType()}");
            }
        }
        
        public static void SetMesh(Renderer renderer, Mesh mesh)
        {
            switch (renderer)
            {
                case MeshRenderer meshrenderer:
                    var meshfilter = meshrenderer.GetComponent<MeshFilter>();
                    if (meshfilter == null) throw new ArgumentException($"The associated renderer was {nameof(MeshRenderer)}, but it has no {nameof(MeshFilter)}.");
                    meshfilter.sharedMesh = mesh;
                    break;
                case SkinnedMeshRenderer skinnedMeshRenderer:
                    skinnedMeshRenderer.sharedMesh = mesh;
                    break;
                default:
                    throw new ArgumentException($"Unsupported type of renderer: {renderer.GetType()}");
            }
        }
    }
}