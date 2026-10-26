using System;
using System.Collections.Generic;
using Unity.Mathematics;

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
		/// <summary>The query camera's center in the map frame.</summary>
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

			public static Settings Default => new()
			{
				pixelSigma = 1.5f, inlierSigmas = 3f, iterations = 400, rangedFraction = 0.25f, centerPriorSigma = 10f,
			};
		}

		/// <param name="cameraRotation">The query camera's rotation in the query world.</param>
		/// <param name="cameraCenter">The query camera's center in the query world.</param>
		public static GravityPnPResult Solve(IReadOnlyList<Correspondence> correspondences, PinholeCamera intrinsics,
			quaternion cameraRotation, float3 cameraCenter, Settings settings, int seed = 1)
		{
			int n = correspondences.Count;
			GravityPnPResult result = new() { correspondences = n };
			if (n < 6)
			{
				result.failure = "too few correspondences";
				return result;
			}

			Context ctx = new(correspondences, intrinsics, cameraRotation, cameraCenter, settings);

			// bearings turned into the query world's axes: the solver only rotates them about Y
			float3[] bearings = new float3[n];
			List<int> ranged = new();
			for (int i = 0; i < n; i++)
			{
				bearings[i] = math.mul(cameraRotation, correspondences[i].ray);
				if (correspondences[i].IsRanged(settings.rangedFraction)) ranged.Add(i);
			}

			Unity.Mathematics.Random random = new((uint)math.max(1, seed));
			float threshold2 = settings.inlierSigmas * settings.inlierSigmas;
			int bestCount = 0;
			float bestCost = float.MaxValue;
			float bestYaw = 0;
			float3 bestCenter = cameraCenter;
			Span<float> yaws = stackalloc float[2];
			Span<float3> centers = stackalloc float3[2];

			bool twoPoint = ranged.Count >= 2;
			int iterations = twoPoint ? settings.iterations : math.min(settings.iterations, n);
			for (int it = 0; it < iterations; it++)
			{
				int solutions;
				if (twoPoint)
				{
					int ia = random.NextInt(ranged.Count), ib = random.NextInt(ranged.Count - 1);
					if (ib >= ia) ib++;
					int a = ranged[ia], b = ranged[ib];
					solutions = SolveTwoPoint(bearings[a], correspondences[a].Point, bearings[b], correspondences[b].Point, yaws, centers);
				}
				else
				{
					// nothing ranged: yaw alone, from one landmark's direction, about where the query says it is
					int a = random.NextInt(n);
					solutions = SolveYaw(bearings[a], correspondences[a].direction, out yaws[0]) ? 1 : 0;
					centers[0] = cameraCenter;
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
			result.translation = center - RotateY(cameraCenter, yaw);
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

		private readonly struct Context
		{
			private readonly IReadOnlyList<Correspondence> correspondences;
			private readonly PinholeCamera intrinsics;
			private readonly quaternion inverseCamera;
			private readonly float3 priorCenter;
			private readonly Settings settings;

			public Context(IReadOnlyList<Correspondence> correspondences, PinholeCamera intrinsics, quaternion cameraRotation, float3 cameraCenter, Settings settings)
			{
				this.correspondences = correspondences;
				this.intrinsics = intrinsics;
				inverseCamera = math.conjugate(cameraRotation);
				priorCenter = cameraCenter;
				this.settings = settings;
			}

			/// <summary>
			/// Projected minus observed pixel. The landmark is taken in homogeneous form,
			/// ρ(origin - C) + direction, which is its direction from C scaled by ρ: exact
			/// for a point, and the bare direction at infinity. The variance is the pixel
			/// noise plus the landmark's inverse depth uncertainty carried into the image.
			/// </summary>
			public bool Residual(int i, float yaw, float3 center, out float2 residual, out float variance)
			{
				Correspondence c = correspondences[i];
				residual = 0;
				variance = float.MaxValue;

				float3 toOrigin = c.origin - center;
				float3 local = math.mul(inverseCamera, RotateY(c.inverseDepth * toOrigin + c.direction, -yaw));
				if (local.z <= 1e-4f * math.length(local)) return false;
				if (!intrinsics.Project(local, out float2 pixel)) return false;

				// d pixel / d ρ
				float3 dl = math.mul(inverseCamera, RotateY(toOrigin, -yaw));
				float iz = 1f / local.z;
				float2 dp = new(intrinsics.fx * (dl.x * local.z - local.x * dl.z) * iz * iz,
					-intrinsics.fy * (dl.y * local.z - local.y * dl.z) * iz * iz);

				residual = pixel - c.pixel;
				variance = settings.pixelSigma * settings.pixelSigma + math.lengthsq(dp) * c.inverseDepthSigma * c.inverseDepthSigma;
				return true;
			}

			public float Normalized2(int i, float yaw, float3 center) =>
				Residual(i, yaw, center, out float2 r, out float v) ? math.lengthsq(r) / v : float.MaxValue;

			public void Select(List<int> inliers, float yaw, float3 center, float threshold2)
			{
				inliers.Clear();
				for (int i = 0; i < correspondences.Count; i++)
					if (Normalized2(i, yaw, center) < threshold2)
						inliers.Add(i);
			}

			// normalized residuals and their numeric Jacobian over (yaw, center)
			private bool Linearize(int i, float yaw, float3 center, out float2 r, out float2 jy, out float2 jx, out float2 jyy, out float2 jz, out float variance)
			{
				const float yawStep = 1e-4f, centerStep = 1e-4f;
				jy = jx = jyy = jz = 0;
				if (!Residual(i, yaw, center, out r, out variance)) return false;
				float w = math.rsqrt(variance);
				r *= w;
				jy = Delta(i, yaw + yawStep, center, r, w) / yawStep;
				jx = Delta(i, yaw, center + new float3(centerStep, 0, 0), r, w) / centerStep;
				jyy = Delta(i, yaw, center + new float3(0, centerStep, 0), r, w) / centerStep;
				jz = Delta(i, yaw, center + new float3(0, 0, centerStep), r, w) / centerStep;
				return true;
			}

			private float2 Delta(int i, float yaw, float3 center, float2 r0, float w) =>
				Residual(i, yaw, center, out float2 r, out _) ? r * w - r0 : 0;

			private double4x4 Information(List<int> inliers, float yaw, float3 center, bool robust, out double4 gradient)
			{
				double4x4 h = default;
				gradient = default;
				const double huber = 2;
				foreach (int i in inliers)
				{
					if (!Linearize(i, yaw, center, out float2 r, out float2 jy, out float2 jx, out float2 jyy, out float2 jz, out _)) continue;
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
				double3 offset = (double3)(center - priorCenter);
				h.c1.y += prior;
				h.c2.z += prior;
				h.c3.w += prior;
				gradient += new double4(0, offset * prior);
				return h;
			}

			public void Refine(List<int> inliers, ref float yaw, ref float3 center)
			{
				for (int iteration = 0; iteration < 10; iteration++)
				{
					double4x4 h = Information(inliers, yaw, center, true, out double4 g);
					h.c0.x += 1e-9 + h.c0.x * 1e-6;
					if (math.determinant(h) == 0) return;
					double4 step = -math.mul(math.inverse(h), g);
					if (math.any(!math.isfinite(step))) return;

					yaw += (float)step.x;
					center += new float3((float)step.y, (float)step.z, (float)step.w);
					if (math.abs(step.x) < 1e-7 && math.length(step.yzw) < 1e-6) return;
				}
			}

			public double4x4 Covariance(List<int> inliers, float yaw, float3 center)
			{
				double4x4 h = Information(inliers, yaw, center, false, out _);
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
