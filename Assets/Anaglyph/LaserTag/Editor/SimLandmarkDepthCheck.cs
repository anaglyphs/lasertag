#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Anaglyph.XR;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// How far triangulated landmarks are from the surfaces they're features of, against the
	/// simulated room's real geometry. Each keyframe's feature rays are cast into the room as
	/// it was when the frame was captured (mesh colliders are added to the room for this),
	/// giving every feature's true depth from its camera; at the end, each ranged landmark's
	/// estimated depth from its reference camera is held against that truth, by distance:
	/// the error, its sign (a bias says something systematic is off, like the camera pose),
	/// and how often it falls within twice the landmark's own claimed sigma.
	/// </summary>
	public class SimLandmarkDepthCheck : MonoBehaviour
	{
		private Transform room;
		private PhysicsScene physics;
		private readonly List<MeshCollider> added = new();
		private readonly HashSet<Collider> roomColliders = new();
		private readonly RaycastHit[] hits = new RaycastHit[16];
		// per keyframe frame record: each feature's true depth, NaN where the ray hit nothing
		private readonly Dictionary<int, float[]> truth = new();

		public void Begin(Transform simulatedRoom)
		{
			room = simulatedRoom;
			physics = room.gameObject.scene.GetPhysicsScene();
			foreach (MeshFilter filter in room.GetComponentsInChildren<MeshFilter>(true))
			{
				if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
				MeshCollider c = filter.gameObject.AddComponent<MeshCollider>();
				c.sharedMesh = filter.sharedMesh;
				added.Add(c);
				roomColliders.Add(c);
			}

			if (VisualLocalizer.Instance != null) VisualLocalizer.Instance.KeyframeAdded += OnKeyframe;
		}

		private void OnDestroy()
		{
			if (VisualLocalizer.Instance != null) VisualLocalizer.Instance.KeyframeAdded -= OnKeyframe;
			foreach (MeshCollider c in added)
				if (c)
					Destroy(c);
		}

		private void OnKeyframe(VisualKeyframe keyframe)
		{
			VisualLocalizer localizer = VisualLocalizer.Instance;
			if (!room || localizer == null || !localizer.Frames.TryGet(keyframe.frame, out VisualFrameRecord record)) return;

			// the camera in the room as it was captured: the room follows the drift frame under
			// the tracking space, which barely moves between capture and now
			Physics.SyncTransforms();
			Transform space = MainXRRig.TrackingSpace;
			Vector3 origin = space.TransformPoint(record.cameraInTrackingSpace.position);
			Quaternion rotation = space.rotation * record.cameraInTrackingSpace.rotation;

			float[] depths = new float[keyframe.features.Count];
			for (int f = 0; f < depths.Length; f++)
			{
				Vector3 direction = rotation * (Vector3)keyframe.intrinsics.Ray(keyframe.features.keypoints[f]);
				depths[f] = NearestRoomHit(origin, direction);
			}

			truth[keyframe.frame] = depths;
		}

		// the room's own surfaces only: the game's colliders (the scanned mesh, players) may share its physics
		private float NearestRoomHit(Vector3 origin, Vector3 direction)
		{
			int n = physics.Raycast(origin, direction, hits, 60f);
			float nearest = float.NaN;
			for (int i = 0; i < n; i++)
				if (roomColliders.Contains(hits[i].collider) && !(hits[i].distance >= nearest))
					nearest = hits[i].distance;
			return nearest;
		}

		private struct Bin
		{
			public List<float> errors, absErrors;
			public int withinTwoSigma, established;
		}

		private static readonly float[] Edges = { 0, 2, 4, 8, 16, 60 };
		// claimed relative inverse-depth sigma, σρ/ρ
		private static readonly float[] RelativeEdges = { 0, 0.05f, 0.1f, 0.25f, 0.5f, 1f, float.PositiveInfinity };

		/// <summary>
		/// How honest each landmark's σρ is, by how well ranged it claims to be: in inverse
		/// depth, where every landmark counts (direction-only too) and its noise is near
		/// Gaussian. The robust scale (1.48 × median |ρ − ρ_true| / σρ) is 1 for an honest σ; above
		/// 1, the landmark claims more than it knows.
		/// </summary>
		public string Calibration()
		{
			VisualLocalizer localizer = VisualLocalizer.Instance;
			if (localizer == null) return "";
			List<float>[] z = new List<float>[RelativeEdges.Length - 1];
			for (int b = 0; b < z.Length; b++) z[b] = new List<float>();
			List<float>[] byViews = { new(), new(), new() }; // 2, 3, 4+ views
			foreach (Landmark l in localizer.Map.Landmarks)
			{
				if (!l.IsUsable(false) || l.observations.Count == 0 || !float.IsFinite(l.inverseDepthSigma) || l.inverseDepthSigma <= 0) continue;
				Observation reference = l.observations[0];
				if (reference.keyframe == null || !truth.TryGetValue(reference.keyframe.frame, out float[] depths) ||
				    reference.feature >= depths.Length || float.IsNaN(depths[reference.feature])) continue;
				float relative = l.inverseDepth > 0 ? l.inverseDepthSigma / l.inverseDepth : float.PositiveInfinity;
				int bin = 0;
				while (bin < z.Length - 1 && relative >= RelativeEdges[bin + 1]) bin++;
				float error = math.abs(l.inverseDepth - 1f / depths[reference.feature]) / l.inverseDepthSigma;
				z[bin].Add(error);
				byViews[math.clamp(l.observations.Count - 2, 0, 2)].Add(error);
			}

			StringBuilder s = new("σρ honesty by claimed σρ/ρ (robust scale, 1 = honest; share within 2σ): ");
			for (int b = 0; b < z.Length; b++)
			{
				int n = z[b].Count;
				if (n == 0) continue;
				int within = 0;
				foreach (float v in z[b]) if (v <= 2) within++;
				s.Append($"[{RelativeEdges[b] * 100:F0}–{(float.IsInfinity(RelativeEdges[b + 1]) ? "∞" : (RelativeEdges[b + 1] * 100).ToString("F0"))}%: {n}, scale {1.4826f * Median(z[b]):F2}, {100f * within / n:F0}% within 2σ] ");
			}

			string[] views = { "2 views", "3 views", "4+ views" };
			s.Append("by views: ");
			for (int v = 0; v < byViews.Length; v++)
			{
				int n = byViews[v].Count;
				if (n == 0) continue;
				int within = 0, wild = 0;
				foreach (float e in byViews[v])
				{
					if (e <= 2) within++;
					if (e > 5) wild++;
				}

				s.Append($"[{views[v]}: {n}, scale {1.4826f * Median(byViews[v]):F2}, {100f * within / n:F0}% within 2σ, {100f * wild / n:F0}% beyond 5σ] ");
			}

			return s.ToString();
		}

		/// <summary>The landmarks against the room, by true distance.</summary>
		public string Summary()
		{
			VisualLocalizer localizer = VisualLocalizer.Instance;
			if (localizer == null) return "landmark depths: no localizer";

			Bin[] bins = new Bin[Edges.Length - 1];
			for (int b = 0; b < bins.Length; b++) bins[b] = new Bin { errors = new(), absErrors = new() };
			int unmeasured = 0, direction = 0;
			foreach (Landmark l in localizer.Map.Landmarks)
			{
				if (!l.IsUsable(false) || l.observations.Count == 0) continue;
				Observation reference = l.observations[0];
				if (reference.keyframe == null || !truth.TryGetValue(reference.keyframe.frame, out float[] depths) ||
				    reference.feature >= depths.Length || float.IsNaN(depths[reference.feature]))
				{
					unmeasured++;
					continue;
				}

				// direction-only landmarks (no range to speak of) say nothing about depth
				if (l.inverseDepth <= 1f / 60f || l.inverseDepthSigma >= l.inverseDepth)
				{
					direction++;
					continue;
				}

				float actual = depths[reference.feature];
				float estimate = 1f / l.inverseDepth;
				float sigma = l.inverseDepthSigma / (l.inverseDepth * l.inverseDepth);
				int bin = 0;
				while (bin < bins.Length - 1 && actual >= Edges[bin + 1]) bin++;
				bins[bin].errors.Add(estimate - actual);
				bins[bin].absErrors.Add(math.abs(estimate - actual));
				if (math.abs(estimate - actual) <= 2 * sigma) bins[bin].withinTwoSigma++;
				if (l.established) bins[bin].established++;
			}

			StringBuilder s = new("landmark depths against the room (estimate − truth, by true distance): ");
			for (int b = 0; b < bins.Length; b++)
			{
				int n = bins[b].errors.Count;
				if (n == 0) continue;
				s.Append($"[{Edges[b]:F0}–{Edges[b + 1]:F0} m: {n}, median |error| {Median(bins[b].absErrors) * 100:F0} cm, median signed {Median(bins[b].errors) * 100:+0;-0} cm, " +
				         $"{100f * bins[b].withinTwoSigma / n:F0}% within 2σ] ");
			}

			s.Append($"({direction} direction-only, {unmeasured} with no ground truth)");
			return s.ToString();
		}

		private static float Median(List<float> values)
		{
			if (values.Count == 0) return float.NaN;
			values.Sort();
			return values[values.Count / 2];
		}
	}
}
#endif
