using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// A physical point two knots of the track saw: where each placed it in raw tracking, with
	/// how far the two disagree in yaw. The same whatever the graph has done since, so it never
	/// has to be remade.
	/// </summary>
	public struct KeyframeEdge
	{
		/// <summary>The older view's knot.</summary>
		public int knotA;
		public int knotB;
		/// <summary>The point as each view's tracking placed it.</summary>
		public float3 pointA, pointB;
		/// <summary>Yaw from B's placement to A's, radians.</summary>
		public float yaw;
		/// <summary>One over its variance, in the point's position along each axis and in yaw.</summary>
		public float positionInformation, yawInformation;
		/// <summary>Of the rays that made it; below 0 for what ties a track to the graph, which is never given up.</summary>
		public int inliers;
	}

	/// <summary>
	/// Measures one <see cref="KeyframeEdge"/> from two views' stored features: no network and
	/// no image, and nothing of B's but its rays, so a view is measured the moment it is made:
	/// mutual nearest descriptors between A's ranged landmarks and B's features, a RANSAC on
	/// where two rays put the camera, tracking's yaw being nearly right, and Gauss-Newton on B's
	/// rays against A's points. A few milliseconds on a worker.
	/// </summary>
	[BurstCompile(FloatMode = FloatMode.Fast)]
	public struct KeyframeEdgeJob : IJob
	{
		private const int D = VisualLocalization.FeatureSet.DescriptorSize;
		private const int RansacIterations = 600;
		private const float MinCosine = 0.75f, MaxSecondBest = 0.97f;
		private const int MinMatches = 20, MinRefining = 15;
		// tracking's yaw wanders about 1° at most, and past this the matches held are mostly wrong
		private const float MaxYaw = 3f * math.TORADIANS, MaxOffset = 1.6f;
		// the yaw the hypotheses try either way, what two rays may miss each other by, and how parallel they may be
		private const float YawSearch = 2f * math.TORADIANS, MaxRayGap = 0.25f, MinRaySine = 0.05f;
		/// <summary>In <see cref="KeyframeEdge.inliers"/> when a pair yields no edge: where it stopped.</summary>
		public const int FewMatches = -1, NoConsensus = -2, RefinementLost = -3, YawLoose = -4, OutOfBounds = -5;
		private const float ScoreSigmas = 6, CoarseSigmas = 8, FineSigmas = 5;
		private const int Refinements = 8, RecenterAt = 3;

		[ReadOnly] public NativeArray<half> descriptorsA, descriptorsB;
		[ReadOnly] public NativeArray<float3> pointsA, raysB;
		public int countA, countB;
		public float3 centerB;
		/// <summary>A feature's localization noise as an angle, radians.</summary>
		public float angularSigma;
		public int minInliers;
		public float maxYawSigma;
		public uint seed;
		/// <summary>One element, its <see cref="KeyframeEdge.inliers"/> not positive when the views share too little.</summary>
		[WriteOnly] public NativeArray<KeyframeEdge> result;

		public void Execute()
		{
			result[0] = default;
			NativeArray<int> matchA = new(countA, Allocator.Temp), matchB = new(countA, Allocator.Temp);
			int matches = Match(ref matchA, ref matchB);
			if (matches < MinMatches)
			{
				Stop(FewMatches);
				return;
			}

			NativeArray<float3> targets = new(matches, Allocator.Temp), rays = new(matches, Allocator.Temp);
			for (int i = 0; i < matches; i++)
			{
				targets[i] = pointsA[matchA[i]];
				rays[i] = raysB[matchB[i]];
			}

			if (!Ransac(targets, rays, out float yaw, out float3 pivotA, out float3 pivotB))
			{
				Stop(NoConsensus);
				return;
			}

			if (!Refine(targets, rays, ref yaw, ref pivotA, ref pivotB, out int inliers, out double4x4 hessian) || inliers < minInliers)
			{
				Stop(RefinementLost);
				return;
			}

			double yawVariance = math.inverse(hessian).c3.w;
			if (yawVariance <= 0 || math.sqrt(yawVariance) * angularSigma > maxYawSigma) Stop(YawLoose);
			else if (math.abs(yaw) > MaxYaw || math.distance(pivotA, pivotB) > 1.25f * MaxOffset) Stop(OutOfBounds);
			else result[0] = new KeyframeEdge { pointA = pivotA, pointB = pivotB, yaw = yaw, inliers = inliers };
		}

		private void Stop(int why) => result[0] = new KeyframeEdge { inliers = why };

		private int Match(ref NativeArray<int> matchA, ref NativeArray<int> matchB)
		{
			const int W = D / 4;
			NativeArray<float4> rows = new(countB * W, Allocator.Temp), row = new(W, Allocator.Temp);
			for (int j = 0; j < countB; j++)
				for (int c = 0; c < D; c += 4)
					rows[j * W + c / 4] = new float4(descriptorsB[j * D + c], descriptorsB[j * D + c + 1], descriptorsB[j * D + c + 2], descriptorsB[j * D + c + 3]);

			NativeArray<float> bestSimilarityB = new(countB, Allocator.Temp);
			NativeArray<int> bestOfB = new(countB, Allocator.Temp), bestOfA = new(countA, Allocator.Temp);
			for (int j = 0; j < countB; j++) bestSimilarityB[j] = -2;

			for (int i = 0; i < countA; i++)
			{
				for (int c = 0; c < D; c += 4)
					row[c / 4] = new float4(descriptorsA[i * D + c], descriptorsA[i * D + c + 1], descriptorsA[i * D + c + 2], descriptorsA[i * D + c + 3]);

				float best = -2, second = -2;
				int bestJ = -1;
				for (int j = 0; j < countB; j++)
				{
					float4 acc = 0;
					for (int c = 0; c < W; c++) acc += row[c] * rows[j * W + c];
					float dot = math.csum(acc);
					if (dot > best)
					{
						second = best;
						best = dot;
						bestJ = j;
					}
					else if (dot > second) second = dot;

					if (dot > bestSimilarityB[j])
					{
						bestSimilarityB[j] = dot;
						bestOfB[j] = i;
					}
				}

				bestOfA[i] = best >= MinCosine && second < MaxSecondBest * best ? bestJ : -1;
			}

			int matches = 0;
			for (int i = 0; i < countA; i++)
			{
				int j = bestOfA[i];
				if (j < 0 || bestOfB[j] != i) continue;
				matchA[matches] = i;
				matchB[matches] = j;
				matches++;
			}

			return matches;
		}

		// B's rays against A's points decide: two matches propose where the camera was, at a yaw near tracking's
		private bool Ransac(NativeArray<float3> targets, NativeArray<float3> rays,
			out float bestYaw, out float3 bestA, out float3 bestB)
		{
			bestYaw = 0;
			bestA = bestB = default;
			int bestInliers = 0;
			Random random = new(seed | 1);
			float gate = math.cos(ScoreSigmas * angularSigma);
			gate *= gate;
			for (int iteration = 0; iteration < RansacIterations; iteration++)
			{
				int i = random.NextInt(targets.Length), j = random.NextInt(targets.Length);
				if (i == j) continue;
				float yaw = random.NextFloat(-YawSearch, YawSearch);
				math.sincos(yaw, out float s, out float c);
				if (!Intersect(targets[i], Turn(s, c, rays[i]), targets[j], Turn(s, c, rays[j]), out float3 center)) continue;
				if (math.distancesq(center, centerB) > MaxOffset * MaxOffset) continue;

				int inliers = 0;
				for (int k = 0; k < targets.Length; k++)
				{
					float3 to = targets[k] - center;
					float along = math.dot(to, Turn(s, c, rays[k]));
					if (along > 0 && along * along > gate * math.lengthsq(to)) inliers++;
				}

				if (inliers <= bestInliers) continue;
				bestInliers = inliers;
				bestYaw = yaw;
				bestA = targets[i];
				bestB = centerB - Turn(-s, c, center - targets[i]);
			}

			return bestInliers >= minInliers;
		}

		// where a camera sees both points along these rays: behind each, where the lines back from them nearly meet
		private static bool Intersect(float3 pointI, float3 rayI, float3 pointJ, float3 rayJ, out float3 center)
		{
			center = default;
			float cosine = math.dot(rayI, rayJ), sineSquared = 1 - cosine * cosine;
			if (sineSquared < MinRaySine * MinRaySine) return false;
			float3 between = pointI - pointJ;
			float alongI = math.dot(rayI, between), alongJ = math.dot(rayJ, between);
			float depthI = (cosine * alongJ - alongI) / sineSquared, depthJ = (alongJ - cosine * alongI) / sineSquared;
			if (depthI > 0 || depthJ > 0) return false;
			float3 onI = pointI + depthI * rayI, onJ = pointJ + depthJ * rayJ;
			if (math.distancesq(onI, onJ) > MaxRayGap * MaxRayGap) return false;
			center = (onI + onJ) * 0.5f;
			return true;
		}

		// about a pivot in the middle of what agrees, so yaw and position hardly couple there
		private bool Refine(NativeArray<float3> targets, NativeArray<float3> rays, ref float yaw, ref float3 pivotA, ref float3 pivotB,
			out int inliers, out double4x4 hessian)
		{
			inliers = 0;
			hessian = default;
			double y = yaw;
			double3 a = pivotA, b = pivotB;
			NativeArray<float> x = new(targets.Length, Allocator.Temp), h = new(targets.Length, Allocator.Temp), z = new(targets.Length, Allocator.Temp);
			for (int iteration = 0; iteration < Refinements; iteration++)
			{
				double gate = (iteration < RecenterAt ? CoarseSigmas : FineSigmas) * angularSigma;
				gate *= gate;
				math.sincos(y, out double s, out double c);
				double3 turned = Turn(s, c, centerB - b);
				double3 center = turned + a;
				double3 slopeCenter = Slope(turned);

				if (iteration == RecenterAt)
				{
					int n = 0;
					for (int k = 0; k < targets.Length; k++)
					{
						double3 u = math.normalize((double3)targets[k] - center);
						if (math.lengthsq(u - Turn(s, c, (double3)rays[k])) >= gate) continue;
						x[n] = targets[k].x;
						h[n] = targets[k].y;
						z[n] = targets[k].z;
						n++;
					}

					if (n < MinRefining) return false;
					double3 middle = new(Median(x, n), Median(h, n), Median(z, n));
					b += Turn(-s, c, middle - a);
					a = middle;
					turned = Turn(s, c, centerB - b);
					center = turned + a;
					slopeCenter = Slope(turned);
				}

				double3x3 tt = default;
				double3 ty = 0, gt = 0;
				double yy = 0, gy = 0;
				inliers = 0;
				for (int k = 0; k < targets.Length; k++)
				{
					double3 to = (double3)targets[k] - center;
					double distance = math.length(to);
					if (distance < 1e-6) continue;
					double3 u = to / distance;
					double3 ray = Turn(s, c, (double3)rays[k]);
					double3 e = u - ray;
					if (math.lengthsq(e) >= gate) continue;
					inliers++;

					// the ray's error: the direction to the point less the ray. A step of the center
					// moves the direction across itself, by 1/distance
					double3 dy = -Across(slopeCenter, u, distance) - Slope(ray);
					double3 dx = -Across(new double3(1, 0, 0), u, distance), dh = -Across(new double3(0, 1, 0), u, distance), dz = -Across(new double3(0, 0, 1), u, distance);
					tt.c0 += dx * dx.x + dh * dh.x + dz * dz.x;
					tt.c1 += dx * dx.y + dh * dh.y + dz * dz.y;
					tt.c2 += dx * dx.z + dh * dh.z + dz * dz.z;
					ty += new double3(math.dot(dx, dy), math.dot(dh, dy), math.dot(dz, dy));
					yy += math.dot(dy, dy);
					gt += new double3(math.dot(dx, e), math.dot(dh, e), math.dot(dz, e));
					gy += math.dot(dy, e);
				}

				if (inliers < MinRefining) return false;
				hessian = new double4x4(new double4(tt.c0, ty.x), new double4(tt.c1, ty.y), new double4(tt.c2, ty.z), new double4(ty, yy));
				double4 step = -math.mul(math.inverse(hessian), new double4(gt, gy));
				if (!math.all(math.isfinite(step))) return false;
				a += step.xyz;
				y += step.w;
			}

			yaw = (float)y;
			pivotA = (float3)a;
			pivotB = (float3)b;
			return true;
		}

		private static double3 Across(double3 v, double3 u, double distance) => (v - u * math.dot(u, v)) / distance;

		private static float Median(NativeArray<float> values, int count)
		{
			values.GetSubArray(0, count).Sort();
			return values[count / 2];
		}

		// Pose4.RotateY, its sine and cosine at hand
		private static float3 Turn(float s, float c, float3 v) => new(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
		private static double3 Turn(double s, double c, double3 v) => new(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);

		// how a vector turned by Turn moves as the angle grows
		private static double3 Slope(double3 turned) => new(turned.z, 0, -turned.x);
	}
}
