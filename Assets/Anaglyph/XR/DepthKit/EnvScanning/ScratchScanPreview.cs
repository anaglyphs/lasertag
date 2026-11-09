using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// Shows the chunk meshes in <see cref="scratchMaterial"/> while <see cref="EnvScanner"/>
	/// scans into its scratch volume, and puts them back as they were when it stops
	/// </summary>
	public class ScratchScanPreview : MonoBehaviour
	{
		[SerializeField] private Material scratchMaterial;

		private bool previewing;
		private bool chunksVisibleBeforePreview;

		private void Start()
		{
			EnvScanner.Instance.SensorVolumeChanged += OnSensorVolumeChanged;
			OnSensorVolumeChanged();
		}

		private void OnDestroy()
		{
			if (EnvScanner.Instance)
				EnvScanner.Instance.SensorVolumeChanged -= OnSensorVolumeChanged;
			// the renderer data asset keeps the layer masks SetChunksVisible writes
			if (previewing && EnvMesher.Instance)
				EnvMesher.Instance.SetChunksVisible(chunksVisibleBeforePreview);
		}

		private void OnSensorVolumeChanged()
		{
			EnvMesher mesher = EnvMesher.Instance;
			bool scanningScratch = EnvScanner.Instance.ScanIntoScratch;
			if (!mesher || scanningScratch == previewing) return;
			previewing = scanningScratch;

			if (previewing)
			{
				chunksVisibleBeforePreview = mesher.ChunksVisible;
				mesher.ChunkMaterialOverride = scratchMaterial;
				mesher.SetChunksVisible(true);
			}
			else
			{
				mesher.ChunkMaterialOverride = null;
				mesher.SetChunksVisible(chunksVisibleBeforePreview);
			}
		}
	}
}
