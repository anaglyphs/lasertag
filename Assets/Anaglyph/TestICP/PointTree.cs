using System;
using System.Linq;
using UnityEngine;

namespace Anaglyph
{
	public class PointTree
	{
		private Node root;

		public PointTree(Vector3[] allPoints)
		{
			int numNodes = 0;

			Node BuildBranch(Vector3[] points, int depth)
			{
				if (points == null || points.Length == 0)
					return null;

				Node node = new();

				node.axis = depth % 3;
				// slow asf but we only need to do at game start
				points = points.OrderBy(v => v[node.axis]).ToArray();



				// use closest to mean (rather than median index) for split
				// https://www.ri.cmu.edu/pub_files/pub1/moore_andrew_1991_1/moore_andrew_1991_1.pdf
				// todo: don't linear search mean

				// int split = points.Length / 2;

				float sum = 0;
				foreach (Vector3 p in points)
					sum += p[node.axis];
				float avg = sum / points.Length;

				int split = 0;
				for (int i = 0; i < points.Length; i++)
					if (points[i][node.axis] >= avg)
					{
						split = i;
						break;
					}

				node.point = points[split];

				if (points.Length > 1)
				{
					int nextDepth = depth + 1;
					int lesserSize = split;
					if(lesserSize > 0)
					{
						Vector3[] lesserRange = new Vector3[lesserSize];
						Array.Copy(points, 0, lesserRange, 0, lesserRange.Length);
						node.lesser = BuildBranch(lesserRange, nextDepth);
					}

					int greaterSize = points.Length - split - 1;
					if(greaterSize > 0)
					{
						Vector3[] greaterRange = new Vector3[greaterSize];
						Array.Copy(points, split + 1, greaterRange, 0, greaterRange.Length);
						node.greater = BuildBranch(greaterRange, nextDepth);
					}
				}

				numNodes++;

				return node;
			}

			root = BuildBranch(allPoints, 0);

			Debug.Log($"Created node tree with {numNodes} nodes.");
		}

		public Vector3 ClosestPointTo(Vector3 target)
		{
			Node closest = null;
			float maxDist = float.MaxValue;
			int iterations = 0;

			void Search(Node node)
			{
				if (node == null)
					return;

				var axis = node.axis;
				var point = node.point;
				var lesser = node.lesser;
				var greater = node.greater;

				bool targetGreater = target[axis] >= point[axis];
				Node sideOfTarget = targetGreater ? greater : lesser;
				Node otherSide = targetGreater ? lesser : greater;
				Search(sideOfTarget);

				float pointDist = Vector3.Distance(target, point);
				if (pointDist <= maxDist)
				{
					maxDist = pointDist;
					closest = node;
				}

				bool splitWithinDist = Mathf.Abs(point[axis] - target[axis]) <= maxDist;
				if (splitWithinDist)
				{
					Search(otherSide);
				}

				iterations++;
			}



			Search(root);
			Debug.Log($"Searched through {iterations} nodes");

			return closest.point;
		}


		private class Node
		{
			public Vector3 point;
			public Node lesser;
			public Node greater;
			public int axis;
		}
	}
}
