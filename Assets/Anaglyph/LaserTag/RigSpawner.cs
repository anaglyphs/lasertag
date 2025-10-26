using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
    public class RigSpawner : MonoBehaviour
    {
		[SerializeField] private GameObject xrRig;
		[SerializeField] private GameObject desktopRig;

		private void Awake()
		{
			if(XRSettings.enabled)
				Instantiate(xrRig);
			else
				Instantiate(desktopRig);
		}
	}
}
