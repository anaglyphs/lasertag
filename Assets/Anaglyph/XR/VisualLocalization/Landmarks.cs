using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// Where keyframes are anchored: a pose graph whose nodes move as drift is
	/// corrected. Keyframes and landmarks are stored relative to a node, so a solve moves
	/// them with it, and how far apart two nodes are in expected drift decides whether
	/// their keyframes may be triangulated together (consecutive views: yes; the same
	/// place after a long walk: no, that is a loop closure).
	/// </summary>
	public interface IKeyframeAnchoring
	{
		/// <summary>
		/// The node a camera at this tracking-space pose belongs to now, and the camera
		/// relative to that node. False when no node is being tracked.
		/// </summary>
		bool TryAnchor(Pose cameraInTrackingSpace, out ulong node, out Pose cameraInNode);

		/// <summary>The node's pose in the shared world, as the graph has it now.</summary>
		bool TryGetNodeWorld(ulong node, out Pose world);

		/// <summary>Expected tracking drift between two nodes, meters; +∞ when unconnected.</summary>
		float Drift(ulong a, ulong b);
	}

	/// <summary>
	/// The node poses a set of landmarks needs, copied on the main thread, so their fits
	/// can run on a worker while the graph goes on changing.
	/// </summary>
	public sealed class NodePoseSnapshot : IKeyframeAnchoring
	{
		private readonly Dictionary<ulong, Pose> worlds = new();

		public NodePoseSnapshot(IKeyframeAnchoring source, IEnumerable<Landmark> landmarks)
		{
			foreach (Landmark l in landmarks)
			{
				Add(source, l.node);
				foreach (Observation o in l.observations) Add(source, o.node);
			}
		}

		private void Add(IKeyframeAnchoring source, ulong node)
		{
			if (!worlds.ContainsKey(node) && source.TryGetNodeWorld(node, out Pose world)) worlds[node] = world;
		}

		public bool TryAnchor(Pose cameraInTrackingSpace, out ulong node, out Pose cameraInNode)
		{
			node = 0;
			cameraInNode = default;
			return false;
		}

		public bool TryGetNodeWorld(ulong node, out Pose world) => worlds.TryGetValue(node, out world);
		public float Drift(ulong a, ulong b) => a == b ? 0 : float.PositiveInfinity;
	}

	/// <summary>One sighting of a landmark: the camera, relative to its node, and the ray it saw it along.</summary>
	public struct Observation
	{
		/// <summary>Null once the keyframe has been evicted; the sighting itself stays valid.</summary>
		public VisualKeyframe keyframe;
		public int feature;
		public ulong node;
		public Pose cameraInNode;
		/// <summary>Unit ray in the camera's Unity axes.</summary>
		public float3 ray;
		/// <summary>The feature's localization noise as an angle, radians.</summary>
		public float angularSigma;
	}

	/// <summary>
	/// A feature tracked across keyframes, parameterized by inverse depth along the ray of
	/// the camera that first saw it: ρ = 0 is a point at infinity, a direction alone, and
	/// σρ says how well triangulation (or the scanned mesh) has ranged it. A building a
	/// block away ends up with ρ ≈ 0 and a σρ that keeps it out of translation while it
	/// still pins yaw; a corner of the room ends up with a tight ρ.
	/// </summary>
	public sealed class Landmark
	{
		public int id;
		public ulong node;
		/// <summary>The reference camera relative to <see cref="node"/>; observations[0] is its sighting.</summary>
		public Pose reference;
		/// <summary>Unit ray from the reference camera, its Unity axes.</summary>
		public float3 bearing;
		public float inverseDepth;
		public float inverseDepthSigma = float.PositiveInfinity;
		/// <summary>The scanned mesh's depth along the reference ray, as an inverse depth prior; σ 0 when the ray hit nothing.</summary>
		public float meshInverseDepth, meshInverseDepthSigma;
		public readonly List<Observation> observations = new();
		/// <summary>Keyframe features pointing at this landmark; it is dropped at zero.</summary>
		public int references;

		public bool HasMesh => meshInverseDepthSigma > 0;

		/// <summary>Seen from at least two keyframes, or placed by the mesh where single-view points are allowed.</summary>
		public bool IsUsable(bool singleViewMesh) => observations.Count >= 2 || (singleViewMesh && HasMesh);

		/// <summary>The reference ray in the world, through the anchoring's current node poses.</summary>
		public bool TryPlace(IKeyframeAnchoring anchoring, out float3 origin, out float3 direction)
		{
			origin = direction = default;
			if (!TryCamera(anchoring, node, reference, out Pose camera)) return false;
			origin = camera.position;
			direction = math.mul(camera.rotation, bearing);
			return true;
		}

		public static bool TryCamera(IKeyframeAnchoring anchoring, ulong node, Pose cameraInNode, out Pose camera)
		{
			camera = default;
			if (anchoring == null || !anchoring.TryGetNodeWorld(node, out Pose world)) return false;
			camera = cameraInNode.GetTransformedBy(world);
			return true;
		}

		/// <summary>
		/// Fits ρ to every sighting (Gauss-Newton on the angular residuals, with the mesh
		/// depth as a prior when there is one) and drops sightings that disagree. Returns
		/// the dropped ones so their keyframes can be unlinked.
		/// </summary>
		public List<Observation> Estimate(IKeyframeAnchoring anchoring, float outlierSigmas = 3f)
		{
			List<Observation> dropped = null;
			if (!TryPlace(anchoring, out float3 origin, out float3 direction)) return null;

			int count = observations.Count;
			Pose[] cameras = new Pose[count];
			bool[] placed = new bool[count];
			for (int k = 1; k < count; k++)
				placed[k] = TryCamera(anchoring, observations[k].node, observations[k].cameraInNode, out cameras[k]);

			for (int round = 0; round < 3; round++)
			{
				// a coarse sweep picks the basin, from the mesh's depth if there is one
				float rho = HasMesh ? meshInverseDepth : 0;
				float best = Cost(rho, origin, direction, cameras, placed);
				for (int s = 0; s < Seeds.Length; s++)
				{
					float cost = Cost(Seeds[s], origin, direction, cameras, placed);
					if (cost >= best) continue;
					best = cost;
					rho = Seeds[s];
				}

				double hessian = 0;
				for (int iteration = 0; iteration < 10; iteration++)
				{
					double g = 0;
					hessian = Prior(rho, ref g);
					float step = 1e-4f + 1e-3f * rho;
					for (int k = 1; k < count; k++)
					{
						if (!placed[k] || !AngularResidual(k, rho, origin, direction, cameras[k], out float2 r)) continue;
						if (!AngularResidual(k, rho + step, origin, direction, cameras[k], out float2 r1)) continue;
						float2 j = (r1 - r) / step;
						hessian += math.dot(j, j);
						g += math.dot(j, r);
					}

					if (hessian <= 0) break;
					float delta = (float)(-g / hessian);
					rho = math.clamp(rho + delta, 0, MaxInverseDepth);
					if (math.abs(delta) < 1e-6f) break;
				}

				inverseDepth = rho;
				inverseDepthSigma = hessian > 0 ? (float)(1 / math.sqrt(hessian)) : float.PositiveInfinity;

				// the worst sighting past the gate goes, and the fit is redone without it
				int worst = -1;
				float worstError = outlierSigmas * outlierSigmas * 2;
				for (int k = 1; k < count; k++)
				{
					float e = placed[k] && AngularResidual(k, rho, origin, direction, cameras[k], out float2 r) ? math.lengthsq(r) : float.MaxValue;
					if (e <= worstError) continue;
					worstError = e;
					worst = k;
				}

				if (worst < 0) break;
				(dropped ??= new()).Add(observations[worst]);
				observations.RemoveAt(worst);
				count--;
				cameras = RemoveAt(cameras, worst);
				placed = RemoveAt(placed, worst);
			}

			return dropped;
		}

		// 1/0.2 m: nearer than the headset can focus a feature from is a bad fit
		private const float MaxInverseDepth = 5f;
		private static readonly float[] Seeds = { 0f, 0.03f, 0.1f, 0.2f, 0.33f, 0.5f, 1f, 2f };
		// weak pull toward 5 m keeps ρ defined from a single sighting; far weaker than any real one
		private const float WeakPrior = 0.2f, WeakPriorSigma = 5f;

		private double Prior(float rho, ref double gradient)
		{
			double h = 1.0 / (WeakPriorSigma * WeakPriorSigma);
			gradient += (rho - WeakPrior) * h;
			if (!HasMesh) return h;
			double m = 1.0 / (meshInverseDepthSigma * meshInverseDepthSigma);
			gradient += (rho - meshInverseDepth) * m;
			return h + m;
		}

		private float Cost(float rho, float3 origin, float3 direction, Pose[] cameras, bool[] placed)
		{
			float cost = 0;
			for (int k = 1; k < cameras.Length; k++)
				cost += placed[k] && AngularResidual(k, rho, origin, direction, cameras[k], out float2 r) ? math.min(math.lengthsq(r), 100) : 100;
			if (HasMesh) cost += math.square((rho - meshInverseDepth) / meshInverseDepthSigma);
			return cost;
		}

		/// <summary>The landmark's predicted direction against sighting k's ray, in its noise sigmas.</summary>
		private bool AngularResidual(int k, float rho, float3 origin, float3 direction, Pose camera, out float2 residual)
		{
			Observation o = observations[k];
			float3 h = math.mul(math.conjugate(camera.rotation), rho * (origin - (float3)camera.position) + direction);
			residual = 0;
			float length = math.length(h);
			if (length < 1e-6f) return false;
			float3 predicted = h / length;
			if (math.dot(predicted, o.ray) <= 0) return false;

			// two axes across the observed ray
			float3 u = math.normalize(math.cross(o.ray, math.abs(o.ray.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0)));
			float3 v = math.cross(o.ray, u);
			residual = new float2(math.dot(predicted, u), math.dot(predicted, v)) / o.angularSigma;
			return true;
		}

		private static T[] RemoveAt<T>(T[] array, int index)
		{
			T[] result = new T[array.Length - 1];
			for (int i = 0, j = 0; i < array.Length; i++)
				if (i != index) result[j++] = array[i];
			return result;
		}
	}
}
