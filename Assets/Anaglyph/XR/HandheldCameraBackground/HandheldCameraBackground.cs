using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Anaglyph.XR
{
	/// <summary>
	/// On a phone, draws the camera feed with a copy of the provider's background shader that follows
	/// PassthroughStylingFeature's tint and edge tint. Headsets keep the prefab's own background material.
	/// </summary>
	[RequireComponent(typeof(ARCameraBackground))]
	public class HandheldCameraBackground : MonoBehaviour
	{
		[SerializeField] private Shader arKitStyledShader;
		[SerializeField] private Shader arCoreStyledShader;
		[SerializeField] private Shader simulationStyledShader;

		private Material styledMaterial;

		private void Awake()
		{
			if (!HandheldAR.IsActive) return;

			Shader styledShader = HandheldAR.Runtime switch
			{
				HandheldAR.Provider.ARKit => arKitStyledShader,
				HandheldAR.Provider.ARCore => arCoreStyledShader,
				_ => simulationStyledShader,
			};

			ARCameraBackground background = GetComponent<ARCameraBackground>();
			background.useCustomMaterial = styledShader != null;
			if (styledShader == null) return;

			styledMaterial = new Material(styledShader);
			background.customMaterial = styledMaterial;
			// the styled shaders are the before-opaques variants
			GetComponent<ARCameraManager>().requestedBackgroundRenderingMode = CameraBackgroundRenderingMode.BeforeOpaques;
		}

		private void OnDestroy()
		{
			if (styledMaterial != null) Destroy(styledMaterial);
		}
	}
}
