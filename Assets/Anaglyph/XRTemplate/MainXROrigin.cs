using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class MainXROrigin : MonoBehaviour
	{
		public static XROrigin Instance;
		public static Transform Transform => Instance.transform;

		private void Awake()
		{
			TryGetComponent(out Instance);
		}
	}
}
