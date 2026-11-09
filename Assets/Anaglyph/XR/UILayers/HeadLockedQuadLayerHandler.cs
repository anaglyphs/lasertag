using System.Runtime.InteropServices;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.Layers;
using Unity.XR.CompositionLayers.Services;
using UnityEngine;
using UnityEngine.XR.OpenXR.CompositionLayers;
using UnityEngine.XR.OpenXR.NativeTypes;

namespace Anaglyph.XR.UILayers
{
	/// <summary>
	/// Same memory layout as <see cref="XrCompositionLayerQuad"/>. Being a separate
	/// type gives this handler its own static callback instance in
	/// <see cref="OpenXRCustomLayerHandler{T}"/> instead of sharing Unity's quad handler's.
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	public struct HeadLockedQuad
	{
		public XrCompositionLayerQuad Quad;
	}

	/// <summary>
	/// Mono, local-texture quad layers submitted in VIEW space, posed by their
	/// transform relative to the main camera.
	/// </summary>
	internal class HeadLockedQuadLayerHandler : OpenXRCustomLayerHandler<HeadLockedQuad>
	{
		private const uint BothEyes = 0;

		protected override bool CreateSwapchain(CompositionLayerManager.LayerInfo layerInfo,
			out SwapchainCreateInfo swapchainCreateInfo)
		{
			swapchainCreateInfo = default;

			Texture texture = GetSourceTexture(layerInfo);
			if (texture == null)
				return false;

			swapchainCreateInfo = new XrSwapchainCreateInfo
			{
				Type = (uint)XrStructureType.XR_TYPE_SWAPCHAIN_CREATE_INFO,
				UsageFlags = (ulong)(XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_SAMPLED_BIT |
				                     XrSwapchainUsageFlags.XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT),
				Format = OpenXRLayerUtility.GetDefaultColorFormat(),
				SampleCount = 1,
				Width = (uint)texture.width,
				Height = (uint)texture.height,
				FaceCount = 1,
				ArraySize = 1,
				MipCount = 1,
			};
			return true;
		}

		protected override bool CreateNativeLayer(CompositionLayerManager.LayerInfo layerInfo,
			SwapchainCreatedOutput swapchainOutput, out HeadLockedQuad nativeLayer)
		{
			nativeLayer = default;

			if (GetSourceTexture(layerInfo) == null)
				return false;

			nativeLayer.Quad.Type = (uint)XrStructureType.XR_TYPE_COMPOSITION_LAYER_QUAD;
			nativeLayer.Quad.EyeVisibility = BothEyes;
			nativeLayer.Quad.SubImage.Swapchain = swapchainOutput.handle;
			Refresh(layerInfo, ref nativeLayer.Quad);
			return true;
		}

		protected override bool ModifyNativeLayer(CompositionLayerManager.LayerInfo layerInfo,
			ref HeadLockedQuad nativeLayer)
		{
			if (GetSourceTexture(layerInfo) == null)
				return false;

			Refresh(layerInfo, ref nativeLayer.Quad);
			return true;
		}

		protected override bool ActiveNativeLayer(CompositionLayerManager.LayerInfo layerInfo,
			ref HeadLockedQuad nativeLayer)
		{
			if (GetSourceTexture(layerInfo) == null || HeadLockedLayersFeature.ViewSpace == 0)
				return false;

			Refresh(layerInfo, ref nativeLayer.Quad);
			return base.ActiveNativeLayer(layerInfo, ref nativeLayer);
		}

		private static void Refresh(CompositionLayerManager.LayerInfo layerInfo, ref XrCompositionLayerQuad quad)
		{
			Texture texture = GetSourceTexture(layerInfo);
			var data = (QuadLayerData)layerInfo.Layer.LayerData;
			Transform layerTransform = layerInfo.Layer.transform;

			quad.Space = HeadLockedLayersFeature.ViewSpace;
			quad.LayerFlags = data.BlendType == BlendType.Premultiply
				? XrCompositionLayerFlags.SourceAlpha
				: XrCompositionLayerFlags.SourceAlpha | XrCompositionLayerFlags.UnPremultipliedAlpha;

			Pose poseInHead = GetPoseRelativeToMainCamera(layerTransform);
			quad.Pose = new XrPosef(poseInHead.position, poseInHead.rotation);

			Vector2 size = data.GetScaledSize(layerTransform.lossyScale);
			quad.Size = new XrExtent2Df { Width = size.x, Height = size.y };

			quad.SubImage.ImageRect = new XrRect2Di
			{
				Offset = new XrOffset2Di { X = 0, Y = 0 },
				Extent = new XrExtent2Di { Width = texture.width, Height = texture.height },
			};
		}

		private static Pose GetPoseRelativeToMainCamera(Transform layerTransform)
		{
			Camera head = CompositionLayerManager.mainCameraCache;

			if (head == null)
				return new Pose(layerTransform.localPosition, layerTransform.localRotation);

			Transform headTransform = head.transform;
			Quaternion inverseHeadRotation = Quaternion.Inverse(headTransform.rotation);

			return new Pose(
				inverseHeadRotation * (layerTransform.position - headTransform.position),
				inverseHeadRotation * layerTransform.rotation);
		}

		private static Texture GetSourceTexture(CompositionLayerManager.LayerInfo layerInfo)
		{
			var textures = layerInfo.Layer.GetComponent<TexturesExtension>();

			if (textures == null || !textures.enabled ||
			    textures.sourceTexture != TexturesExtension.SourceTextureEnum.LocalTexture)
				return null;

			return textures.LeftTexture;
		}
	}
}
