using System.Collections.Generic;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Unity.Mathematics;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Where this headset has measured its alignment against the map, and what correction it
	/// needed there. A site is a place with good features: a visual revisit of this
	/// headset's own map, or a cross-link against another headset's views, found that its tracking
	/// (placed by the colocation alignment) had to move by a correction to line up. The map
	/// never moves. Every measurement of every site is a colocation constraint, as an anchor
	/// would be, and the colocator's filter over all of them aligns the rig, weighing each by
	/// the walk since it was measured.
	///
	/// A site is locked where the map had the features it was first measured on, relative to
	/// the track node they hang off, so only the graph can move it: a revisit within its radius
	/// updates what tracking makes of it rather than moving it, so sites keep their spacing and
	/// a player walking on doesn't drag one along. Each site is one constraint, its latest.
	/// </summary>
	public sealed class ConstraintSites
	{
		public struct Site
		{
			public int id;
			/// <summary>Where its first measurement's features were, in the shared frame as it stood then.</summary>
			public float3 position;
			/// <summary>The node it is locked to.</summary>
			public ulong anchor;
			/// <summary>Where it is locked, in that node's frame.</summary>
			public float3 point;
			/// <summary>Head travel when last measured.</summary>
			public float path;
			public float time;
			public bool crossLink;
			public int measurements;
			/// <summary>Where the map has the features its latest measurement stood on (ranged inliers), for debug views; may be null.</summary>
			public UnityEngine.Vector3[] features;
			/// <summary>Its measurements as they came, oldest first: whether revisits after a lap agree (a bent map) or wander with the path (drift).</summary>
			public List<Measurement> history;

			public Measurement Latest => history[^1];
		}

		/// <summary>One measurement of a site.</summary>
		public struct Measurement
		{
			/// <summary>The camera it was measured from, in the shared frame.</summary>
			public float3 camera;
			/// <summary>Where the map has the features it was measured on (their middle), in the shared frame: where it pins the correction best, and its constraint stands.</summary>
			public float3 focus;
			/// <summary>World → world: where the alignment placed things, onto where the map had them.</summary>
			public Pose4 correction;
			/// <summary>Where the alignment placed things, onto the site's node's frame: true whatever the graph does next.</summary>
			public Pose4 fromPlaced;
			public float sigmaTranslation, sigmaYaw;
			public float path;
			public float time;
			public bool crossLink;
			/// <summary>The segment most of the map points it stood on hang from: which pass over the place it matched.</summary>
			public ulong anchor;
		}

		/// <summary>Measurements each site keeps in its history: the first and the latest ones.</summary>
		public int HistoryLength = 32;

		/// <summary>A measurement this close to a site joins it, meters.</summary>
		public float SiteRadius = 8f;
		public int Capacity = 32;
		/// <summary>Where the graph has a node's frame now.</summary>
		public System.Func<ulong, Pose4> WorldOf = _ => Pose4.Identity;

		private readonly List<Site> sites = new();
		private int nextId = 1;

		public IReadOnlyList<Site> Sites => sites;
		public int Count => sites.Count;

		public void Clear() => sites.Clear();

		/// <summary>Where a site is in the shared frame now.</summary>
		public float3 PositionOf(in Site site) => WorldOf(site.anchor).Apply(site.point);

		/// <summary>
		/// A measurement on features at <paramref name="focus"/>, from <paramref name="camera"/>:
		/// joins the site there, or starts a new one.
		/// </summary>
		public Site Add(float3 focus, float3 camera, Pose4 correction, float sigmaTranslation, float sigmaYaw, float path, float time, bool crossLink,
			UnityEngine.Vector3[] features = null, ulong anchor = 0)
		{
			Measurement measurement = new()
			{
				camera = camera, focus = focus, correction = correction, sigmaTranslation = sigmaTranslation, sigmaYaw = sigmaYaw,
				path = path, time = time, crossLink = crossLink, anchor = anchor,
			};

			int nearest = Nearest(focus, out float distance);
			if (nearest >= 0 && distance <= SiteRadius)
			{
				Site s = sites[nearest];
				measurement.fromPlaced = Pose4.Between(WorldOf(s.anchor), correction);
				// the first stays: the baseline every lap is held against
				if (s.history.Count >= HistoryLength) s.history.RemoveAt(1);
				s.history.Add(measurement);
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

			Pose4 anchorWorld = WorldOf(anchor);
			measurement.fromPlaced = Pose4.Between(anchorWorld, correction);
			Site site = new()
			{
				id = nextId++, position = focus, anchor = anchor, point = anchorWorld.ApplyInverse(focus),
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
				float d = math.distance(PositionOf(sites[i]), position);
				if (d >= distance) continue;
				distance = d;
				best = i;
			}

			return best;
		}

		/// <summary>Head travel since the most recent measurement of any site: how much drift a new one may correct.</summary>
		public float PathSinceLatest(float path, float fallback)
		{
			float latest = float.NegativeInfinity;
			foreach (Site s in sites) latest = math.max(latest, s.path);
			return float.IsNegativeInfinity(latest) ? fallback : math.max(0, path - latest);
		}
	}
}
