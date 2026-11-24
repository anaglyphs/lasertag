using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.Layers;
using Unity.XR.CompositionLayers.Services;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using UnityEngine.XR.OpenXR.CompositionLayers;

namespace Anaglyph.XR.UILayers
{
	/// <summary>
	/// Shows a fixed-size world-space UIDocument on an underlay quad layer while
	/// composition layers are displayed. The document stays world-space, so input
	/// is unchanged: it moves to a Unity layer only a mirror camera renders, and a
	/// depth-tested hole lets the underlay show through wherever nothing is in front.
	/// The layer is hidden while the main camera is behind the panel.
	/// </summary>
	[RequireComponent(typeof(UIDocument))]
	public class UnderlaidUIDocument : MonoBehaviour
	{
		private const float MirrorCameraDistance = 0.05f;

		[SerializeField, Tooltip("Unity layer that the main camera does not render.")]
		private string mirroredUnityLayerName = "Composited UI";

		[SerializeField] private Shader holeShader;

		[SerializeField, Tooltip("URP renderer index for the mirror camera: one without features, rendering the mirrored Unity layer.")]
		private int mirrorCameraRendererIndex = 1;

		[SerializeField, Tooltip("Must match the world-space PanelSettings' Pixels Per Unit.")]
		private float panelPixelsPerUnit = 100;

		[SerializeField, Tooltip("Layer texture pixels per panel pixel.")]
		private float textureScale = 1.5f;

		private UIDocument document;
		private int displayedUnityLayer;
		private int mirroredUnityLayer;

		private GameObject layerRoot;
		private CompositionLayer compositionLayer;
		private Camera mirrorCamera;
		private RenderTexture layerTexture;
		private Material holeMaterial;

		private static bool CompositionLayersDisplayed => OpenXRLayerProvider.isStarted;

		private bool IsDocumentDisplayed
		{
			get
			{
				VisualElement root = document.rootVisualElement;
				return root != null && root.resolvedStyle.display != DisplayStyle.None;
			}
		}

		private bool IsViewedFromFront
		{
			get
			{
				Camera viewer = Camera.main;
				return viewer == null ||
					Vector3.Dot(viewer.transform.position - transform.position, transform.forward) < 0;
			}
		}

		private void Awake()
		{
			document = GetComponent<UIDocument>();
			displayedUnityLayer = gameObject.layer;
			mirroredUnityLayer = LayerMask.NameToLayer(mirroredUnityLayerName);

			if (mirroredUnityLayer < 0)
				Debug.LogError($"{name}: Unity layer '{mirroredUnityLayerName}' does not exist.", this);

			if (document.worldSpaceSizeMode != WorldSpaceSizeMode.Fixed)
				Debug.LogWarning($"{name}: {nameof(UnderlaidUIDocument)} expects a fixed-size document.", this);
		}

		private void OnDisable()
		{
			gameObject.layer = displayedUnityLayer;
		}

		private void OnDestroy()
		{
			Destroy(holeMaterial);

			if (layerTexture != null)
				layerTexture.Release();

			Destroy(layerTexture);
		}

		private void LateUpdate()
		{
			bool composited = mirroredUnityLayer >= 0 && CompositionLayersDisplayed;
			gameObject.layer = composited ? mirroredUnityLayer : displayedUnityLayer;

			bool showLayer = composited && IsDocumentDisplayed && IsViewedFromFront;

			if (showLayer && layerRoot == null)
				CreateLayer();

			if (layerRoot == null)
				return;

			if (layerRoot.activeSelf != showLayer)
				layerRoot.SetActive(showLayer);

			if (!showLayer)
				return;

			PlaceMirrorCamera();
			KeepPassthroughBehindUnderlays();
		}

		private Vector2 PanelSizeInUnits => document.worldSpaceSize / panelPixelsPerUnit;

		private Vector2 PanelCenterFromPivot
		{
			get
			{
				Vector2 half = PanelSizeInUnits * 0.5f;
				return document.pivot switch
				{
					Pivot.TopLeft => new Vector2(half.x, -half.y),
					Pivot.TopCenter => new Vector2(0, -half.y),
					Pivot.TopRight => new Vector2(-half.x, -half.y),
					Pivot.LeftCenter => new Vector2(half.x, 0),
					Pivot.RightCenter => new Vector2(-half.x, 0),
					Pivot.BottomLeft => new Vector2(half.x, half.y),
					Pivot.BottomCenter => new Vector2(0, half.y),
					Pivot.BottomRight => new Vector2(-half.x, half.y),
					_ => Vector2.zero,
				};
			}
		}

