using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.LaserTag
{
	[DefaultExecutionOrder(-10000)]
	public class CameraRigSpawner : MonoBehaviour
	{
		[SerializeField] private bool xrSimulation;

		[SerializeField] private GameObject xrRig;
		[SerializeField] private GameObject desktopRig;

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
			GameObject g = Instantiate(usingXR ? xrRig : desktopRig);

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
