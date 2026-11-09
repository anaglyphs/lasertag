using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.Layers;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.XR.UILayers
{
	/// <summary>
	/// Shows a fixed-size, center-pivoted world-space UIDocument on a head-locked
	/// composition layer while <see cref="HeadLockedLayersFeature"/> is available,
	/// and as ordinary world-space UI otherwise. The layer takes the document's
	/// place: same pose, same physical size.
	/// </summary>
	[RequireComponent(typeof(UIDocument))]
	public class HeadLockedUIDocument : MonoBehaviour
	{
		[SerializeField, Tooltip("Must match the world-space PanelSettings' Pixels Per Unit.")]
		private float panelPixelsPerUnit = 100;

		[SerializeField, Tooltip("Layer texture pixels per panel pixel.")]
		private float textureScale = 1.5f;

		private UIDocument document;
		private PanelSettings worldSpacePanelSettings;
		private PanelSettings layerPanelSettings;
		private RenderTexture layerTexture;
		private GameObject layerObject;

		private bool IsOnLayer => layerObject != null && layerObject.activeSelf;

		private void Awake()
		{
			document = GetComponent<UIDocument>();
			worldSpacePanelSettings = document.panelSettings;

			if (document.worldSpaceSizeMode != WorldSpaceSizeMode.Fixed || document.pivot != Pivot.Center)
				Debug.LogWarning($"{name}: {nameof(HeadLockedUIDocument)} expects a fixed-size, " +
				                 "center-pivoted document; the layer will not match it.", this);
		}

		private void LateUpdate()
		{
			bool layerAvailable = HeadLockedLayersFeature.IsAvailable;

			if (layerAvailable != IsOnLayer)
				ShowOnLayer(layerAvailable);
		}

		private void OnDestroy()
		{
			Destroy(layerPanelSettings);

			if (layerTexture != null)
				layerTexture.Release();

			Destroy(layerTexture);
		}

		private void ShowOnLayer(bool onLayer)
		{
			if (onLayer && layerObject == null)
				CreateLayer();

			document.panelSettings = onLayer ? layerPanelSettings : worldSpacePanelSettings;

			if (layerObject != null)
				layerObject.SetActive(onLayer);
		}

		private void CreateLayer()
		{
			Vector2 panelSize = document.worldSpaceSize;

			layerTexture = new RenderTexture(
				Mathf.CeilToInt(panelSize.x * textureScale),
				Mathf.CeilToInt(panelSize.y * textureScale),
				0, RenderTextureFormat.ARGB32)
			{
				name = $"{name} Head-Locked Layer",
			};
			layerTexture.Create();

			layerPanelSettings = Instantiate(worldSpacePanelSettings);
			layerPanelSettings.name = layerTexture.name;
			layerPanelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
			layerPanelSettings.targetTexture = layerTexture;
			layerPanelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
			layerPanelSettings.scale = textureScale;
			layerPanelSettings.clearColor = true;
			layerPanelSettings.colorClearValue = Color.clear;

			// built inactive so the layer registers only once fully configured
			layerObject = new GameObject(layerTexture.name);
			layerObject.SetActive(false);
			layerObject.transform.SetParent(transform, false);

			var layerData = new HeadLockedQuadLayerData
			{
				Size = panelSize / panelPixelsPerUnit,
				ApplyTransformScale = true,
				BlendType = BlendType.Premultiply,
			};
			layerObject.AddComponent<CompositionLayer>().ChangeLayerDataType(layerData);

			var textures = layerObject.AddComponent<TexturesExtension>();
			textures.sourceTexture = TexturesExtension.SourceTextureEnum.LocalTexture;
			textures.LeftTexture = layerTexture;
		}
	}
}
