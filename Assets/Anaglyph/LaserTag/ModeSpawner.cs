using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;

namespace Anaglyph.LaserTag
{
	[DefaultExecutionOrder(-10000)]
	public class ModeSpawner : MonoBehaviour
	{
		[FormerlySerializedAs("xrRig")] [SerializeField] private GameObject xrModePrefab;
		[FormerlySerializedAs("desktopRig")] [SerializeField] private GameObject desktopOperatorModePrefab;

		[SerializeField] private GameObject arFoundationSimulator;

		private static bool ShouldSimulateXR
		{
			get
			{
#if UNITY_EDITOR
				// ParrelSync clones and Multiplayer Play Mode virtual players have no headset
				return Editor.PlayModeXRSimulation.Setting.Value ||
				       !Anaglyph.XR.SimPlayer.IsMainEditor;
#else
				return false;
#endif
			}
		}

		private void Awake()
		{
			bool simulateXR = ShouldSimulateXR;
#if UNITY_EDITOR
			Anaglyph.XR.HandheldAR.SimulatedInEditor = simulateXR && Editor.PlayModePhoneSimulation.Setting.Value;
#endif
			bool handheld = Anaglyph.XR.HandheldAR.IsActive;
			bool usingXR = XRSettings.enabled || simulateXR || handheld;
			if (!usingXR && Application.isMobilePlatform)
				Debug.LogError("[ModeSpawner] no XR runtime started on this phone: ARKit or ARCore is missing from XR Plug-in Management for this platform");
			GameObject g = Instantiate(usingXR ? xrModePrefab : desktopOperatorModePrefab);

			if (handheld)
			{
				// a phone's camera background would otherwise write the sensor's depth and hide everything on a real surface: DepthKit occludes
				foreach (AROcclusionManager occlusion in g.GetComponentsInChildren<AROcclusionManager>(true))
					occlusion.requestedOcclusionPreferenceMode = UnityEngine.XR.ARSubsystems.OcclusionPreferenceMode.NoOcclusion;
				g.AddComponent<CameraMode>();
				g.AddComponent<Interface.HandheldScreenUI>();
				HandheldRendering.Apply();
			}

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
