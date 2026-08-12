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

		public event Action<bool> VisibleChanged = delegate { };

		private void Awake()
		{
			Configure();
		}

		private void OnEnable()
		{
			ApplyVisibility();
		}

		public void SetVisible(bool visible)
		{
			if (IsVisible == visible)
				return;

			IsVisible = visible;
			ApplyVisibility();
			VisibleChanged.Invoke(visible);
		}

		// may run before Awake, on a panel deactivated by PermissionGateMenu
		private void ApplyVisibility()
		{
			GetComponent<BoxCollider>().enabled = IsVisible;

			// null until the panel is active and UIDocument has built its tree
			VisualElement root = GetComponent<UIDocument>().rootVisualElement;
			if (root != null)
				root.style.display = IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
		}

		public void Configure()
		{
			UIDocument document = GetComponent<UIDocument>();
			BoxCollider panelCollider = GetComponent<BoxCollider>();
			XRSimpleInteractable interactable = GetComponent<XRSimpleInteractable>();
			XRPokeFilter pokeFilter = GetComponent<XRPokeFilter>();

			Vector2 panelSize = document.worldSpaceSize / PixelsPerUnit;

			panelCollider.isTrigger = true;
			panelCollider.center = Vector3.zero;
			panelCollider.size = new Vector3(panelSize.x, panelSize.y, 0.02f);

			interactable.colliders.Clear();
			interactable.colliders.Add(panelCollider);
			pokeFilter.pokeInteractable = interactable;
			pokeFilter.pokeCollider = panelCollider;
		}
	}
}
