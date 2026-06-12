using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Anaglyph.DriftCorrection
{
	/// <summary>
	/// Aligns sample points (with normals) against TSDF chunk volumes.
	/// Solves 4DoF (translation + yaw) Gauss-Newton point-to-TSDF ICP:
	/// pitch/roll drift is negligible because tracking observes gravity.
	/// The result maps sample points onto the TSDF's surfaces.
	/// </summary>
	public static class TsdfAlignment
	{
		public const int StatInlierFrac = 0;
		public const int StatRmsMeters = 1;
		// min eigenvalue of inlier normals' horizontal covariance.
		// near zero when all surfaces face one horizontal direction,
		// which leaves yaw and tangential translation unconstrained
		public const int StatHorizNormalMinEig = 2;
		// mean |normal.y| of inliers. near zero without floor/ceiling
		// in view, which leaves vertical translation unconstrained
		public const int StatVertNormalFrac = 3;
		public const int StatTransX = 4;
		public const int StatTransY = 5;
		public const int StatTransZ = 6;
		public const int StatYawRad = 7;
		public const int StatCount = 8;

		[BurstCompile]
		public struct AlignJob : IJob
		{
			// TSDF volumes of multiple chunks, concatenated per slot
			[ReadOnly] public NativeArray<sbyte> volumes;
			// world position of each slot's chunk volume corner
			[ReadOnly] public NativeArray<float3> chunkCorners;

			// world-space sample points, each referencing a chunk slot
			[ReadOnly] public NativeArray<float3> points;
			[ReadOnly] public NativeArray<float3> normals;
			[ReadOnly] public NativeArray<int> pointSlots;

			public int voxPerChunkDim;
			public float voxSize;
			public float truncationBand; // meters
			public int iterations;
			// |tsdf| at or above this fraction is clamped or unobserved
			public float maxBandFrac;
			// reject samples whose normal disagrees with the tsdf gradient
			public float minNormalAgreement;

			[WriteOnly] public NativeArray<float4x4> result; // length 1
			[WriteOnly] public NativeArray<float> stats; // length StatCount

			public void Execute()
			{
				result[0] = float4x4.identity;
				for (int i = 0; i < StatCount; i++)
					stats[i] = 0f;

				int numPoints = points.Length;
				if (numPoints == 0)
					return;

				float3 pivot = float3.zero;
				for (int i = 0; i < numPoints; i++)
					pivot += points[i];
				pivot /= numPoints;

				sbyte maxBandRaw = (sbyte)(sbyte.MaxValue * maxBandFrac);

				float yaw = 0f;
				float3 trans = float3.zero;

				int inliers = 0;
				float sqrSum = 0f;
				float covXX = 0f, covXZ = 0f, covZZ = 0f;
				float vertSum = 0f;

				for (int iter = 0; iter < iterations; iter++)
				{
					// normal equations for [tx, ty, tz, yaw], rows in c0..c3
					float4x4 ata = default;
					float4 atb = float4.zero;

					inliers = 0;
					sqrSum = 0f;
					covXX = 0f; covXZ = 0f; covZZ = 0f;
					vertSum = 0f;

					math.sincos(yaw, out float s, out float c);

					for (int i = 0; i < numPoints; i++)
					{
						float3 q = RotateY(points[i] - pivot, s, c);
						float3 p = q + pivot + trans;

						if (!SampleTsdf(p, pointSlots[i], maxBandRaw, out float dist, out float3 grad))
							continue;

						float3 n = RotateY(normals[i], s, c);

						// abs() so mesh winding orientation can't reject everything
						if (math.abs(math.dot(grad, n)) < minNormalAgreement)
							continue;

						// d(RotY(yaw) v)/dyaw == cross(up, RotY(yaw) v)
						float3 dYaw = math.cross(math.up(), q);
						float4 j = new float4(grad, math.dot(grad, dYaw));

						ata.c0 += j * j.x;
						ata.c1 += j * j.y;
						ata.c2 += j * j.z;
						ata.c3 += j * j.w;
						atb += j * dist;

						inliers++;
						sqrSum += dist * dist;
						covXX += n.x * n.x;
						covXZ += n.x * n.z;
						covZZ += n.z * n.z;
						vertSum += math.abs(n.y);
					}

					if (inliers < 16)
						break;

					if (!Solve4x4(ata, -atb, out float4 dx))
						break;

					trans += dx.xyz;
					yaw += dx.w;

					if (math.length(dx.xyz) < 1e-5f && math.abs(dx.w) < 1e-5f)
						break;
				}

				result[0] = math.mul(float4x4.Translate(pivot + trans),
					math.mul(float4x4.RotateY(yaw), float4x4.Translate(-pivot)));

				stats[StatInlierFrac] = inliers / (float)numPoints;
				stats[StatTransX] = trans.x;
				stats[StatTransY] = trans.y;
				stats[StatTransZ] = trans.z;
				stats[StatYawRad] = yaw;

				if (inliers > 0)
				{
					stats[StatRmsMeters] = math.sqrt(sqrSum / inliers);

					float xx = covXX / inliers;
					float xz = covXZ / inliers;
					float zz = covZZ / inliers;
					float half = (xx + zz) * 0.5f;
					float det = xx * zz - xz * xz;
					stats[StatHorizNormalMinEig] = half - math.sqrt(math.max(0f, half * half - det));
					stats[StatVertNormalFrac] = vertSum / inliers;
				}
			}

			// matches Unity.Mathematics RotateY convention
			private static float3 RotateY(float3 p, float s, float c)
			{
				return new float3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z);
			}

			private bool SampleTsdf(float3 worldPos, int slot, sbyte maxBandRaw, out float dist, out float3 grad)
			{
				dist = 0f;
				grad = float3.zero;

				int vpcd = voxPerChunkDim;

				// voxel centers sit at corner + (coord + 0.5) * voxSize
				float3 g = (worldPos - chunkCorners[slot]) / voxSize - 0.5f;
				int3 g0 = (int3)math.floor(g);

				if (math.any(g0 < 0) || math.any(g0 >= vpcd - 1))
					return false;

				int sy = vpcd;
				int sz = vpcd * vpcd;
				int idx = slot * vpcd * sz + g0.x + g0.y * sy + g0.z * sz;

				sbyte r000 = volumes[idx];
				sbyte r100 = volumes[idx + 1];
				sbyte r010 = volumes[idx + sy];
				sbyte r110 = volumes[idx + 1 + sy];
				sbyte r001 = volumes[idx + sz];
				sbyte r101 = volumes[idx + 1 + sz];
				sbyte r011 = volumes[idx + sy + sz];
				sbyte r111 = volumes[idx + 1 + sy + sz];

				// clamped or unobserved voxels have no usable distance or gradient
				if (math.abs(r000) >= maxBandRaw || math.abs(r100) >= maxBandRaw ||
				    math.abs(r010) >= maxBandRaw || math.abs(r110) >= maxBandRaw ||
				    math.abs(r001) >= maxBandRaw || math.abs(r101) >= maxBandRaw ||
				    math.abs(r011) >= maxBandRaw || math.abs(r111) >= maxBandRaw)
					return false;

				const float toNorm = 1f / sbyte.MaxValue;
				float c000 = r000 * toNorm, c100 = r100 * toNorm;
				float c010 = r010 * toNorm, c110 = r110 * toNorm;
				float c001 = r001 * toNorm, c101 = r101 * toNorm;
				float c011 = r011 * toNorm, c111 = r111 * toNorm;

				float3 f = g - g0;

				float c00 = math.lerp(c000, c100, f.x);
				float c10 = math.lerp(c010, c110, f.x);
				float c01 = math.lerp(c001, c101, f.x);
				float c11 = math.lerp(c011, c111, f.x);
				float c0 = math.lerp(c00, c10, f.y);
				float c1 = math.lerp(c01, c11, f.y);
				float val = math.lerp(c0, c1, f.z);

				// analytic gradient of the trilinear interpolant
				float gx = math.lerp(
					math.lerp(c100 - c000, c110 - c010, f.y),
					math.lerp(c101 - c001, c111 - c011, f.y), f.z);
				float gy = math.lerp(
					math.lerp(c010 - c000, c110 - c100, f.x),
					math.lerp(c011 - c001, c111 - c101, f.x), f.z);
				float gz = math.lerp(
					math.lerp(c001 - c000, c101 - c100, f.x),
					math.lerp(c011 - c010, c111 - c110, f.x), f.y);

				float3 gv = new float3(gx, gy, gz);
				float len = math.length(gv);

				if (len < 1e-6f)
					return false;

				grad = gv / len;
				dist = val * truncationBand;
				return true;
			}

			// gaussian elimination with partial pivoting. rows stored in c0..c3
			private static bool Solve4x4(float4x4 a, float4 b, out float4 x)
			{
				x = float4.zero;

				for (int i = 0; i < 4; i++)
				{
					int pivot = i;
					float maxAbs = math.abs(a[i][i]);

					for (int r = i + 1; r < 4; r++)
					{
						float v = math.abs(a[r][i]);
						if (v > maxAbs)
						{
							pivot = r;
							maxAbs = v;
						}
					}

					if (maxAbs < 1e-8f)
						return false;

					if (pivot != i)
					{
						float4 rowTmp = a[i];
						a[i] = a[pivot];
						a[pivot] = rowTmp;

						float bTmp = b[i];
						b[i] = b[pivot];
						b[pivot] = bTmp;
					}

					float inv = 1f / a[i][i];

					for (int r = i + 1; r < 4; r++)
					{
						float factor = a[r][i] * inv;
						a[r] -= a[i] * factor;
						b[r] -= b[i] * factor;
					}
				}

				for (int i = 3; i >= 0; i--)
				{
					float sum = b[i];
					for (int col = i + 1; col < 4; col++)
						sum -= a[i][col] * x[col];
					x[i] = sum / a[i][i];
				}

				return true;
			}
		}
	}
}
