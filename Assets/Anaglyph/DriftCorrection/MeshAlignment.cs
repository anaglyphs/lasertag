using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Anaglyph.DriftCorrection
{
	/// <summary>
	/// Mesh-on-mesh alignment: builds a k-d tree over target surface points
	/// and registers source points against it with 4DoF (translation + yaw)
	/// Gauss-Newton point-to-plane ICP. Pitch/roll drift is negligible
	/// because tracking observes gravity. The result maps source points
	/// onto the target surfaces.
	/// </summary>
	public static class MeshAlignment
	{
		public const int StatInlierFrac = 0;
		public const int StatRmsMeters = 1;
		// min eigenvalue of inlier target normals' horizontal covariance.
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

		// median splits keep the tree balanced, so depth stays under
		// log2(n) + 1; this comfortably covers any addressable size
		private const int StackSize = 96;

		public struct Node
		{
			public float3 point;
			public float3 normal;
			public int lesser;
			public int greater;
		}

		[BurstCompile]
		public struct BuildJob : IJob
		{
			// partitioned in place; normals are co-permuted with points
			public NativeArray<float3> points;
			public NativeArray<float3> normals;
			public NativeArray<Node> nodes;

			public void Execute()
			{
				int count = points.Length;
				if (count == 0) return;

				// x: start, y: end, z: depth, w: parent index << 1 | isLesser
				NativeArray<int4> stack = new(StackSize, Allocator.Temp);
				int top = 0;
				stack[top++] = new int4(0, count - 1, 0, -1);

				int numNodes = 0;

				while (top > 0)
				{
					int4 range = stack[--top];
					int start = range.x;
					int end = range.y;
					int depth = range.z;

					int axis = depth % 3;
					int mid = (start + end) / 2;

					// quickselect: only the median needs to land in place,
					// not a full sort of the range
					SelectMedian(start, end, mid, axis);

					int index = numNodes++;

					Node node;
					node.point = points[mid];
					node.normal = normals[mid];
					node.lesser = -1;
					node.greater = -1;
					nodes[index] = node;

					int parentPacked = range.w;
					if (parentPacked != -1)
					{
						int parentIndex = parentPacked >> 1;
						Node parent = nodes[parentIndex];
						if ((parentPacked & 1) == 1)
							parent.lesser = index;
						else
							parent.greater = index;
						nodes[parentIndex] = parent;
					}

					int childDepth = depth + 1;

					if (mid - 1 >= start)
						stack[top++] = new int4(start, mid - 1, childDepth, (index << 1) | 1);

					if (mid + 1 <= end)
						stack[top++] = new int4(mid + 1, end, childDepth, index << 1);
				}
			}

			private void SelectMedian(int lo, int hi, int k, int axis)
			{
				while (lo < hi)
				{
					int j = Partition(lo, hi, axis);

					if (k <= j) hi = j;
					else lo = j + 1;
				}
			}

			// hoare partition: [lo..j] <= pivot <= [j+1..hi], with j < hi
			private int Partition(int lo, int hi, int axis)
			{
				float pivot = points[(lo + hi) / 2][axis];
				int i = lo - 1;
				int j = hi + 1;

				while (true)
				{
					do i++; while (points[i][axis] < pivot);
					do j--; while (points[j][axis] > pivot);

					if (i >= j) return j;

					(points[i], points[j]) = (points[j], points[i]);
					(normals[i], normals[j]) = (normals[j], normals[i]);
				}
			}
		}

		[BurstCompile]
		public struct AlignJob : IJob
		{
			[ReadOnly] public NativeArray<Node> nodes;

			// world-space source points to align onto the tree's surfaces
			[ReadOnly] public NativeArray<float3> points;
			[ReadOnly] public NativeArray<float3> normals;

			public int iterations;
			// correspondence radius anneals from max to final across
			// iterations: wide for capture, tight for the final fit
			public float maxCorrespondenceDist; // meters
			public float finalCorrespondenceDist; // meters
			// normal agreement anneals the opposite way: lenient to strict
			public float minNormalAgreement;
			public float finalNormalAgreement;
			// fraction of matches kept by |residual| each iteration; the
			// rest are outliers (bodies, scan frontiers) that would drag
			// both the solve and the rms stat
			public float trimFraction;

			[WriteOnly] public NativeArray<float4x4> result; // length 1
			[WriteOnly] public NativeArray<float> stats; // length StatCount

			public void Execute()
			{
				result[0] = float4x4.identity;
				for (int i = 0; i < StatCount; i++)
					stats[i] = 0f;

				int numPoints = points.Length;
				if (numPoints == 0 || nodes.Length == 0)
					return;

				NativeArray<int> stackNode = new(StackSize, Allocator.Temp);
				NativeArray<int> stackDepth = new(StackSize, Allocator.Temp);
				NativeArray<float> stackDistSqr = new(StackSize, Allocator.Temp);

				float3 pivot = float3.zero;
				for (int i = 0; i < numPoints; i++)
					pivot += points[i];
				pivot /= numPoints;

				NativeArray<float4> corrJ = new(numPoints, Allocator.Temp);
				NativeArray<float> corrR = new(numPoints, Allocator.Temp);
				NativeArray<float> corrAbsR = new(numPoints, Allocator.Temp);

				float yaw = 0f;
				float3 trans = float3.zero;

				int inliers = 0;
				float sqrSum = 0f;
				float covXX = 0f, covXZ = 0f, covZZ = 0f;
				float vertSum = 0f;

				for (int iter = 0; iter < iterations; iter++)
				{
					float t = iterations > 1 ? iter / (float)(iterations - 1) : 0f;
					float radius = maxCorrespondenceDist *
						math.pow(finalCorrespondenceDist / maxCorrespondenceDist, t);
					float radiusSqr = radius * radius;
					float normalAgreement = math.lerp(minNormalAgreement, finalNormalAgreement, t);

					math.sincos(yaw, out float s, out float c);

					// first pass: gather correspondences
					int matched = 0;

					for (int i = 0; i < numPoints; i++)
					{
						float3 q = RotateY(points[i] - pivot, s, c);
						float3 p = q + pivot + trans;

						int nearest = FindNearest(p, radiusSqr, stackNode, stackDepth, stackDistSqr);
						if (nearest < 0) continue;

						Node target = nodes[nearest];
						float3 n = target.normal;
						float3 srcNormal = RotateY(normals[i], s, c);

						// abs() so mesh winding orientation can't reject everything
						if (math.abs(math.dot(n, srcNormal)) < normalAgreement)
							continue;

						float r = math.dot(n, p - target.point);

						// d(RotY(yaw) v)/dyaw == cross(up, RotY(yaw) v)
						float3 dYaw = math.cross(math.up(), q);

						corrJ[matched] = new float4(n, math.dot(n, dYaw));
						corrR[matched] = r;
						corrAbsR[matched] = math.abs(r);
						matched++;
					}

					if (matched < 16)
						break;

					int keep = math.clamp((int)(matched * trimFraction), 16, matched);
					float trimThreshold = keep < matched
						? SelectKth(corrAbsR, matched, keep - 1)
						: float.MaxValue;

					// second pass: accumulate the kept correspondences.
					// normal equations for [tx, ty, tz, yaw], rows in c0..c3
					float4x4 ata = default;
					float4 atb = float4.zero;

					inliers = 0;
					sqrSum = 0f;
					covXX = 0f; covXZ = 0f; covZZ = 0f;
					vertSum = 0f;

					for (int i = 0; i < matched; i++)
					{
						float r = corrR[i];
						if (math.abs(r) > trimThreshold) continue;

						float4 j = corrJ[i];

						ata.c0 += j * j.x;
						ata.c1 += j * j.y;
						ata.c2 += j * j.z;
						ata.c3 += j * j.w;
						atb += j * r;

						float3 n = j.xyz;

						inliers++;
						sqrSum += r * r;
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

			// iterative nearest-neighbor search. seeding the best distance
			// with the correspondence cap prunes subtrees beyond capture
			// range; returns -1 when nothing is within it
			private int FindNearest(float3 target, float maxDistSqr,
				NativeArray<int> stackNode, NativeArray<int> stackDepth, NativeArray<float> stackDistSqr)
			{
				int best = -1;
				float bestDistSqr = maxDistSqr;

				int top = 0;
				int node = 0;
				int depth = 0;

				while (true)
				{
					while (node != -1)
					{
						Node n = nodes[node];

						float distSqr = math.distancesq(target, n.point);
						if (distSqr < bestDistSqr)
						{
							bestDistSqr = distSqr;
							best = node;
						}

						int axis = depth % 3;
						float diff = target[axis] - n.point[axis];

						int near = diff < 0 ? n.lesser : n.greater;
						int far = diff < 0 ? n.greater : n.lesser;

						if (far != -1)
						{
							stackNode[top] = far;
							stackDepth[top] = depth + 1;
							stackDistSqr[top] = diff * diff;
							top++;
						}

						node = near;
						depth++;
					}

					// pop the nearest pending branch that can still win
					do
					{
						if (top == 0)
							return best;
						top--;
					} while (stackDistSqr[top] >= bestDistSqr);

					node = stackNode[top];
					depth = stackDepth[top];
				}
			}

			// matches Unity.Mathematics RotateY convention
			private static float3 RotateY(float3 p, float s, float c)
			{
				return new float3(c * p.x + s * p.z, p.y, -s * p.x + c * p.z);
			}

			// in-place quickselect: returns the k-th smallest of a[0..count)
			private static float SelectKth(NativeArray<float> a, int count, int k)
			{
				int lo = 0;
				int hi = count - 1;

				while (lo < hi)
				{
					float pivot = a[(lo + hi) / 2];
					int i = lo - 1;
					int j = hi + 1;

					while (true)
					{
						do i++; while (a[i] < pivot);
						do j--; while (a[j] > pivot);

						if (i >= j) break;

						(a[i], a[j]) = (a[j], a[i]);
					}

					if (k <= j) hi = j;
					else lo = j + 1;
				}

				return a[k];
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
