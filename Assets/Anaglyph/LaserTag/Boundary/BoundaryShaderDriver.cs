using Anaglyph.XRTemplate;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class BoundaryShaderDriver : MonoBehaviour
	{
		[SerializeField] private Material material;
		private int xrOriginID = Shader.PropertyToID("_XROrigin");

		private void Update()
		{
			material.SetVector(xrOriginID, MainXRRig.TrackingSpace.position);
		}

		private void OnDisable()
		{
			material.SetVector(xrOriginID, Vector3.zero);
		}
	}
}
