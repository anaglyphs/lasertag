using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// Wires a world-space <see cref="UIDocument"/> up for XR poking, and hides it without
	/// deactivating the GameObject - <see cref="UIDocument"/> discards its visual tree when
	/// disabled, which would invalidate every element reference the panel's menu script holds.
	/// An opaque back face covers the panel from behind while it is placed in the world.
	/// </summary>
	// runs after UIDocument has built its visual tree
	[DefaultExecutionOrder(100)]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(UIDocument))]
	[RequireComponent(typeof(BoxCollider))]
	[RequireComponent(typeof(XRSimpleInteractable))]
	[RequireComponent(typeof(XRPokeFilter))]
	public sealed class UIToolkitPanelXRSetup : MonoBehaviour
	{
		private const float PixelsPerUnit = 100f;
		private const float BackFaceDepthInMeters = 0.002f;

		[SerializeField] private Material backFaceMaterial;

		private UIDocument document;
		private GameObject backFace;

		public bool IsVisible { get; private set; } = true;
		private bool isRendered = true;

		public event Action<bool> VisibleChanged = delegate { };

		private void Awake()
		{
			document = GetComponent<UIDocument>();
			Configure();
			CreateBackFace();
		}

		private void LateUpdate()
		{
			bool showBackFace = isRendered && IsPlacedInWorld;
			if (backFace != null && backFace.activeSelf != showBackFace)
				backFace.SetActive(showBackFace);
		}

		public bool IsPlacedInWorld =>
			document.panelSettings != null && document.panelSettings.renderMode == PanelRenderMode.WorldSpace;

		private void OnEnable()
		{
			ApplyVisibility();
		}

		// Closing can retain the image after input and logical visibility are off.
		public void SetVisible(bool visible) => SetVisible(visible, false);

		public void SetVisible(bool visible, bool keepRendered)
		{
			bool visibilityChanged = IsVisible != visible;
			bool rendered = visible || keepRendered;
			if (!visibilityChanged && isRendered == rendered) return;

			IsVisible = visible;
			isRendered = rendered;
			ApplyVisibility();
			if (visibilityChanged) VisibleChanged.Invoke(visible);
		}

		// MainMenuController may set visibility before this component's Awake.
		private void ApplyVisibility()
		{
			GetComponent<BoxCollider>().enabled = IsVisible;

			// null until the panel is active and UIDocument has built its tree
			VisualElement root = GetComponent<UIDocument>().rootVisualElement;
			if (root != null)
			{
				root.style.display = isRendered ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		// the built-in quad faces -Z, so turning it around makes it visible only from behind the panel
		private void CreateBackFace()
		{
			if (backFaceMaterial == null)
			{
				Debug.LogError($"{name}: {nameof(UIToolkitPanelXRSetup)} has no back face material.", this);
				return;
			}

			BoxCollider panelCollider = GetComponent<BoxCollider>();
			float depthInLocalUnits = BackFaceDepthInMeters / Mathf.Max(transform.lossyScale.z, 1e-4f);

			backFace = new GameObject("Back Face");
			backFace.layer = gameObject.layer;
			backFace.transform.SetParent(transform, false);
			backFace.transform.localPosition = panelCollider.center + Vector3.forward * depthInLocalUnits;
			backFace.transform.localRotation = Quaternion.Euler(0, 180, 0);
			backFace.transform.localScale = new Vector3(panelCollider.size.x, panelCollider.size.y, 1);

			backFace.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
			var backFaceRenderer = backFace.AddComponent<MeshRenderer>();
			backFaceRenderer.sharedMaterial = backFaceMaterial;
			backFaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			backFaceRenderer.receiveShadows = false;
			backFace.SetActive(isRendered && IsPlacedInWorld);
		}

		public Vector2 PanelSizeInLocalUnits => GetComponent<UIDocument>().worldSpaceSize / PixelsPerUnit;

		public Vector2 PanelCenterInLocalUnits
		{
			get
			{
				Vector2 halfSize = PanelSizeInLocalUnits * 0.5f;
				return GetComponent<UIDocument>().pivot switch
				{
					Pivot.TopLeft => new Vector2(halfSize.x, -halfSize.y),
					Pivot.TopCenter => new Vector2(0, -halfSize.y),
					Pivot.TopRight => new Vector2(-halfSize.x, -halfSize.y),
					Pivot.LeftCenter => new Vector2(halfSize.x, 0),
					Pivot.RightCenter => new Vector2(-halfSize.x, 0),
					Pivot.BottomLeft => new Vector2(halfSize.x, halfSize.y),
					Pivot.BottomCenter => new Vector2(0, halfSize.y),
					Pivot.BottomRight => new Vector2(-halfSize.x, halfSize.y),
					_ => Vector2.zero
				};
			}
		}

		/// <summary>Height of the panel's top (side 1) or bottom (side -1) edge above its pivot, in parent units.</summary>
		public float EdgeHeightAbovePivot(int side) =>
			(PanelCenterInLocalUnits.y + side * PanelSizeInLocalUnits.y * 0.5f) * transform.localScale.y;

		[ContextMenu("Configure XR Panel")]
		public void Configure()
		{
			UIDocument document = GetComponent<UIDocument>();
			BoxCollider panelCollider = GetComponent<BoxCollider>();
			XRSimpleInteractable interactable = GetComponent<XRSimpleInteractable>();
			XRPokeFilter pokeFilter = GetComponent<XRPokeFilter>();

			Vector2 panelSize = PanelSizeInLocalUnits;
			Vector2 panelCenter = PanelCenterInLocalUnits;

			panelCollider.isTrigger = true;
			panelCollider.center = new Vector3(panelCenter.x, panelCenter.y, 0);
			panelCollider.size = new Vector3(panelSize.x, panelSize.y, 0.02f);

			interactable.colliders.Clear();
			interactable.colliders.Add(panelCollider);
			pokeFilter.pokeInteractable = interactable;
			pokeFilter.pokeCollider = panelCollider;
		}
	}
}
