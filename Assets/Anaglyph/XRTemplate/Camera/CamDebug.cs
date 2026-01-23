using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.XRTemplate.DeviceCameras
{
	public class CamDebug : MonoBehaviour
	{
		[SerializeField] private int camID = 1;
		private CameraReader camReader;

		public UnityEvent<Texture> onGetTexture = new();

		private void Start()
		{
			CameraReader[] cameras = FindObjectsByType<CameraReader>(FindObjectsSortMode.None);

			foreach (CameraReader reader in cameras)
			{
				if (reader.CamID != camID) continue;

				camReader = reader;
				break;
			}

			camReader.ImageAvailable += OnImageAvailable;
		}

		private void OnImageAvailable(Texture2D texture)
		{
			onGetTexture.Invoke(texture);
		}

		private void OnDestroy()
		{
			if (camReader)
				camReader.ImageAvailable -= OnImageAvailable;
		}
	}
}