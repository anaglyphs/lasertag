using UnityEngine;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.DepthKit;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(100)]
	public class DepthRaycastTest : MonoBehaviour
	{
		[SerializeField] private Transform hitIndicator;
		[SerializeField] private LineRendererLaser laser;

		private void Update()
		{
			//bool didHit = DepthCast.Raycast(new Ray(transform.position, transform.forward), out DepthCastResult hit, maxLength: 30f, handRejection: true);

			//if (didHit)
			//{
			//	hitIndicator.position = hit.Position;
			//	hitIndicator.up = hit.Normal;
			//	laser.SetEndPositionForFrame(hit.Position);
			//} else
			//{
			//	hitIndicator.position = transform.position;
			//	hitIndicator.up = transform.up;
			//}

			Ray ray = new Ray(transform.position, transform.forward);
			Vector3 point;
			EnvironmentMapper.Instance.Cast(ray, out point);
			hitIndicator.position = point;
		}
	}
}