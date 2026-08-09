using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Anaglyph.Lasertag
{
	[DisallowMultipleComponent]
	[RequireComponent(typeof(UIDocument))]
	[RequireComponent(typeof(BoxCollider))]
	[RequireComponent(typeof(XRSimpleInteractable))]
	[RequireComponent(typeof(XRPokeFilter))]
	public sealed class UIToolkitPanelXRSetup : MonoBehaviour
	{
		private const float PixelsPerUnit = 100f;

		private void Awake()
		{
			Configure();
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
