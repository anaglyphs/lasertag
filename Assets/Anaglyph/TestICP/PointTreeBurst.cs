using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public static class PointTreeBurst
{
	public static void QuicksortPoints(NativeArray<float3> points, int axis) =>
		QuicksortPoints(points, 0, points.Length - 1, axis);

	public static void QuicksortPoints(NativeArray<float3> points, int first, int last, int axis)
	{
		if (first >= last)
			return;

		int count = last - first + 1;
		if (count < 2)
			return;

		int mid = (first + last) / 2;
		float pivotValue = points[mid][axis];

		// partition
		int l = first;
		int g = last;

		while (l < g)
		{
			while (points[l][axis] < pivotValue)
				l++;

			while (points[g][axis] > pivotValue)
				g--;

			if (l >= g)
				break;

			float3 pl = points[l];
			points[l] = points[g];
			points[g] = pl;
			l++;
			g--;
		}

		QuicksortPoints(points, first, g, axis);
		QuicksortPoints(points, g + 1, last, axis);
	}

	public struct Node
	{
		public float3 point;
		public int lesser;
		public int greater;
	}

	[BurstCompile]
	public struct BuildTreeJob : IJob
	{
		public NativeArray<float3> points;
		[WriteOnly]
		public NativeArray<Node> nodes;

		private int numNodes;

		public void Execute()
		{
			numNodes = 0;
			BuildBranch(0, points.Length - 1, 0, ref numNodes);
		}

		private int BuildBranch(int start, int end, int depth, ref int numNodes)
		{
			if (start >= end)
				return -1;

			Node node = new();
			node.lesser = -1;
			node.greater = -1;

			int index = numNodes;
			numNodes++;

			int axis = depth % 3;

			QuicksortPoints(points, start, end, axis);

			int split = (start + end) / 2;

			node.point = points[split];

			int length = end - start + 1;
			if(length > 1)
			{
				int nextDepth = depth + 1;

				if(split > start)
					node.lesser = BuildBranch(start, split, nextDepth, ref numNodes);

				if(split + 1 < end)
					node.greater = BuildBranch(split + 1, end, nextDepth, ref numNodes);
			}

			nodes[index] = node;
			return index;
		}
	}

	[BurstCompile]
	public struct FindClosestPoints : IJobFor
	{
		[ReadOnly]
		public float4x4 pointTransform;
		[ReadOnly]
		public NativeArray<float3> points;
		[ReadOnly]
		public NativeArray<Node> nodes;
		[WriteOnly]
		public NativeArray<float3> found;

		public void Execute(int index)
		{
			int closest = -1;
			float maxDistSqr = float.MaxValue;
			int iterations = 0;

			float3 point = points[index];
			var transformAff = new AffineTransform(pointTransform);
			var pointAff = new AffineTransform(point, quaternion.identity);
			float3 target = math.mul(transformAff, pointAff).t;

			Search(target, 0, 0, ref maxDistSqr, ref closest, ref iterations);

			found[index] = nodes[closest].point;
		}

		void Search(float3 target, int iNode, int depth, ref float maxDistSqr, ref int closest, ref int iterations)
		{
			if (iNode == -1)
				return;

			Node node = nodes[iNode];

			int axis = depth % 3;
			bool targetGreater = target[axis] >= node.point[axis];

			int iSideOfTarget = targetGreater ? node.greater : node.lesser;
			int iOtherSide = targetGreater ? node.lesser : node.greater;

			Search(target, iSideOfTarget, depth + 1, ref maxDistSqr, ref closest, ref iterations);

			float pointDistSqr = math.distancesq(target, node.point);
			if (pointDistSqr <= maxDistSqr)
			{
				maxDistSqr = pointDistSqr;
				closest = iNode;
			}

			float diff = node.point[axis] - target[axis];
			bool splitWithinDistSqr = diff * diff <= maxDistSqr;

			if (splitWithinDistSqr)
				Search(target, iOtherSide, depth + 1, ref maxDistSqr, ref closest, ref iterations);

			iterations++;
		}


	}
}
