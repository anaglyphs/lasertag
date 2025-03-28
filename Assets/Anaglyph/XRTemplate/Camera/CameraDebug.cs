using Anaglyph.XRTemplate.CameraReader;
using UnityEngine;

namespace Anaglyph.XRTemplate.CameraReader
{
    public class CameraDebug : MonoBehaviour
    {
		private Material material;

		private void Awake()
		{
			TryGetComponent(out Renderer renderer);
			material = new(renderer.material);
			renderer.material = material;

			CameraManager.OnCaptureStart += OnCaptureStart;
		}

		private void OnCaptureStart()
		{
			material.mainTexture = CameraManager.Instance.CamTex;
		}
	}
}