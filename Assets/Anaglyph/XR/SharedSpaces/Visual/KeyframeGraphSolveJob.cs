using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>A place along the track: raw tracking near here maps into the map's frame through one (translation, yaw).</summary>
	public struct KeyframeGraphKnot
	{
		/// <summary>Where the head was, in raw tracking: the point consecutive knots are held together at.</summary>
		public float3 origin;
		public float path;
		/// <summary>The knot before it on the same alignment, or -1.</summary>
		public int previous;
		/// <summary>The knot after it on the same alignment, or -1.</summary>
		public int next;
		public bool isFixed;
	}

	/// <summary>How much one edge says, along each way it can be wrong: a solve's working copy of what its inliers and its line of sight imply.</summary>
	public struct KeyframeEdgeWeight
	{
		/// <summary>Unit, level, in the map: the way the edge's point lies from the camera.</summary>
		public float3 sight;
		/// <summary>One over its variance along the sight, across it, in height and in yaw.</summary>
		public float along, across, height, yaw;
	}

	/// <summary>
	/// One Gauss-Newton step of the graph: over each free knot's transform from raw tracking to
	/// the map, with tracking drifting a little between consecutive knots, and every
	/// <see cref="KeyframeEdge"/> saying two knots should agree on a point. The system is
	/// assembled over a skyline, ordered breadth first through the graph so laps of a place sit
	/// near each other in it, and factored in place: a few milliseconds for a few hundred knots.
	/// </summary>
	[BurstCompile(FloatMode = FloatMode.Fast)]
	public struct KeyframeGraphSolveJob : IJob
	{
		private const double Damping = 1e-6;

		[ReadOnly] public NativeArray<KeyframeGraphKnot> knots;
		[ReadOnly] public NativeArray<KeyframeEdge> edges;
		/// <summary>Parallel to <see cref="edges"/>.</summary>
		[ReadOnly] public NativeArray<KeyframeEdgeWeight> weights;
		/// <summary>Each knot's (translation, yaw): in, where to linearize; out, one step on.</summary>
		public NativeArray<double4> state;
		/// <summary>One element: the largest step taken.</summary>
		[WriteOnly] public NativeArray<double> largestStep;
		public double positionRateSquared, yawRateSquared, positionFloorSquared, yawFloorSquared;
		public double huber;

		public void Execute()
		{
			largestStep[0] = 0;
			int knotCount = knots.Length;
			NativeArray<int> position = new(knotCount, Allocator.Temp);
			int free = Order(ref position);
			if (free == 0) return;

			// the skyline: each row reaches back to its earliest neighbor
			NativeArray<int> first = new(free, Allocator.Temp);
			for (int k = 0; k < knotCount; k++)
				if (position[k] >= 0)
					first[position[k]] = position[k];
			for (int k = 0; k < knotCount; k++)
				if (knots[k].previous >= 0)
					Reach(ref first, position, k, knots[k].previous);
			for (int e = 0; e < edges.Length; e++)
				Reach(ref first, position, edges[e].knotA, edges[e].knotB);

			int size = free * 4;
			NativeArray<int> rowStart = new(size + 1, Allocator.Temp);
			for (int i = 0, offset = 0; i <= size; i++)
			{
				rowStart[i] = offset;
				if (i < size) offset += i - first[i / 4] * 4 + 1;
			}

			NativeArray<double> matrix = new(rowStart[size], Allocator.Temp);
			NativeArray<double> gradient = new(size, Allocator.Temp);

			for (int k = 0; k < knotCount; k++)
			{
				int previous = knots[k].previous;
				if (previous < 0) continue;
				double3 at = knots[previous].origin;
				double walked = math.max(knots[k].path - knots[previous].path, 0);
				double position2 = 1 / (positionRateSquared * walked + positionFloorSquared);
				double4x4 information = default;
				information.c0.x = information.c1.y = information.c2.z = position2;
				information.c3.w = 1 / (yawRateSquared * walked + yawFloorSquared);
				Add(ref matrix, ref gradient, rowStart, first, position, previous, k, at, at, 0, information);
			}

			for (int e = 0; e < edges.Length; e++)
			{
				KeyframeEdge edge = edges[e];
				KeyframeEdgeWeight weight = weights[e];
				double3 sight = weight.sight;
				double3 side = new(sight.z, 0, -sight.x);
				double4x4 information = default;
				information.c0.xyz = (double)weight.along * sight * sight.x + (double)weight.across * side * side.x;
				information.c1.y = weight.height;
				information.c2.xyz = (double)weight.along * sight * sight.z + (double)weight.across * side * side.z;
				information.c3.w = weight.yaw;
				Add(ref matrix, ref gradient, rowStart, first, position, edge.knotA, edge.knotB, edge.pointA, edge.pointB, edge.yaw, information, huber);
			}

			for (int i = 0; i < size; i++) matrix[rowStart[i + 1] - 1] += Damping;

			Factor(ref matrix, rowStart, first, size);
			Substitute(matrix, rowStart, first, ref gradient, size);

			double largest = 0;
			for (int k = 0; k < knotCount; k++)
			{
				if (position[k] < 0) continue;
				int p = position[k] * 4;
				double4 step = -new double4(gradient[p], gradient[p + 1], gradient[p + 2], gradient[p + 3]);
				if (!math.all(math.isfinite(step))) return;
				state[k] += step;
				largest = math.max(largest, math.cmax(math.abs(step)));
			}

			largestStep[0] = largest;
		}

		// reverse breadth first from a far end: position[knot] among the free knots, -1 for fixed
		private int Order(ref NativeArray<int> position)
		{
			int knotCount = knots.Length;
			NativeArray<int> degree = new(knotCount + 1, Allocator.Temp);
			for (int k = 0; k < knotCount; k++)
			{
				int previous = knots[k].previous;
				if (previous < 0 || knots[k].isFixed || knots[previous].isFixed) continue;
				degree[k]++;
				degree[previous]++;
			}

			for (int e = 0; e < edges.Length; e++)
			{
				int a = edges[e].knotA, b = edges[e].knotB;
				if (a == b || knots[a].isFixed || knots[b].isFixed) continue;
				degree[a]++;
				degree[b]++;
			}

			// neighbors, in one array
			NativeArray<int> start = new(knotCount + 1, Allocator.Temp);
			for (int k = 0; k < knotCount; k++) start[k + 1] = start[k] + degree[k];
			NativeArray<int> neighbors = new(start[knotCount], Allocator.Temp);
			NativeArray<int> fill = new(knotCount, Allocator.Temp);
			for (int k = 0; k < knotCount; k++)
			{
				int previous = knots[k].previous;
				if (previous >= 0 && !knots[k].isFixed && !knots[previous].isFixed) Link(ref neighbors, ref fill, start, k, previous);
			}

			for (int e = 0; e < edges.Length; e++)
			{
				int a = edges[e].knotA, b = edges[e].knotB;
				if (a != b && !knots[a].isFixed && !knots[b].isFixed) Link(ref neighbors, ref fill, start, a, b);
			}

			NativeArray<int> queue = new(knotCount, Allocator.Temp);
			NativeArray<byte> visited = new(knotCount, Allocator.Temp);
			int ordered = 0;
			for (int seed = 0; seed < knotCount; seed++)
			{
				if (knots[seed].isFixed || visited[seed] != 0) continue;
				// one sweep finds the far end of this piece, and a second orders it from there
				int start0 = ordered;
				int far = Sweep(seed, start0, ref queue, ref visited, start, neighbors, out int end);
				for (int i = start0; i < end; i++) visited[queue[i]] = 0;
				Sweep(far, start0, ref queue, ref visited, start, neighbors, out ordered);
			}

			for (int k = 0; k < knotCount; k++) position[k] = -1;
			for (int i = 0; i < ordered; i++) position[queue[i]] = ordered - 1 - i;
			return ordered;
		}

		private static void Link(ref NativeArray<int> neighbors, ref NativeArray<int> fill, NativeArray<int> start, int a, int b)
		{
			neighbors[start[a] + fill[a]] = b;
			fill[a]++;
			neighbors[start[b] + fill[b]] = a;
			fill[b]++;
		}

		private static int Sweep(int from, int head, ref NativeArray<int> queue, ref NativeArray<byte> visited, NativeArray<int> start, NativeArray<int> neighbors, out int tail)
		{
			tail = head;
			queue[tail++] = from;
			visited[from] = 1;
			int last = from;
			while (head < tail)
			{
				last = queue[head++];
				for (int n = start[last]; n < start[last + 1]; n++)
				{
					int other = neighbors[n];
					if (visited[other] != 0) continue;
					visited[other] = 1;
					queue[tail++] = other;
				}
			}

			return last;
		}

		private static void Reach(ref NativeArray<int> first, NativeArray<int> position, int a, int b)
		{
			int pa = position[a], pb = position[b];
			if (pa < 0 || pb < 0) return;
			if (pa < pb) (pa, pb) = (pb, pa);
			if (pb < first[pa]) first[pa] = pb;
		}

		// the residual Ra(pa) − Rb(pb), and yaw a + difference − yaw b, into the normal equations
		private void Add(ref NativeArray<double> matrix, ref NativeArray<double> gradient, NativeArray<int> rowStart, NativeArray<int> first, NativeArray<int> position,
			int a, int b, double3 pointA, double3 pointB, double yawDifference, in double4x4 information, double robust = 0)
		{
			double4 stateA = state[a], stateB = state[b];
			double3 turnedA = Pose4Turn(stateA.w, pointA), turnedB = Pose4Turn(stateB.w, pointB);
			double4 residual = new(turnedA + stateA.xyz - turnedB - stateB.xyz, stateA.w + yawDifference - stateB.w);
			double4 weighted = math.mul(information, residual);
			double scale = 1;
			if (robust > 0)
			{
				double distance = math.sqrt(math.max(math.dot(residual, weighted), 0));
				if (distance > robust) scale = robust / distance;
			}

			double4x4 ja = Jacobian(turnedA, 1), jb = Jacobian(turnedB, -1);
			double4x4 ia = math.mul(information, ja), ib = math.mul(information, jb);
			int pa = position[a], pb = position[b];
			if (pa >= 0)
			{
				AddGradient(ref gradient, pa, math.mul(math.transpose(ja), weighted) * scale);
				AddBlock(ref matrix, rowStart, first, pa, pa, math.mul(math.transpose(ja), ia), scale);
			}

			if (pb >= 0)
			{
				AddGradient(ref gradient, pb, math.mul(math.transpose(jb), weighted) * scale);
				AddBlock(ref matrix, rowStart, first, pb, pb, math.mul(math.transpose(jb), ib), scale);
			}

			if (pa >= 0 && pb >= 0 && pa != pb)
			{
				if (pa > pb) AddBlock(ref matrix, rowStart, first, pa, pb, math.mul(math.transpose(ja), ib), scale);
				else AddBlock(ref matrix, rowStart, first, pb, pa, math.mul(math.transpose(jb), ia), scale);
			}
		}

		private static double4x4 Jacobian(double3 turned, double sign) => new double4x4(
			new double4(1, 0, 0, 0), new double4(0, 1, 0, 0), new double4(0, 0, 1, 0), new double4(turned.z, 0, -turned.x, 1)) * sign;

		private static void AddGradient(ref NativeArray<double> gradient, int block, double4 value)
		{
			for (int r = 0; r < 4; r++) gradient[block * 4 + r] += value[r];
		}

		// rows of one block against columns of an earlier or the same one, the lower triangle only
		private static void AddBlock(ref NativeArray<double> matrix, NativeArray<int> rowStart, NativeArray<int> first, int row, int column, in double4x4 block, double scale)
		{
			int firstColumn = first[row] * 4;
			for (int r = 0; r < 4; r++)
			{
				int rowOffset = rowStart[row * 4 + r] - firstColumn + column * 4;
				int columns = row == column ? r + 1 : 4;
				for (int c = 0; c < columns; c++) matrix[rowOffset + c] += block[c][r] * scale;
			}
		}

		private static void Factor(ref NativeArray<double> m, NativeArray<int> rowStart, NativeArray<int> first, int size)
		{
			for (int i = 0; i < size; i++)
			{
				int firstI = first[i / 4] * 4, rowI = rowStart[i] - firstI;
				for (int j = firstI; j <= i; j++)
				{
					int firstJ = first[j / 4] * 4, rowJ = rowStart[j] - firstJ;
					int from = math.max(firstI, firstJ);
					double sum = m[rowI + j];
					for (int k = from; k < j; k++) sum -= m[rowI + k] * m[rowJ + k];
					m[rowI + j] = j < i ? sum / m[rowJ + j] : math.sqrt(math.max(sum, 1e-12));
				}
			}
		}

		private static void Substitute(NativeArray<double> m, NativeArray<int> rowStart, NativeArray<int> first, ref NativeArray<double> x, int size)
		{
			for (int i = 0; i < size; i++)
			{
				int firstI = first[i / 4] * 4, rowI = rowStart[i] - firstI;
				double sum = x[i];
				for (int k = firstI; k < i; k++) sum -= m[rowI + k] * x[k];
				x[i] = sum / m[rowI + i];
			}

			for (int i = size - 1; i >= 0; i--)
			{
				int firstI = first[i / 4] * 4, rowI = rowStart[i] - firstI;
				x[i] /= m[rowI + i];
				for (int k = firstI; k < i; k++) x[k] -= m[rowI + k] * x[i];
			}
		}

		private static double3 Pose4Turn(double angle, double3 v)
		{
			math.sincos(angle, out double s, out double c);
			return new double3(c * v.x + s * v.z, v.y, -s * v.x + c * v.z);
		}
	}
}
