using Anaglyph.XRTemplate.CameraReader;
using UnityEngine;

namespace EnvisionCenter.XRTemplate.CameraReader
{
    public class CameraDebug : MonoBehaviour
    {
		private Material material;

		private async void Start()
		{
			TryGetComponent(out Renderer renderer);
			material = new(renderer.sharedMaterial);
			renderer.material = material;

			await CameraManager.Instance.Configure(1, 640, 480);
			if (!CameraManager.Instance.IsConfigured)
				Debug.LogError("Could not configure camera");

			await CameraManager.Instance.TryOpenCamera();

			material.mainTexture = CameraManager.Instance.CamTex;
		}
	}
}