using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(-10000)]
	public class CameraRigSpawner : MonoBehaviour
	{
		[SerializeField] private bool xrSimulationInEditor;

		[SerializeField] private GameObject xrRig;
		[SerializeField] private GameObject desktopRig;

		private void Awake()
		{
			bool usingXR = XRSettings.enabled;
#if UNITY_EDITOR
			usingXR |= xrSimulationInEditor;
#endif
			Instantiate(usingXR ? xrRig : desktopRig);
		}
	}
}