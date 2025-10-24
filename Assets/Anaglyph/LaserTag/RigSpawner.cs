using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
    public class RigSpawner : MonoBehaviour
    {
		[SerializeField] private GameObject xrRig;

		private void Awake()
		{
			if(XRSettings.enabled)
				Instantiate(xrRig);
		}
	}
}
