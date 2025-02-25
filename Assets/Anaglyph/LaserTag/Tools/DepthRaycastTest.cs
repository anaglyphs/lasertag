using UnityEngine;
using Anaglyph.XRTemplate;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(100)]
	public class DepthRaycastTest : MonoBehaviour
	{
		[SerializeField] private Transform hitIndicator;

		private void Update()
		{
			Ray ray = new Ray(transform.position, transform.forward);
			var hit = Environment.Raycast(ray, 50f);
			hitIndicator.gameObject.SetActive(hit.didHit);
			hitIndicator.position = hit.point;
		}
	}
}