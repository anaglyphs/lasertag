using UnityEngine;

namespace Anaglyph.XRTemplate
{
    public class XROriginFollower : MonoBehaviour
    {
		private void Update() => UpdateTransform();
		private void LateUpdate() => UpdateTransform();

		private void UpdateTransform()
		{
			var t = MainXROrigin.Transform;
			transform.SetPositionAndRotation(t.position, t.rotation);
		}
	}
}
