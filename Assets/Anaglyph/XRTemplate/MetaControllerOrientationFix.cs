using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class MetaControllerOrientationFix : MonoBehaviour
	{
		private void OnEnable()
		{
#if UNITY_ANDROID
			if (OVRPlugin.productName.ToLower().Contains("quest"))
				transform.localRotation = Quaternion.Euler(0, 0, 180);
#endif
		}
	}
}