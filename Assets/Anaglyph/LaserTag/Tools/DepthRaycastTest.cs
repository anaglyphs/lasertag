using UnityEngine;
using Anaglyph.XRTemplate;

namespace Anaglyph.Lasertag
{
	public class DepthRaycastTest : MonoBehaviour
	{
		[SerializeField] private Transform hitIndicator;
		[SerializeField] private LineRendererLaser laser;

		private void LateUpdate()
		{
			bool didHit = DepthCast.Raycast(new Ray(transform.position, transform.forward), out DepthCastResult hit, maxLength: 10f, handRejection: true);
			laser.enabled = didHit;

			if (didHit)
			{
				hitIndicator.position = hit.Position;
				hitIndicator.up = hit.Normal;
				laser.SetEndPositionForFrame(hit.Position);
			} else
			{
				hitIndicator.position = transform.position;
				hitIndicator.up = transform.up;
			}
		}
	}
}