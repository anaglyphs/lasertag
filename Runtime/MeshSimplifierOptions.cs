#nullable enable
using System;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System.Runtime.CompilerServices;
namespace Meshia.MeshSimplification
{
    [Serializable]
    public struct MeshSimplifierOptions : IEquatable<MeshSimplifierOptions>
    {
        public static MeshSimplifierOptions Default => new()
        {
            PreserveBorderEdges = false,
            PreserveSurfaceCurvature = false,
            UseBarycentricCoordinateInterpolation = false,
            MinNormalDot = 0.2f,
            EnableSmartLink = true,
            VertexLinkDistance = 0.0001f,
            VertexLinkMinNormalDot = 0.95f,
            VertexLinkColorDistance = 0.01f,
            VertexLinkUvDistance = 0.001f,
        };

        /// <summary>
        /// If you want to suppress hole generation during simplification, enable this option.
        /// </summary>
        [Tooltip("If you want to suppress hole generation during simplification, enable this option.")]
        public bool PreserveBorderEdges;
        public bool PreserveSurfaceCurvature;
        /// <summary>
        /// If you find that the texture is distorted, try toggling this option.
        /// </summary>
        [Tooltip("If you find that the texture is distorted, try toggling this option.")]
        public bool UseBarycentricCoordinateInterpolation;
        /// <summary>
        /// If this option is enabled, vertices that are not originally connected but are close to each other will be included in the first merge candidates. <br/>
        /// Increases the initialization cost.
        /// </summary>
        [Tooltip("If this option is enabled, vertices that are not originally connected but are close to each other will be included in the first merge candidates. \n" +
            "Increases the initialization cost.")]
        public bool EnableSmartLink;
        [Range(-1, 1)]
        public float MinNormalDot;
        /// <summary>
        /// When smart link is enabled, this is used to select candidates for merging vertices that are not originally connected to each other. <br/>
        /// Increasing this value also increases the initialization cost.
        /// </summary>
        [Tooltip("When smart link is enabled, this is used to select candidates for merging vertices that are not originally connected to each other. \n" +
            "Increasing this value also increases the initialization cost.")]
        public float VertexLinkDistance;
        [Range(-1, 1)]
        public float VertexLinkMinNormalDot;
        // This could be HDR color, so there is no Range.
        public float VertexLinkColorDistance;
        [Range(0, 1.41421356237f)]
        public float VertexLinkUvDistance;


        public readonly override bool Equals(object obj)
        {
            return obj is MeshSimplifierOptions options && Equals(options);
        }

        public readonly bool Equals(MeshSimplifierOptions other)
        {
            return PreserveBorderEdges == other.PreserveBorderEdges &&
                   PreserveSurfaceCurvature == other.PreserveSurfaceCurvature &&
                   UseBarycentricCoordinateInterpolation == other.UseBarycentricCoordinateInterpolation &&
                   EnableSmartLink == other.EnableSmartLink &&
                   MinNormalDot == other.MinNormalDot &&
                   VertexLinkDistance == other.VertexLinkDistance &&
                   VertexLinkMinNormalDot == other.VertexLinkMinNormalDot &&
                   VertexLinkColorDistance == other.VertexLinkColorDistance &&
                   VertexLinkUvDistance == other.VertexLinkUvDistance;
        }

        public readonly override int GetHashCode()
        {
            return HashCode.Combine(PreserveBorderEdges, PreserveSurfaceCurvature, MinNormalDot);
        }

        public static bool operator ==(MeshSimplifierOptions left, MeshSimplifierOptions right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(MeshSimplifierOptions left, MeshSimplifierOptions right)
        {
            return !(left == right);
        }
    }
}
