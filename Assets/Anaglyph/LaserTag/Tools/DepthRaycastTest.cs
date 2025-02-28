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
			hitIndicator.gameObject.SetActive(EnvironmentMapper.Raycast(ray, 50f, out var result));
			hitIndicator.position = result.point; 
		}
	}
}