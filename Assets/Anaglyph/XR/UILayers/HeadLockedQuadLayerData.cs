using System;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.Layers;

namespace Anaglyph.XR.UILayers
{
	/// <summary>
	/// A quad layer posed relative to the main camera and submitted in VIEW
	/// space by <see cref="HeadLockedLayersFeature"/>. Has no in-editor emulation.
	/// </summary>
	[Serializable]
	[CompositionLayerData(
		Provider = "Anaglyph",
		Name = "Head-Locked Quad",
		SupportTransform = true,
		Description = "Quad layer fixed to the display, unaffected by reprojection.",
		SuggestedExtenstionTypes = new[] { typeof(TexturesExtension) })]
	public class HeadLockedQuadLayerData : QuadLayerData
	{
	}
}
