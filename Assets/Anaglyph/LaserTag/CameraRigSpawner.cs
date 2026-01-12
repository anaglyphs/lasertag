using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Anaglyph.Lasertag
{
	public class CameraRigSpawner : MonoBehaviour
	{
		[SerializeField] private GameObject xrRig;
		[SerializeField] private GameObject desktopRig;

		private void Awake()
		{
			bool usingXR = XRSettings.enabled || XRInteractionSimulator.instance.enabled;
			Instantiate(usingXR ? xrRig : desktopRig);
		}
	}
}