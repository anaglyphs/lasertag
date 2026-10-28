using System.Collections.Generic;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Unity.Mathematics;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Where this headset has measured its alignment against the map, and what correction it
	/// needed there. A site is a place with good features: a visual revisit of this
	/// headset's own map, or a cross-link against the authority's, found that its tracking
	/// (placed by the colocation alignment) had to move by <see cref="Site.correction"/> to
	/// line up. The map never moves. Each site is a colocation constraint, as an anchor
	/// would be, and the colocator's rigid fit over all of them aligns the rig.
	///
	/// A site stays where it was first measured: a revisit within its radius refines its
	/// correction there rather than moving it, so sites keep their spacing and a player
	/// walking on doesn't drag one along. Drift grows with distance walked, so the older
	/// measurement counts for less the further the head has gone since.
	/// </summary>
	public sealed class ConstraintSites
	{
		public struct Site
		{
			public int id;
			/// <summary>Where it was measured, in the shared frame.</summary>
			public float3 position;
			/// <summary>World → world: where the alignment placed things, onto where the map has them.</summary>
			public Pose4 correction;
			public float sigmaTranslation, sigmaYaw;
			/// <summary>Head travel when last measured.</summary>
			public float path;
			public float time;
			public bool crossLink;
			public int measurements;
			/// <summary>Where the map has the features its latest measurement stood on (ranged inliers), for debug views; may be null.</summary>
			public UnityEngine.Vector3[] features;
			/// <summary>Its measurements as they came, oldest first, before merging: whether revisits after a lap agree (a bent map) or wander with the path (drift).</summary>
			public List<Measurement> history;
		}

		/// <summary>One measurement of a site, unmerged.</summary>
		public struct Measurement
		{
			/// <summary>The camera it was measured from, in the shared frame.</summary>
			public float3 camera;
			public Pose4 correction;
			public float sigmaTranslation, sigmaYaw;
			public float path;
			public float time;
			public bool crossLink;
		}

		/// <summary>Measurements each site keeps in its history: the first and the latest ones.</summary>
		public int HistoryLength = 32;

		/// <summary>A measurement this close to a site updates it, meters.</summary>
		public float SiteRadius = 8f;
		/// <summary>Head travel over which a site's earlier measurement loses most of its weight against a revisit's, meters.</summary>
		public float PathScale = 15f;
		public int Capacity = 16;

		private readonly List<Site> sites = new();
		private int nextId = 1;

		public IReadOnlyList<Site> Sites => sites;
		public int Count => sites.Count;

		public void Clear() => sites.Clear();

		/// <summary>
		/// A measurement at <paramref name="position"/>: merges into the site there (the older
		/// measurement weighted down by how far the head has walked since), or starts a new one.
		/// </summary>
		public Site Add(float3 position, Pose4 correction, float sigmaTranslation, float sigmaYaw, float path, float time, bool crossLink,
			UnityEngine.Vector3[] features = null)
		{
			Measurement measurement = new()
			{
				camera = position, correction = correction, sigmaTranslation = sigmaTranslation, sigmaYaw = sigmaYaw,
				path = path, time = time, crossLink = crossLink,
			};

			int nearest = Nearest(position, out float distance);
			if (nearest >= 0 && distance <= SiteRadius)
			{
				Site s = sites[nearest];
				// the first stays: the baseline every lap is held against
				if (s.history.Count >= HistoryLength) s.history.RemoveAt(1);
				s.history.Add(measurement);
				float stale = math.exp(-math.max(0, path - s.path) / PathScale);
				// the new measurement carried from its camera to the site: its yaw's uncertainty
				// grows into translation over the lever arm
				float lever = distance * sigmaYaw;
				float oldT = stale / Square(s.sigmaTranslation), newT = 1 / Square(math.sqrt(sigmaTranslation * sigmaTranslation + lever * lever));
				float oldY = stale / Square(s.sigmaYaw), newY = 1 / Square(sigmaYaw);

				// averaged about the site, which stays where it is
				float3 at = s.position;
				float3 p = (s.correction.Apply(at) * oldT + correction.Apply(at) * newT) / (oldT + newT);
				float yaw = s.correction.yaw + Pose4.WrapAngle(correction.yaw - s.correction.yaw) * (newY / (oldY + newY));
				s.correction = new Pose4(p - Pose4.RotateY(at, yaw), yaw);
				s.sigmaTranslation = 1 / math.sqrt(oldT + newT);
				s.sigmaYaw = 1 / math.sqrt(oldY + newY);
				s.path = path;
				s.time = time;
				s.crossLink |= crossLink;
				s.measurements++;
				if (features != null) s.features = features;
				sites[nearest] = s;
				return s;
			}

			if (sites.Count >= Capacity)
			{
				// the one walked furthest from: the least it can still say
				int oldest = 0;
				for (int i = 1; i < sites.Count; i++)
					if (sites[i].path < sites[oldest].path) oldest = i;
				sites.RemoveAt(oldest);
			}

			Site site = new()
			{
				id = nextId++, position = position, correction = correction,
				sigmaTranslation = sigmaTranslation, sigmaYaw = sigmaYaw,
				path = path, time = time, crossLink = crossLink, measurements = 1, features = features,
				history = new List<Measurement> { measurement },
			};
			sites.Add(site);
			return site;
		}

		/// <summary>The site nearest a point, or -1; and how far it is.</summary>
		public int Nearest(float3 position, out float distance)
		{
			int best = -1;
			distance = float.PositiveInfinity;
			for (int i = 0; i < sites.Count; i++)
			{
				float d = math.distance(sites[i].position, position);
				if (d >= distance) continue;
				distance = d;
				best = i;
			}

			return best;
		}

		/// <summary>Whether a site a cross-link made lies within <paramref name="radius"/> of a point: the authority's map reaches it.</summary>
		public bool CrossLinkWithin(float3 position, float radius)
		{
			foreach (Site s in sites)
				if (s.crossLink && math.distance(s.position, position) <= radius)
					return true;
			return false;
		}

		/// <summary>Head travel since the most recent measurement of any site: how much drift a new one may correct.</summary>
		public float PathSinceLatest(float path, float fallback)
		{
			float latest = float.NegativeInfinity;
			foreach (Site s in sites) latest = math.max(latest, s.path);
			return float.IsNegativeInfinity(latest) ? fallback : math.max(0, path - latest);
		}

		private static float Square(float x) => math.max(x * x, 1e-8f);
	}
}
