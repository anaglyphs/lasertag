using UnityEngine;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.DepthKit;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(100)]
	public class DepthRaycastTest : MonoBehaviour
	{
		[SerializeField] private Transform hitIndicator;

		private void Update()
		{
			Ray ray = new Ray(transform.position, transform.forward);
			Vector3 point;
			EnvironmentMapper.Instance.Raycast(ray, 50f, out point);
			hitIndicator.position = point;
		}
	}
}