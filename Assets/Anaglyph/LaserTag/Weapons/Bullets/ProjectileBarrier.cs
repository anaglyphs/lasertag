using UnityEngine;

namespace Anaglyph.LaserTag.Weapons.Bullets
{
	public class ProjectileBarrier : MonoBehaviour
	{
		public ulong OwnerClientId { get; set; }
		private static readonly RaycastHit[] hits = new RaycastHit[32];

		public static bool Cast(Vector3 origin, Vector3 travel, float radius, ulong shooter, out RaycastHit nearest)
		{
			nearest = default;
			int mask = 1 << Physics.IgnoreRaycastLayer;
			int count = radius > 0
				? Physics.SphereCastNonAlloc(origin, radius, travel.normalized, hits, travel.magnitude, mask, QueryTriggerInteraction.Ignore)
				: Physics.RaycastNonAlloc(origin, travel.normalized, hits, travel.magnitude, mask, QueryTriggerInteraction.Ignore);
			RaycastHit[] results = hits;
			if (count == hits.Length)
			{
				results = radius > 0
					? Physics.SphereCastAll(origin, radius, travel.normalized, travel.magnitude, mask, QueryTriggerInteraction.Ignore)
					: Physics.RaycastAll(origin, travel.normalized, travel.magnitude, mask, QueryTriggerInteraction.Ignore);
				count = results.Length;
			}

			bool found = false;
			for (int i = 0; i < count; i++)
			{
				RaycastHit hit = results[i];
				if (!hit.collider.TryGetComponent(out ProjectileBarrier barrier) || barrier.OwnerClientId == shooter)
					continue;
				if (found && hit.distance >= nearest.distance)
					continue;
				nearest = hit;
				found = true;
			}
			return found;
		}
	}
}
