using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
    public class CameraRigSpawner : MonoBehaviour
    {
		[SerializeField] private GameObject xrRig;
		[SerializeField] private GameObject desktopRig;

		private void Awake()
		{
			Instantiate(XRSettings.enabled ? xrRig : desktopRig);
		}
	}
}
