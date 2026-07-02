using Anaglyph.DepthKit.EnvScanning;
using Anaglyph.Netcode;
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

			showDebugMeshToggle.onValueChanged.AddListener(EnvMesher.Instance.SetChunksVisible);

			showDebugMeshForEveryone.onClick.AddListener(delegate
			{
				EnvMeshSync.Instance?.SetEnvMeshVisibleEveryone(true);
			});

			hideDebugMeshForEveryone.onClick.AddListener(delegate
			{
				EnvMeshSync.Instance?.SetEnvMeshVisibleEveryone(false);
			});
		}

		private void OnEnable()
		{
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			OnNetcodeStateChanged(NetcodeManagement.State);
		}

		private void OnDisable()
		{
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			bool c = state == NetcodeState.Connected;
			showDebugMeshForEveryone.interactable = c;
			hideDebugMeshForEveryone.interactable = c;
		}
	}
}