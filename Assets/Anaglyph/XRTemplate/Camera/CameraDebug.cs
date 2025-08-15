using UnityEngine;

namespace Anaglyph.XRTemplate.DeviceCameras
{
    public class CameraDebug : MonoBehaviour
    {
		private Material material;
		[SerializeField] private CameraReader reader;

		private async void Start()
		{
			TryGetComponent(out Renderer renderer);
			material = new(renderer.sharedMaterial);
			renderer.material = material;

			await reader.TryOpenCamera();

			material.mainTexture = reader.Texture;
		}
	}
}