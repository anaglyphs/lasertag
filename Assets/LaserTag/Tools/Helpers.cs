using UnityEngine;

namespace LaserTag.Tools
{
	public static class Helpers
	{
		private static Collider[] pointCastBuffer = new Collider[10];

		public static bool ColliderAtPoint(out Collider hit, Vector3 position, int layerMask = Physics.DefaultRaycastLayers, QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore)
		{
			hit = null;
			int hits = Physics.OverlapSphereNonAlloc(position, 0.01f, pointCastBuffer, layerMask, triggerInteraction);
			bool didHit = hits > 0;

			if (didHit)
			{
				// choose closest
				float maxDist = Mathf.Infinity;
				Collider closest = pointCastBuffer[0];
				for (int i = 1; i < hits; i++)
				{
					float sqrDist = (pointCastBuffer[i].transform.position - position).sqrMagnitude;
					if (sqrDist < maxDist)
					{
						maxDist = sqrDist;
						closest = pointCastBuffer[i];
					}
				}

				hit = closest;
			}

			return didHit;
		}
	}
}