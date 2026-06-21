using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AnchorVisual : MonoBehaviour
	{
		private ARAnchor anchor;
		private MeshRenderer meshRenderer;

		private Material material;

		private static Color[] trackingStateColors;

		[RuntimeInitializeOnLoadMethod]
		private static void Init()
		{
			trackingStateColors = new Color[3];

			trackingStateColors[(int)TrackingState.None] = Color.red;
			trackingStateColors[(int)TrackingState.Limited] = Color.yellow;
			trackingStateColors[(int)TrackingState.Tracking] = Color.green;
		}

		private void Awake()
		{
			TryGetComponent(out anchor);
			TryGetComponent(out meshRenderer);

			material = new Material(meshRenderer.material);
			meshRenderer.sharedMaterial = material;
		}

		private void Update()
		{
			material.color = trackingStateColors[(int)anchor.trackingState];
		}
	}
}