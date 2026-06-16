using System;
using Anaglyph.DepthKit.EnvScanning;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class SettingsMenu : MonoBehaviour
	{
		[SerializeField] private Toggle debugModeToggle;
		[SerializeField] private Toggle showDebugMeshToggle;
		[SerializeField] private Button showDebugMeshForEveryone;
		[SerializeField] private Button hideDebugMeshForEveryone;

		private void Start()
		{
			debugModeToggle.onValueChanged.AddListener(AnaglyphDebug.SetDebugMode);

			showDebugMeshToggle.onValueChanged.AddListener(ChunkManager.Instance.SetChunksVisible);

			showDebugMeshForEveryone.onClick.AddListener(delegate
			{
				EnvChunkSync.Instance?.SetEnvMeshVisibleEveryoneRpc(true);
			});

			hideDebugMeshForEveryone.onClick.AddListener(delegate
			{
				EnvChunkSync.Instance?.SetEnvMeshVisibleEveryoneRpc(false);
			});
		}
	}
}