		private void CreateLayer()
		{
			Vector2 panelSize = PanelSizeInUnits;

			layerTexture = new RenderTexture(
				Mathf.CeilToInt(document.worldSpaceSize.x * textureScale),
				Mathf.CeilToInt(document.worldSpaceSize.y * textureScale),
				16, RenderTextureFormat.ARGB32)
			{
				name = $"{name} Underlay",
			};
			layerTexture.Create();

			// built inactive so the layer registers only once fully configured
			layerRoot = new GameObject(layerTexture.name);
			layerRoot.SetActive(false);
			layerRoot.transform.SetParent(transform, false);
			layerRoot.transform.localPosition = PanelCenterFromPivot;

			var layerData = new QuadLayerData
			{
				Size = panelSize,
				ApplyTransformScale = true,
				BlendType = BlendType.Premultiply,
			};
			compositionLayer = layerRoot.AddComponent<CompositionLayer>();
			compositionLayer.ChangeLayerDataType(layerData);

			var textures = layerRoot.AddComponent<TexturesExtension>();
			textures.sourceTexture = TexturesExtension.SourceTextureEnum.LocalTexture;
			textures.LeftTexture = layerTexture;

			CreateHole(panelSize);
			CreateMirrorCamera();

			layerRoot.SetActive(true);
			compositionLayer.Order = CompositionLayerManager.GetFirstUnusedLayer(false);
		}

		private void CreateHole(Vector2 panelSize)
		{
			holeMaterial = new Material(holeShader) { name = $"{name} Hole" };

			var hole = new GameObject("Hole");
			hole.layer = displayedUnityLayer;
			hole.transform.SetParent(layerRoot.transform, false);
			hole.transform.localScale = new Vector3(panelSize.x, panelSize.y, 1);

			hole.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
			var holeRenderer = hole.AddComponent<MeshRenderer>();
			holeRenderer.sharedMaterial = holeMaterial;
			holeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			holeRenderer.receiveShadows = false;
		}

		private void CreateMirrorCamera()
		{
			var cameraObject = new GameObject("Mirror Camera");
			cameraObject.transform.SetParent(layerRoot.transform, false);

			mirrorCamera = cameraObject.AddComponent<Camera>();
			mirrorCamera.orthographic = true;
			mirrorCamera.cullingMask = 1 << mirroredUnityLayer;
			mirrorCamera.clearFlags = CameraClearFlags.SolidColor;
			mirrorCamera.backgroundColor = Color.clear;
			mirrorCamera.targetTexture = layerTexture;
			mirrorCamera.stereoTargetEye = StereoTargetEyeMask.None;
			mirrorCamera.allowHDR = false;
			mirrorCamera.allowMSAA = false;
			mirrorCamera.useOcclusionCulling = false;
			mirrorCamera.nearClipPlane = MirrorCameraDistance * 0.5f;
			mirrorCamera.farClipPlane = MirrorCameraDistance * 1.5f;

			var urpCamera = mirrorCamera.GetUniversalAdditionalCameraData();
			urpCamera.SetRenderer(mirrorCameraRendererIndex);
			urpCamera.renderPostProcessing = false;
			urpCamera.antialiasing = AntialiasingMode.None;
			urpCamera.requiresDepthTexture = false;
			urpCamera.requiresColorTexture = false;
			urpCamera.renderShadows = false;
		}

		// camera view ignores transform scale, so its pose and size are set in world units
		private void PlaceMirrorCamera()
		{
			Transform layerTransform = layerRoot.transform;
			float worldHeight = PanelSizeInUnits.y * layerTransform.lossyScale.y;

			mirrorCamera.transform.SetPositionAndRotation(
				layerTransform.position - layerTransform.forward * MirrorCameraDistance,
				layerTransform.rotation);
			mirrorCamera.orthographicSize = worldHeight * 0.5f;
			mirrorCamera.aspect = (float)layerTexture.width / layerTexture.height;
		}

		private static void KeepPassthroughBehindUnderlays()
		{
			var passthroughType = CompositionLayerManager.PassthroughLayerType;
			var layers = CompositionLayerManager.Instance?.CompositionLayers;

			if (passthroughType == null || layers == null)
				return;

			CompositionLayer passthrough = null;
			int lowestOtherOrder = 0;

			foreach (CompositionLayer layer in layers)
			{
				if (layer == null || layer.LayerData == null)
					continue;

				if (passthroughType.IsInstanceOfType(layer.LayerData))
					passthrough = layer;
				else
					lowestOtherOrder = Mathf.Min(lowestOtherOrder, layer.Order);
			}

			if (passthrough != null && lowestOtherOrder < 0 && passthrough.Order > lowestOtherOrder)
				passthrough.Order = CompositionLayerManager.GetNextUnusedLayer(lowestOtherOrder);
		}
	}
}
