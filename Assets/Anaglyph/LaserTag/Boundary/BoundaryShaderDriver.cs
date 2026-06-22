using Anaglyph.XRTemplate;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class BoundaryShaderDriver : MonoBehaviour
	{
		[SerializeField] private Material material;
		private int xrOriginID = Shader.PropertyToID("_XROrigin");

		private BoundaryPositioner positioner;

		private void Awake()
		{
			positioner = GetComponent<BoundaryPositioner>();
		}

		private void Update()
		{
			// Align the shader's radial origin to the boundary's center (the last
			// recenter point), falling back to the tracking-space origin.
			Vector3 origin = positioner != null
				? positioner.RecenterWorldPos
				: MainXRRig.TrackingSpace.position;

			material.SetVector(xrOriginID, origin);
		}

		private void OnDisable()
		{
			material.SetVector(xrOriginID, Vector3.zero);
		}
	}
}
