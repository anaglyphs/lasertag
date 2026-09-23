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

		public bool IsVisible { get; private set; } = true;
		private bool isRendered = true;

		public event Action<bool> VisibleChanged = delegate { };

		private void Awake()
		{
			Configure();
		}

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

		[ContextMenu("Configure XR Panel")]
		public void Configure()
		{
			UIDocument document = GetComponent<UIDocument>();
			BoxCollider panelCollider = GetComponent<BoxCollider>();
			XRSimpleInteractable interactable = GetComponent<XRSimpleInteractable>();
			XRPokeFilter pokeFilter = GetComponent<XRPokeFilter>();

			Vector2 panelSize = document.worldSpaceSize / PixelsPerUnit;
			Vector2 halfSize = panelSize * 0.5f;
			Vector2 panelCenter = document.pivot switch
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
