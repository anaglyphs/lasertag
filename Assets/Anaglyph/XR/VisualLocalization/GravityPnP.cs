using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// One 2D-to-landmark correspondence: a query pixel and the map landmark it shows. A
	/// landmark is a ray from the camera that first saw it, with an inverse depth along
	/// it: zero is a point at infinity, which still says which way it lies, and the
	/// inverse depth's uncertainty says how far its position can be trusted.
	/// </summary>
	public struct Correspondence
	{
		public float2 pixel;
		/// <summary>The pixel's unit ray in the query camera's Unity axes.</summary>
		public float3 ray;
		/// <summary>The landmark's reference camera center, map frame.</summary>
		public float3 origin;
		/// <summary>Unit direction from <see cref="origin"/> to the landmark, map frame.</summary>
		public float3 direction;
		/// <summary>1 / distance along <see cref="direction"/>; 0 at infinity.</summary>
		public float inverseDepth;
		public float inverseDepthSigma;
		public float confidence;
		/// <summary>The pose-graph node the landmark is anchored to; 0 when unanchored.</summary>
		public ulong node;
		/// <summary>
		/// Names the landmark among the solve's correspondences, so several cameras' sightings of one share
		/// its depth: what they agree on from their different standing points ranges it. 0 for one of its own.
		/// </summary>
		public int landmark;
		/// <summary>Which of the solve's query cameras saw it (an index into the cameras given to <see cref="GravityPnP.Solve(IReadOnlyList{Correspondence}, PinholeCamera, IReadOnlyList{Pose}, GravityPnP.Settings, int)"/>); 0 with one camera.</summary>
		public int camera;

		public float3 Point => origin + direction / inverseDepth;

		/// <summary>Ranged well enough to place: its depth known to within a fraction of itself.</summary>
		public bool IsRanged(float maxRelativeSigma) => inverseDepth > 1e-4f && inverseDepthSigma <= maxRelativeSigma * inverseDepth;

		/// <summary>A landmark at a known point, seen from <paramref name="viewpoint"/> with this depth uncertainty.</summary>
		public static Correspondence ToPoint(float2 pixel, float3 ray, float3 point, float3 viewpoint, float depthSigma)
		{
			float3 d = point - viewpoint;
			float depth = math.length(d);
			return new Correspondence
			{
				pixel = pixel, ray = ray, origin = viewpoint, direction = d / depth,
				inverseDepth = 1f / depth, inverseDepthSigma = depthSigma / (depth * depth), confidence = 1,
			};
		}
	}

	public struct GravityPnPResult
	{
		public bool success;
		/// <summary>Turn about +Y, Unity's sense (quaternion.RotateY), taking query-world directions to map-world.</summary>
		public float yaw;
		/// <summary>p_map = RotateY(yaw) p_query + translation.</summary>
		public float3 translation;
		/// <summary>The first query camera's center in the map frame.</summary>
		public float3 cameraCenter;
		public int inliers;
		/// <summary>Which correspondences the final fit kept, by index into those solved.</summary>
		public int[] inlierIndices;
		/// <summary>Inliers whose landmark is ranged (<see cref="GravityPnP.Settings.IsRanged"/>): these pin translation, the rest only yaw.</summary>
		public int rangedInliers;
		public int correspondences;
		/// <summary>Reprojection RMS over the inliers, query pixels.</summary>
		public float rmsPixels;
		/// <summary>
		/// RMS of the inliers' residuals each in its own sigma (pixel noise plus its
		/// landmark's depth uncertainty): about 1 for a fit as good as the noise allows.
		/// Raw pixels overstate far landmarks' misfit, which their sigma already expects
		/// </summary>
		public float normalizedRms;
		/// <summary>One-sigma uncertainty of the yaw, radians.</summary>
		public float yawSigma;
		/// <summary>One-sigma uncertainty of the camera center, meters (root of the covariance trace).</summary>
		public float translationSigma;
		/// <summary>Covariance over (yaw, camera center x, y, z) in the map frame, widened as the sigmas are; zero when not known.</summary>
		public float4x4 covariance;
		public string failure;

		/// <summary>
		/// One-sigma uncertainty of where the solve puts a map point, meters (root of the
		/// covariance trace): at the camera center, <see cref="translationSigma"/>; near the
		/// landmarks it stood on, less, as the yaw's error there partly undoes the center's.
		/// </summary>
		public float TranslationSigmaAt(float3 point)
		{
			if (covariance.c0.x <= 0) return translationSigma;
			// a point the solve places moves with the center, and turns about it with the yaw
			float3 lever = point - cameraCenter;
			float4 x = new(lever.z, 1, 0, 0), y = new(0, 0, 1, 0), z = new(-lever.x, 0, 0, 1);
			float variance = math.dot(x, math.mul(covariance, x)) + math.dot(y, math.mul(covariance, y)) + math.dot(z, math.mul(covariance, z));
			return math.sqrt(math.max(0, variance));
		}

		public override string ToString() => success
			? $"yaw={math.degrees(yaw):F2}°±{math.degrees(yawSigma):F2} t=({translation.x:F3}, {translation.y:F3}, {translation.z:F3})±{translationSigma * 100:F1}cm inliers={inliers}/{correspondences} (ranged {rangedInliers}) rms={rmsPixels:F2}px"
			: $"failed: {failure} ({inliers}/{correspondences})";
	}

	/// <summary>
	/// Absolute pose of a camera from 2D-landmark correspondences when both frames share
	/// gravity, as Quest tracking frames do: four unknowns, the yaw and the camera
	/// center. RANSAC over a closed-form two-point solver on ranged landmarks (or, with
	/// fewer than two, yaw alone from single far ones), then Gauss-Newton on every
	/// inlier, each weighted by its pixel noise plus how far its landmark's depth
	/// uncertainty moves it in the query image. Far landmarks thereby pin yaw and
	/// near ones translation, and the final Hessian gives the pose's uncertainty, which
	/// is how a caller decides whether the map constrained it enough.
	///
	/// Several query cameras may share the one transform: frames of one stretch of
	/// tracking, their relative poses known from it. Each correspondence names its
	/// camera, and the baseline between the cameras pins translation as one view can't.
	/// </summary>
	public static class GravityPnP
	{
		[Serializable]
		public struct Settings
		{
			/// <summary>Feature localization noise, pixels.</summary>
			public float pixelSigma;
			/// <summary>Normalized residual (in sigmas) under which a correspondence is an inlier.</summary>
			public float inlierSigmas;
			public int iterations;
			/// <summary>Landmarks ranged to within this fraction of their depth seed two-point hypotheses.</summary>
			public float rangedFraction;
			/// <summary>
			/// Weak prior on the camera center sitting where the query world says it is, meters:
			/// keeps translation defined (and its uncertainty honest) when only far landmarks match.
			/// </summary>
			public float centerPriorSigma;
			/// <summary>
			/// Yaw error the map views matched each carry as a whole, degrees: a view's own
			/// placement error, common to every landmark it sighted, which no number of them
			/// averages away. Added to the solver's own uncertainty, over the views matched
			/// (<see cref="AddViewUncertainty"/>).
			/// </summary>
			public float viewYawSigmaDegrees;
			/// <summary>Likewise for position, meters.</summary>
			public float viewTranslationSigma;
			/// <summary>
			/// How many times its stated sigma a landmark's inverse depth is taken to be off by: in
			/// the sim, far landmarks' depths miss by two to four times what their fits claim. The
			/// matched landmarks may also all be off together by that much
			/// (<see cref="AddDepthScaleUncertainty"/>).
			/// </summary>
			public float depthSigmaScale;
			/// <summary>
			/// The least a landmark's inverse depth is taken to be off by, 1/m: its sightings' angular
			/// error over the short walk they span. A fit of many sightings claims less, and is wrong
			/// by this much all the same: tracking moved between them. It is 3 cm at 1.5 m and 6 m at 20.
			/// </summary>
			public float inverseDepthSigmaFloor;

			/// <summary>What a landmark's inverse depth is taken to be off by, given what its fit claims.</summary>
			public readonly float InverseDepthSigma(float claimed) => math.max(depthSigmaScale * claimed, inverseDepthSigmaFloor);

			/// <summary>Ranged well enough to place, by what its inverse depth is taken to be off by: a point past about 17 m never is.</summary>
			public readonly bool IsRanged(in Correspondence c) => c.inverseDepth > 1e-4f && InverseDepthSigma(c.inverseDepthSigma) <= rangedFraction * c.inverseDepth;

			public static Settings Default => new()
			{
				pixelSigma = 1.5f, inlierSigmas = 3f, iterations = 400, rangedFraction = 0.25f, centerPriorSigma = 10f,
				viewYawSigmaDegrees = 0.3f, viewTranslationSigma = 0.03f, depthSigmaScale = 2f, inverseDepthSigmaFloor = 0.015f,
			};
		}

		/// <summary>
		/// Widens a result's translation sigma by a depth error its ranged landmarks share: views
		/// a short walk apart range far points too near or too far together, as a fraction of
		/// depth taken to be what a typical one of them is off by
		/// (<see cref="Settings.InverseDepthSigma"/>). Depths scaled together about where they were ranged from carry the solved
		/// camera along its line to that place, by the same fraction of its distance from it:
		/// nothing for a camera standing where the map's views stood, a meter for one across a
		/// room of far landmarks. The yaw is left as it is: it stands on the landmarks' directions,
		/// which their depths don't move.
		/// </summary>
		public static void AddDepthScaleUncertainty(ref GravityPnPResult result, IReadOnlyList<Correspondence> correspondences, Settings settings)
		{
			if (!result.success || result.inlierIndices == null) return;
			float3 rangedFrom = 0;
			List<float> offBy = new();
			foreach (int i in result.inlierIndices)
			{
				Correspondence c = correspondences[i];
				if (!c.IsRanged(settings.rangedFraction)) continue;
				rangedFrom += c.origin;
				offBy.Add(settings.InverseDepthSigma(c.inverseDepthSigma) / c.inverseDepth);
			}

			if (offBy.Count == 0) return;
			offBy.Sort();
			float3 shift = offBy[offBy.Count / 2] * (result.cameraCenter - rangedFrom / offBy.Count);
			result.translationSigma = math.sqrt(result.translationSigma * result.translationSigma + math.lengthsq(shift));
			if (result.covariance.c0.x > 0)
			{
				result.covariance.c1.y += shift.x * shift.x;
				result.covariance.c2.z += shift.y * shift.y;
				result.covariance.c3.w += shift.z * shift.z;
			}
		}

		/// <summary>
		/// Widens a result's sigmas by the error the <paramref name="views"/> map views
		/// matched share: each view's own placement error moves all of its landmarks
		/// together, so the solver's covariance, which takes them as independent, runs
		/// several times too small against the truth. Independent views average it down.
		/// </summary>
		public static void AddViewUncertainty(ref GravityPnPResult result, Settings settings, int views)
		{
			if (!result.success || views <= 0) return;
			float yaw = math.radians(settings.viewYawSigmaDegrees);
			result.yawSigma = math.sqrt(result.yawSigma * result.yawSigma + yaw * yaw / views);
			result.translationSigma = math.sqrt(result.translationSigma * result.translationSigma +
			                                    settings.viewTranslationSigma * settings.viewTranslationSigma / views);
			if (result.covariance.c0.x > 0)
			{
				// the center's share spread over its three axes, as the trace counts it
				float center = settings.viewTranslationSigma * settings.viewTranslationSigma / views / 3;
				result.covariance.c0.x += yaw * yaw / views;
				result.covariance.c1.y += center;
				result.covariance.c2.z += center;
				result.covariance.c3.w += center;
			}
		}

		/// <summary>
		/// How well a pose explains correspondences, for diagnosis against a known truth: how many it
		/// keeps as inliers, its truncated cost as the solve scores it, and the typical miss in pixels
		/// across the line a landmark's depth moves it along (its direction alone) and along that line
		/// (its depth), the latter in sigmas too.
		/// </summary>
		public static string Describe(IReadOnlyList<Correspondence> correspondences, PinholeCamera intrinsics, IReadOnlyList<Pose> cameras, Settings settings, float yaw, float3 firstCenter, out int inliers)
		{
			Context ctx = new(correspondences, intrinsics, cameras, settings);
			float threshold2 = settings.inlierSigmas * settings.inlierSigmas;
			inliers = 0;
			double cost = 0;
			List<float> across = new(), along = new(), alongSigmas = new(), depths = new();
			for (int i = 0; i < correspondences.Count; i++)
			{
				float e = ctx.Normalized2(i, yaw, firstCenter);
				cost += math.min(e, threshold2);
				if (e < threshold2) inliers++;
				if (!ctx.Residual(i, yaw, firstCenter, out float2 r, out float2 moves, out float depthSigma)) continue;
				float moved = math.length(moves);
				float a = moved > 1e-6f ? math.dot(r, moves) / moved : 0;
				across.Add(math.sqrt(math.max(0, math.lengthsq(r) - a * a)));
				along.Add(math.abs(a));
				alongSigmas.Add(math.abs(a) * math.rsqrt(settings.pixelSigma * settings.pixelSigma + moved * moved * depthSigma * depthSigma));
				if (correspondences[i].inverseDepth > 1e-4f) depths.Add(1f / correspondences[i].inverseDepth);
			}

			static float Middle(List<float> values)
			{
				if (values.Count == 0) return float.NaN;
				values.Sort();
				return values[values.Count / 2];
			}

			return $"inliers {inliers}/{correspondences.Count} cost {cost:F0}, median miss across {Middle(across):F1} px, along {Middle(along):F1} px ({Middle(alongSigmas):F1} sigmas), landmarks {Middle(depths):F1} m from where they were ranged";
		}

		// a fit begun this far in yaw from the pose found that ends this far from it still, at a cost within this
		// share of its own over everything matched, is as good an answer
		private const float RivalStartDegrees = 10f, RivalDegrees = 3f, RivalCostShare = 1.05f;
		private const int RivalStarts = 3;

		/// <param name="cameraRotation">The query camera's rotation in the query world.</param>
		/// <param name="cameraCenter">The query camera's center in the query world.</param>
		public static GravityPnPResult Solve(IReadOnlyList<Correspondence> correspondences, PinholeCamera intrinsics,
			quaternion cameraRotation, float3 cameraCenter, Settings settings, int seed = 1) =>
			Solve(correspondences, intrinsics, new[] { new Pose(cameraCenter, cameraRotation) }, settings, seed);

		/// <param name="cameras">The query cameras in the query world; each correspondence's <see cref="Correspondence.camera"/> indexes them. The result's center and prior are the first's.</param>
		public static GravityPnPResult Solve(IReadOnlyList<Correspondence> correspondences, PinholeCamera intrinsics,
			IReadOnlyList<Pose> cameras, Settings settings, int seed = 1)
		{
			int n = correspondences.Count;
			GravityPnPResult result = new() { correspondences = n };
			if (n < 6)
			{
				result.failure = "too few correspondences";
				return result;
			}

			if (cameras == null || cameras.Count == 0)
			{
				result.failure = "no camera";
				return result;
			}

			Context ctx = new(correspondences, intrinsics, cameras, settings);

			// bearings turned into the query world's axes: the solver only rotates them about Y.
			// A two-point hypothesis needs both rays leaving one center, so the ranged
			// correspondences are also kept by camera
			float3[] bearings = new float3[n];
			List<int> ranged = new();
			List<int>[] rangedByCamera = new List<int>[cameras.Count];
			for (int i = 0; i < n; i++)
			{
				int c = ctx.CameraOf(correspondences[i]);
				bearings[i] = math.mul(ctx.Rotation(c), correspondences[i].ray);
				if (!correspondences[i].IsRanged(settings.rangedFraction)) continue;
				ranged.Add(i);
				(rangedByCamera[c] ??= new List<int>()).Add(i);
			}

			bool twoPoint = false;
			foreach (List<int> same in rangedByCamera)
				if (same != null && same.Count >= 2)
					twoPoint = true;

			Unity.Mathematics.Random random = new((uint)math.max(1, seed));
			float threshold2 = settings.inlierSigmas * settings.inlierSigmas;
			int bestCount = 0;
			float bestCost = float.MaxValue;
			float bestYaw = 0;
			float3 bestCenter = ctx.PriorCenter;
			Span<float> yaws = stackalloc float[2];
			Span<float3> centers = stackalloc float3[2];

			int iterations = twoPoint ? settings.iterations : math.min(settings.iterations, n);
			for (int it = 0; it < iterations; it++)
			{
				int solutions;
				if (twoPoint)
				{
					int a = ranged[random.NextInt(ranged.Count)];
					int c = ctx.CameraOf(correspondences[a]);
					List<int> same = rangedByCamera[c];
					if (same.Count < 2) continue;
					int b;
					do b = same[random.NextInt(same.Count)];
					while (b == a);
					solutions = SolveTwoPoint(bearings[a], correspondences[a].Point, bearings[b], correspondences[b].Point, yaws, centers);
					// the hypothesis places that camera; the first camera sits a known, turned baseline away
					for (int s = 0; s < solutions; s++) centers[s] = ctx.FirstCenter(c, centers[s], yaws[s]);
				}
				else
				{
					// nothing ranged: yaw alone, from one landmark's direction, about where the query says it is
					int a = random.NextInt(n);
					solutions = SolveYaw(bearings[a], correspondences[a].direction, out yaws[0]) ? 1 : 0;
					centers[0] = ctx.PriorCenter;
				}

				for (int s = 0; s < solutions; s++)
				{
					// MSAC: inliers by count, ties by truncated cost
					int count = 0;
					float cost = 0;
					for (int i = 0; i < n; i++)
					{
						float e = ctx.Normalized2(i, yaws[s], centers[s]);
						if (e < threshold2) count++;
						cost += math.min(e, threshold2);
					}

					if (count > bestCount || (count == bestCount && cost < bestCost))
					{
						bestCount = count;
						bestCost = cost;
						bestYaw = yaws[s];
						bestCenter = centers[s];
					}
				}
			}

			result.inliers = bestCount;
			if (bestCount < 6)
			{
				result.failure = "no consensus";
				return result;
			}

			// refine on the inliers, re-select, refine again
			float yaw = bestYaw;
			float3 center = bestCenter;
			List<int> inliers = new(n);
			for (int round = 0; round < 3; round++)
			{
				ctx.Select(inliers, yaw, center, threshold2);
				if (inliers.Count < 6) break;
				ctx.Refine(inliers, ref yaw, ref center);
			}

			ctx.Select(inliers, yaw, center, threshold2);
			result.inliers = inliers.Count;
			result.inlierIndices = inliers.ToArray();
			if (inliers.Count < 6)
			{
				result.failure = "refinement lost the consensus";
				return result;
			}

			double pixels = 0, normalized = 0;
			int rangedInliers = 0;
			foreach (int i in inliers)
			{
				ctx.Residual(i, yaw, center, out float2 r, out _, out _);
				pixels += math.lengthsq(r);
				normalized += ctx.Normalized2(i, yaw, center);
				if (settings.IsRanged(correspondences[i])) rangedInliers++;
			}

			double4x4 covariance = ctx.Covariance(inliers, yaw, center);
			// residuals larger than the assumed noise widen the uncertainty, never narrow it
			double scale = math.max(1.0, normalized / math.max(1, 2 * inliers.Count - 4));
			covariance *= scale;

			// the fit's curvature where it ended says how well the pose is pinned only if the fit is a
			// bowl. From across a room it is a valley: the yaw turns by degrees, the center sliding
			// sideways with it, and the landmarks' directions fit as well. Its length counts as the
			// one-sigma it is, less what the curvature already says along it
			float4 valley = ctx.Valley(inliers, yaw, center, threshold2, (float)scale);
			// and a fit begun well away that settles somewhere else, explaining everything matched about as
			// well, says the pose found is one of several the matches allow
			float4 rival = ctx.Rival(yaw, center, threshold2);
			if (math.abs(rival.x) > math.abs(valley.x)) valley = rival;

			double beyond = 1 - covariance.c0.x / math.max((double)valley.x * valley.x, 1e-12);
			if (beyond > 0)
			{
				double4 v = (double4)valley * math.sqrt(beyond);
				covariance += new double4x4(v * v.x, v * v.y, v * v.z, v * v.w);
			}

			result.success = true;
			result.yaw = yaw;
			result.cameraCenter = center;
			result.translation = center - RotateY(ctx.PriorCenter, yaw);
			result.rangedInliers = rangedInliers;
			result.rmsPixels = (float)math.sqrt(pixels / inliers.Count);
			result.normalizedRms = (float)math.sqrt(normalized / (2 * inliers.Count));
			result.covariance = (float4x4)covariance;
			result.yawSigma = (float)math.sqrt(math.max(0, covariance.c0.x));
			result.translationSigma = (float)math.sqrt(math.max(0, covariance.c1.y + covariance.c2.z + covariance.c3.w));
			return result;
		}

		/// <summary>
		/// Both rays leave the camera center C, turned by the yaw: X1 - X2 = R(λ1 d1 - λ2 d2).
		/// The vertical row is linear in the depths λ, the horizontal length is quadratic,
		/// and the yaw is then the angle between the horizontal vectors.
		/// </summary>
		private static int SolveTwoPoint(float3 d1, float3 x1, float3 d2, float3 x2, Span<float> yaws, Span<float3> centers)
		{
			// solve for whichever depth has the steeper ray to divide by
			if (math.abs(d1.y) > math.abs(d2.y))
			{
				(d1, d2) = (d2, d1);
				(x1, x2) = (x2, x1);
			}

			if (math.abs(d2.y) < 0.02f) return 0;

			float3 delta = x1 - x2;
			// λ2 = a λ1 + b
			float a = d1.y / d2.y, b = -delta.y / d2.y;
			float2 h1 = d1.xz, h2 = d2.xz;
			float2 p = h1 - a * h2, q = -b * h2;
			float target = math.lengthsq(delta.xz);
			float qa = math.dot(p, p), qb = 2 * math.dot(p, q), qc = math.dot(q, q) - target;
			if (qa < 1e-8f) return 0;
			float disc = qb * qb - 4 * qa * qc;
			if (disc < 0) return 0;
			float root = math.sqrt(disc);

			int count = 0;
			for (int sign = -1; sign <= 1; sign += 2)
			{
				float l1 = (-qb + sign * root) / (2 * qa);
				float l2 = a * l1 + b;
				if (l1 <= 0.05f || l2 <= 0.05f) continue;

				// R v = delta in the horizontal plane, R the RotateY matrix [c s; -s c] on (x, z)
				float2 v = l1 * h1 - l2 * h2;
				if (!HorizontalYaw(v, delta.xz, out float yaw)) continue;

				yaws[count] = yaw;
				centers[count] = x1 - l1 * RotateY(d1, yaw);
				count++;
				if (disc < 1e-12f) break;
			}

			return count;
		}

		// the yaw turning a query-world bearing onto a map direction, when their elevations agree
		private static bool SolveYaw(float3 bearing, float3 direction, out float yaw)
		{
			yaw = 0;
			if (math.abs(bearing.y - direction.y) > 0.1f) return false;
			return HorizontalYaw(bearing.xz, direction.xz, out yaw);
		}

		// the RotateY angle taking horizontal vector v (x, z) along t
		private static bool HorizontalYaw(float2 v, float2 t, out float yaw)
		{
			yaw = 0;
			float norm = math.length(v) * math.length(t);
			if (norm < 1e-6f) return false;
			float c = (v.x * t.x + v.y * t.y) / norm;
			float s = (v.y * t.x - v.x * t.y) / norm;
			yaw = math.atan2(s, c);
			return true;
		}

		// the unknowns are the yaw and the first camera's center in the map frame: every
		// other camera sits its query-world baseline from the first, turned by the yaw, so
		// the yaw's lever arm is only that baseline, never the world origin's distance
		private readonly struct Context
		{
			private readonly IReadOnlyList<Correspondence> correspondences;
			private readonly PinholeCamera intrinsics;
			private readonly quaternion[] rotation, inverseCamera;
			private readonly float3[] offset;
			private readonly Settings settings;
			// the correspondences that sight one landmark, every camera's together
			private readonly int[][] landmarks;
			private readonly bool[] taken;
			// one landmark's sightings while it is fitted: at most the largest group's
			private readonly bool[] live;
			private readonly float2[] raw, perDepth, rawAtFit, perDepthAtFit;
			private readonly float[][] rows;

			/// <summary>The first camera's center in the query world: where the query says it is.</summary>
			public readonly float3 PriorCenter;

			public Context(IReadOnlyList<Correspondence> correspondences, PinholeCamera intrinsics, IReadOnlyList<Pose> cameras, Settings settings)
			{
				this.correspondences = correspondences;
				this.intrinsics = intrinsics;
				this.settings = settings;
				int count = cameras.Count;
				rotation = new quaternion[count];
				inverseCamera = new quaternion[count];
				offset = new float3[count];
				PriorCenter = cameras[0].position;
				for (int c = 0; c < count; c++)
				{
					rotation[c] = cameras[c].rotation;
					inverseCamera[c] = math.conjugate(rotation[c]);
					offset[c] = (float3)cameras[c].position - PriorCenter;
				}

				Dictionary<int, List<int>> byLandmark = new();
				List<int[]> groups = new();
				for (int i = 0; i < correspondences.Count; i++)
				{
					int landmark = correspondences[i].landmark;
					if (landmark == 0) groups.Add(new[] { i });
					else if (byLandmark.TryGetValue(landmark, out List<int> sightings)) sightings.Add(i);
					else byLandmark[landmark] = new List<int> { i };
				}

				int most = 1;
				foreach (List<int> sightings in byLandmark.Values)
				{
					groups.Add(sightings.ToArray());
					most = math.max(most, sightings.Count);
				}

				landmarks = groups.ToArray();
				taken = new bool[correspondences.Count];
				live = new bool[most];
				raw = new float2[most];
				perDepth = new float2[most];
				rawAtFit = new float2[most];
				perDepthAtFit = new float2[most];
				rows = new float[5][];
				for (int r = 0; r < rows.Length; r++) rows[r] = new float[2 * most + 1];
			}

			public quaternion Rotation(int camera) => rotation[camera];
			public int CameraOf(Correspondence c) => math.clamp(c.camera, 0, offset.Length - 1);

			/// <summary>Camera c's center in the map frame, from the first camera's.</summary>
			private float3 CenterOf(int camera, float yaw, float3 first) => first + RotateY(offset[camera], yaw);

			/// <summary>The first camera's center in the map frame, from a hypothesis placing camera c.</summary>
			public float3 FirstCenter(int camera, float3 center, float yaw) => center - RotateY(offset[camera], yaw);

			/// <summary>
			/// Projected minus observed pixel. The landmark is taken in homogeneous form,
			/// ρ(origin - C) + direction, which is its direction from C scaled by ρ: exact
			/// for a point, and the bare direction at infinity. <paramref name="perDepth"/> is how
			/// a unit of inverse depth moves the projection, and <paramref name="depthSigma"/> how
			/// far the landmark's inverse depth may be off: it moves the projection along one line
			/// in the image, and across that line the landmark's direction alone decides.
			/// </summary>
			public bool Residual(int i, float yaw, float3 first, out float2 residual, out float2 perDepth, out float depthSigma)
			{
				Correspondence c = correspondences[i];
				residual = perDepth = 0;
				depthSigma = settings.InverseDepthSigma(c.inverseDepthSigma);
				int camera = CameraOf(c);
				float3 center = CenterOf(camera, yaw, first);

				float3 toOrigin = c.origin - center;
				float3 local = math.mul(inverseCamera[camera], RotateY(c.inverseDepth * toOrigin + c.direction, -yaw));
				if (local.z <= 1e-4f * math.length(local)) return false;
				if (!intrinsics.Project(local, out float2 pixel)) return false;

				float3 dl = math.mul(inverseCamera[camera], RotateY(toOrigin, -yaw));
				float iz = 1f / local.z;
				perDepth = new float2(intrinsics.fx * (dl.x * local.z - local.x * dl.z) * iz * iz,
					-intrinsics.fy * (dl.y * local.z - local.y * dl.z) * iz * iz);
				residual = pixel - c.pixel;
				return true;
			}

			/// <summary>One correspondence's misfit in sigmas squared, its landmark's depth free to be off by what it may be: taken alone, whatever else sights the landmark.</summary>
			public float Normalized2(int i, float yaw, float3 first)
			{
				if (!Residual(i, yaw, first, out float2 r, out float2 moves, out float depthSigma)) return float.MaxValue;
				float pixel2 = settings.pixelSigma * settings.pixelSigma, moved2 = math.lengthsq(moves);
				if (moved2 < 1e-12f) return math.lengthsq(r) / pixel2;
				float along = math.dot(r, moves) * math.rsqrt(moved2);
				return (math.lengthsq(r) - along * along) / pixel2 + along * along / (pixel2 + moved2 * depthSigma * depthSigma);
			}

			public void Select(List<int> inliers, float yaw, float3 first, float threshold2)
			{
				inliers.Clear();
				for (int i = 0; i < correspondences.Count; i++)
					if (Normalized2(i, yaw, first) < threshold2)
						inliers.Add(i);
			}

			/// <summary>
			/// One landmark's taken sightings as rows in sigmas: its depth moved to wherever fits them
			/// all best, against how far it may be off. Two rows a sighting, then the depth's own. Seen
			/// from one camera that leaves only the miss across the line depth moves it along; seen
			/// from several, their standing points range it, and what is left pins the pose.
			/// <paramref name="fit"/> decides which sightings count; a later call keeps to those, and
			/// one that falls behind its camera meanwhile stays as it was.
			/// </summary>
			private int Rows(int[] sightings, float yaw, float3 first, bool fit, float[] into)
			{
				double pull = 0, stiffness = 0;
				float depthSigma = 0;
				bool any = false;
				for (int k = 0; k < sightings.Length; k++)
				{
					int i = sightings[k];
					if (fit)
					{
						live[k] = taken[i] && Residual(i, yaw, first, out raw[k], out perDepth[k], out depthSigma);
						rawAtFit[k] = raw[k];
						perDepthAtFit[k] = perDepth[k];
					}
					else if (live[k] && !Residual(i, yaw, first, out raw[k], out perDepth[k], out depthSigma))
					{
						raw[k] = rawAtFit[k];
						perDepth[k] = perDepthAtFit[k];
					}

					if (!live[k]) continue;
					any = true;
					pull += math.dot(perDepth[k], raw[k]);
					stiffness += math.lengthsq(perDepth[k]);
				}

				if (!any) return 0;
				float pixel = settings.pixelSigma;
				if (depthSigma <= 0) depthSigma = settings.InverseDepthSigma(correspondences[sightings[0]].inverseDepthSigma);
				float shift = (float)(-pull / (stiffness + pixel * pixel / (depthSigma * depthSigma)));
				int count = 0;
				for (int k = 0; k < sightings.Length; k++)
				{
					if (!live[k]) continue;
					float2 left = (raw[k] + perDepth[k] * shift) / pixel;
					into[count++] = left.x;
					into[count++] = left.y;
				}

				into[count++] = shift / depthSigma;
				return count;
			}

			private void Take(List<int> inliers, bool take)
			{
				foreach (int i in inliers) taken[i] = take;
			}

			// the rows of every landmark the inliers sight and their numeric Jacobian over (yaw, first center)
			private double4x4 Information(List<int> inliers, float yaw, float3 first, bool robust, out double4 gradient)
			{
				const float yawStep = 1e-4f, centerStep = 1e-4f;
				const double huber = 2;
				double4x4 h = default;
				gradient = default;
				Take(inliers, true);
				foreach (int[] sightings in landmarks)
				{
					int count = Rows(sightings, yaw, first, true, rows[0]);
					if (count == 0) continue;
					Rows(sightings, yaw + yawStep, first, false, rows[1]);
					Rows(sightings, yaw, first + new float3(centerStep, 0, 0), false, rows[2]);
					Rows(sightings, yaw, first + new float3(0, centerStep, 0), false, rows[3]);
					Rows(sightings, yaw, first + new float3(0, 0, centerStep), false, rows[4]);
					// a landmark's rows are weighed together, by its misfit a sighting
					double w = 1;
					if (robust)
					{
						double misfit = 0;
						for (int r = 0; r < count; r++) misfit += rows[0][r] * rows[0][r];
						double norm = math.sqrt(misfit / (count / 2));
						if (norm > huber) w = huber / norm;
					}

					for (int r = 0; r < count; r++)
					{
						double4 j = new((rows[1][r] - rows[0][r]) / yawStep, (rows[2][r] - rows[0][r]) / centerStep,
							(rows[3][r] - rows[0][r]) / centerStep, (rows[4][r] - rows[0][r]) / centerStep);
						h += w * new double4x4(j * j.x, j * j.y, j * j.z, j * j.w);
						gradient += w * j * rows[0][r];
					}
				}

				Take(inliers, false);

				// the weak prior on the center: translation stays defined with only far landmarks
				double prior = 1.0 / (settings.centerPriorSigma * settings.centerPriorSigma);
				double3 offset = (double3)(first - PriorCenter);
				h.c1.y += prior;
				h.c2.z += prior;
				h.c3.w += prior;
				gradient += new double4(0, offset * prior);
				return h;
			}

			public void Refine(List<int> inliers, ref float yaw, ref float3 first)
			{
				for (int iteration = 0; iteration < 10; iteration++)
				{
					double4x4 h = Information(inliers, yaw, first, true, out double4 g);
					h.c0.x += 1e-9 + h.c0.x * 1e-6;
					if (math.determinant(h) == 0) return;
					double4 step = -math.mul(math.inverse(h), g);
					if (math.any(!math.isfinite(step))) return;

					yaw += (float)step.x;
					first += new float3((float)step.y, (float)step.z, (float)step.w);
					if (math.abs(step.x) < 1e-7 && math.length(step.yzw) < 1e-6) return;
				}
			}

			/// <summary>
			/// Another pose that explains the correspondences about as well, as its turn and the center's
			/// shift from the one found; zero when fits begun to either side all come back to it or end worse.
			/// </summary>
			public float4 Rival(float yaw, float3 first, float threshold2)
			{
				List<int> all = new(correspondences.Count), kept = new(correspondences.Count);
				for (int i = 0; i < correspondences.Count; i++) all.Add(i);
				float found = Cost(all, yaw, first, threshold2);
				float4 rival = 0;
				for (int start = -RivalStarts; start <= RivalStarts; start++)
				{
					if (start == 0) continue;
					float otherYaw = yaw + math.radians(start * RivalStartDegrees);
					float3 other = first;
					Select(kept, yaw, first, threshold2);
					RefineCenter(kept, otherYaw, ref other);
					for (int round = 0; round < 3; round++)
					{
						Select(kept, otherYaw, other, threshold2);
						if (kept.Count < 6) break;
						Refine(kept, ref otherYaw, ref other);
					}

					if (kept.Count < 6) continue;
					float turn = math.atan2(math.sin(otherYaw - yaw), math.cos(otherYaw - yaw));
					if (math.abs(turn) < math.radians(RivalDegrees) || math.abs(turn) <= math.abs(rival.x)) continue;
					if (Cost(all, otherYaw, other, threshold2) <= RivalCostShare * found) rival = new float4(turn, other - first);
				}

				return rival;
			}

			// what Information fits, as one number: a landmark's misfit past the bound a sighting counting as the bound
			private float Cost(List<int> inliers, float yaw, float3 first, float threshold2)
			{
				float cost = 0;
				Take(inliers, true);
				foreach (int[] sightings in landmarks)
				{
					// a sighting the pose puts behind its camera misfits as badly as any
					int count = Rows(sightings, yaw, first, true, rows[0]), unseen = -math.max(0, count - 1) / 2;
					foreach (int i in sightings)
						if (taken[i])
							unseen++;
					cost += unseen * threshold2;
					if (count == 0) continue;
					float misfit = 0;
					for (int r = 0; r < count; r++) misfit += rows[0][r] * rows[0][r];
					cost += math.min(misfit, threshold2 * (count / 2));
				}

				Take(inliers, false);
				return cost;
			}

			// the center that fits best with the yaw held where it is
			private void RefineCenter(List<int> inliers, float yaw, ref float3 first)
			{
				for (int iteration = 0; iteration < 5; iteration++)
				{
					double4x4 h = Information(inliers, yaw, first, true, out double4 g);
					double3x3 hc = new(h.c1.yzw, h.c2.yzw, h.c3.yzw);
					if (math.determinant(hc) == 0) return;
					double3 step = -math.mul(math.inverse(hc), g.yzw);
					if (math.any(!math.isfinite(step))) return;
					first += (float3)step;
					if (math.length(step) < 1e-5) return;
				}
			}

			/// <summary>
			/// How far the yaw can be turned, the center following as best it can, before the inliers
			/// fit worse by <paramref name="rise"/> (one sigma's worth, as the residuals run): the turn
			/// in radians and the center's shift with it, whichever way goes further.
			/// </summary>
			public float4 Valley(List<int> inliers, float yaw, float3 first, float threshold2, float rise)
			{
				const int steps = 11;
				float floor = Cost(inliers, yaw, first, threshold2);
				float4 widest = 0;
				for (int sign = -1; sign <= 1; sign += 2)
				{
					float3 center = first;
					float4 reached = 0;
					float turn = math.radians(0.1f), previous = 0;
					for (int k = 0; k < steps; k++, previous = turn, turn *= 1.6f)
					{
						RefineCenter(inliers, yaw + sign * turn, ref center);
						float over = Cost(inliers, yaw + sign * turn, center, threshold2) - floor;
						if (over >= rise)
						{
							// between the last turn that fit and this one, as a bowl would put it
							float at = math.max(previous, turn * math.sqrt(rise / over));
							reached = new float4(sign * at, (center - first) * (at / turn));
							break;
						}

						reached = new float4(sign * turn, center - first);
					}

					if (math.abs(reached.x) > math.abs(widest.x)) widest = reached;
				}

				return widest;
			}

			public double4x4 Covariance(List<int> inliers, float yaw, float3 first)
			{
				double4x4 h = Information(inliers, yaw, first, false, out _);
				h.c0.x += 1e-12;
				return math.determinant(h) == 0 ? new double4x4(1e6) : math.inverse(h);
			}
		}

		/// <summary>quaternion.RotateY(angle) applied to v.</summary>
		public static float3 RotateY(float3 v, float angle)
		{
			math.sincos(angle, out float s, out float c);
			return new float3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
		}
	}
}
