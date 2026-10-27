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
		/// <summary>Inliers whose landmark is ranged: these pin translation, the rest only yaw.</summary>
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
		public string failure;

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

			public static Settings Default => new()
			{
				pixelSigma = 1.5f, inlierSigmas = 3f, iterations = 400, rangedFraction = 0.25f, centerPriorSigma = 10f,
				viewYawSigmaDegrees = 0.3f, viewTranslationSigma = 0.03f,
			};
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
		}

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
				ctx.Residual(i, yaw, center, out float2 r, out _);
				pixels += math.lengthsq(r);
				normalized += ctx.Normalized2(i, yaw, center);
				if (correspondences[i].IsRanged(settings.rangedFraction)) rangedInliers++;
			}

			double4x4 covariance = ctx.Covariance(inliers, yaw, center);
			// residuals larger than the assumed noise widen the uncertainty, never narrow it
			double scale = math.max(1.0, normalized / math.max(1, 2 * inliers.Count - 4));

			result.success = true;
			result.yaw = yaw;
			result.cameraCenter = center;
			result.translation = center - RotateY(ctx.PriorCenter, yaw);
			result.rangedInliers = rangedInliers;
			result.rmsPixels = (float)math.sqrt(pixels / inliers.Count);
			result.normalizedRms = (float)math.sqrt(normalized / (2 * inliers.Count));
			result.yawSigma = (float)math.sqrt(math.max(0, covariance.c0.x * scale));
			result.translationSigma = (float)math.sqrt(math.max(0, (covariance.c1.y + covariance.c2.z + covariance.c3.w) * scale));
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
			/// for a point, and the bare direction at infinity. The variance is the pixel
			/// noise plus the landmark's inverse depth uncertainty carried into the image.
			/// </summary>
			public bool Residual(int i, float yaw, float3 first, out float2 residual, out float variance)
			{
				Correspondence c = correspondences[i];
				residual = 0;
				variance = float.MaxValue;
				int camera = CameraOf(c);
				float3 center = CenterOf(camera, yaw, first);

				float3 toOrigin = c.origin - center;
				float3 local = math.mul(inverseCamera[camera], RotateY(c.inverseDepth * toOrigin + c.direction, -yaw));
				if (local.z <= 1e-4f * math.length(local)) return false;
				if (!intrinsics.Project(local, out float2 pixel)) return false;

				// d pixel / d ρ
				float3 dl = math.mul(inverseCamera[camera], RotateY(toOrigin, -yaw));
				float iz = 1f / local.z;
				float2 dp = new(intrinsics.fx * (dl.x * local.z - local.x * dl.z) * iz * iz,
					-intrinsics.fy * (dl.y * local.z - local.y * dl.z) * iz * iz);

				residual = pixel - c.pixel;
				variance = settings.pixelSigma * settings.pixelSigma + math.lengthsq(dp) * c.inverseDepthSigma * c.inverseDepthSigma;
				return true;
			}

			public float Normalized2(int i, float yaw, float3 first) =>
				Residual(i, yaw, first, out float2 r, out float v) ? math.lengthsq(r) / v : float.MaxValue;

			public void Select(List<int> inliers, float yaw, float3 first, float threshold2)
			{
				inliers.Clear();
				for (int i = 0; i < correspondences.Count; i++)
					if (Normalized2(i, yaw, first) < threshold2)
						inliers.Add(i);
			}

			// normalized residuals and their numeric Jacobian over (yaw, first center)
			private bool Linearize(int i, float yaw, float3 first, out float2 r, out float2 jy, out float2 jx, out float2 jyy, out float2 jz, out float variance)
			{
				const float yawStep = 1e-4f, centerStep = 1e-4f;
				jy = jx = jyy = jz = 0;
				if (!Residual(i, yaw, first, out r, out variance)) return false;
				float w = math.rsqrt(variance);
				r *= w;
				jy = Delta(i, yaw + yawStep, first, r, w) / yawStep;
				jx = Delta(i, yaw, first + new float3(centerStep, 0, 0), r, w) / centerStep;
				jyy = Delta(i, yaw, first + new float3(0, centerStep, 0), r, w) / centerStep;
				jz = Delta(i, yaw, first + new float3(0, 0, centerStep), r, w) / centerStep;
				return true;
			}

			private float2 Delta(int i, float yaw, float3 first, float2 r0, float w) =>
				Residual(i, yaw, first, out float2 r, out _) ? r * w - r0 : 0;

			private double4x4 Information(List<int> inliers, float yaw, float3 first, bool robust, out double4 gradient)
			{
				double4x4 h = default;
				gradient = default;
				const double huber = 2;
				foreach (int i in inliers)
				{
					if (!Linearize(i, yaw, first, out float2 r, out float2 jy, out float2 jx, out float2 jyy, out float2 jz, out _)) continue;
					float norm = math.length(r);
					double w = !robust || norm <= huber ? 1 : huber / norm;
					for (int k = 0; k < 2; k++)
					{
						double4 j = new(jy[k], jx[k], jyy[k], jz[k]);
						h += w * new double4x4(j * j.x, j * j.y, j * j.z, j * j.w);
						gradient += w * j * r[k];
					}
				}

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
