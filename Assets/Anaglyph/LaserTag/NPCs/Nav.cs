using System;
using UnityEngine;

namespace Anaglyph.LaserTag.NPCs
{
	public class Nav : MonoBehaviour
	{
		public Transform target;

		public float radius;
		public float speed;

		private bool didHitSidePrev = false;
		
		private void FixedUpdate()
		{
			Vector3 pos = transform.position;
			Vector3 targetPos = target.position;
			
			bool obstacle = Physics.Linecast(pos, targetPos, out RaycastHit hit);

			if (obstacle)
			{
				Ray ray = new(pos, transform.forward);
				bool hitForw = Physics.Raycast(ray, out RaycastHit obstHit, radius);

				if (hitForw)
				{
					Vector3 tangent = Vector3.Cross(obstHit.normal, transform.up);
					transform.forward = tangent;
				}

				ray = new(pos, -transform.right);
				bool hitSide = Physics.Raycast(ray, out RaycastHit sideHit, radius);
				if (didHitSidePrev && !hitSide)
				{
					transform.forward = -transform.right;
				}

				didHitSidePrev = hitSide;
			}
			else
			{
				transform.forward = targetPos - pos;
			}

			transform.position += transform.forward * speed;
		}
	}
}
