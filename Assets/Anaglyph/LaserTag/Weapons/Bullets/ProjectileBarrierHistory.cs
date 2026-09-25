using UnityEngine;

namespace Anaglyph.LaserTag.Weapons.Bullets
{
	public sealed class ProjectileBarrierHistory
	{
		public const float RetentionSeconds = 3f;
		public static ProjectileBarrierHistory Local { get; } = new();
		private readonly Sweep[] sweeps = new Sweep[512];
		private int next, count;

		private struct Sweep
		{
			public ulong owner;
			public Vector3 start, end;
			public float radius, begins, expires;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetLocal() => Local.Clear();

		public void Clear() => next = count = 0;

		public void Record(ulong owner, Vector3 start, Vector3 end, float radius, float begins, float expires)
		{
			sweeps[next] = new Sweep { owner = owner, start = start, end = end, radius = radius, begins = begins, expires = expires };
			next = (next + 1) % sweeps.Length;
			count = Mathf.Min(count + 1, sweeps.Length);
		}

		public bool Blocked(ulong defender, Ray shot, float speed, float shotTime, float hitDistance, float bulletRadius, float now)
		{
			if (speed <= 0 || hitDistance < 0) return false;
			for (int i = 0; i < count; i++)
			{
				Sweep sweep = sweeps[i];
				if (sweep.owner != defender || now - sweep.expires > RetentionSeconds) continue;
				float from = Mathf.Max(0, (sweep.begins - shotTime) * speed);
				float to = Mathf.Min(hitDistance, (sweep.expires - shotTime) * speed);
				if (to < from) continue;
				float radius = sweep.radius + bulletRadius;
				if (SegmentDistanceSquared(shot.GetPoint(from), shot.GetPoint(to), sweep.start, sweep.end) <= radius * radius)
					return true;
			}
			return false;
		}

		private static float SegmentDistanceSquared(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
		{
			Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
			float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
			float s, t;
			const float epsilon = 0.000001f;
			if (a <= epsilon && e <= epsilon) return r.sqrMagnitude;
			if (a <= epsilon)
			{
				s = 0;
				t = Mathf.Clamp01(f / e);
			}
			else
			{
				float c = Vector3.Dot(d1, r);
				if (e <= epsilon) { t = 0; s = Mathf.Clamp01(-c / a); }
				else
				{
					float b = Vector3.Dot(d1, d2), denominator = a * e - b * b;
					s = denominator > epsilon ? Mathf.Clamp01((b * f - c * e) / denominator) : 0;
					t = (b * s + f) / e;
					if (t < 0) { t = 0; s = Mathf.Clamp01(-c / a); }
					else if (t > 1) { t = 1; s = Mathf.Clamp01((b - c) / a); }
				}
			}
			return (p1 + d1 * s - p2 - d2 * t).sqrMagnitude;
		}
	}
}
