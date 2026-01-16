using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(-10000)]
	public class CameraRigSpawner : MonoBehaviour
	{
		[SerializeField] private bool xrSimulation;

		[SerializeField] private GameObject xrRig;
		[SerializeField] private GameObject desktopRig;

		[SerializeField] private GameObject arFoundationSimulator;

		private void Awake()
		{
			bool usingXR = XRSettings.enabled || xrSimulation;
			GameObject g = Instantiate(usingXR ? xrRig : desktopRig);

#if UNITY_EDITOR

			if (xrSimulation && !XRSettings.enabled)
			{
				Instantiate(arFoundationSimulator);
				g.transform.position = Vector3.up * 1.7f;
			}
#endif
		}
	}
}