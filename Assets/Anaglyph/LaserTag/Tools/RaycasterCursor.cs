using UnityEngine;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(-100)]
	public class RaycasterCursor : MonoBehaviour
	{
		private Raycaster raycaster;
		private new Renderer renderer;

		private void Awake()
		{
			raycaster = GetComponentInParent<Raycaster>();
			TryGetComponent(out renderer);
		}

		private void LateUpdate()
		{
			
			renderer.enabled = raycaster.DidHit;
			
			if (raycaster.DidHit)
				transform.position = raycaster.Result.point;
		}
	}
}
