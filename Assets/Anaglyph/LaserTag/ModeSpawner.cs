using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace Anaglyph.LaserTag
{
	[DefaultExecutionOrder(-10000)]
	public class ModeSpawner : MonoBehaviour
	{
		[SerializeField] private bool xrSimulation;

		[FormerlySerializedAs("xrRig")] [SerializeField] private GameObject xrModePrefab;
		[FormerlySerializedAs("desktopRig")] [SerializeField] private GameObject desktopOperatorModePrefab;

		[SerializeField] private GameObject arFoundationSimulator;

		private static bool ShouldSimulateXR
		{
			get
			{
#if UNITY_EDITOR
				// Multiplayer Play Mode virtual players have no headset
				return !Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
#else
				return false;
#endif
			}
		}

		private void Awake()
		{
			bool simulateXR = xrSimulation || ShouldSimulateXR;
			bool usingXR = XRSettings.enabled || simulateXR;
			GameObject g = Instantiate(usingXR ? xrModePrefab : desktopOperatorModePrefab);

#if UNITY_EDITOR

			if (simulateXR && !XRSettings.enabled)
			{
				Instantiate(arFoundationSimulator);
				g.transform.position = Vector3.zero;
			}

#endif
		}
	}
}
