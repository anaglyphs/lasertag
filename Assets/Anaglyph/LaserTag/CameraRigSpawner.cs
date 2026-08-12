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
		
		#if UNITY_EDITOR
		
		#endif

		private void Awake()
		{
			bool usingXR = XRSettings.enabled || xrSimulation;
			GameObject g = Instantiate(usingXR ? xrRig : desktopRig);

#if UNITY_EDITOR

			if (xrSimulation && !XRSettings.enabled)
			{
				Instantiate(arFoundationSimulator);
				g.transform.position = Vector3.zero;
			}
			
			
#endif
		}
	}
}