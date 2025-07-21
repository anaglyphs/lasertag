using System;
using System.Linq;
using UnityEngine;

namespace Anaglyph
{
	public class PointTree
	{
		private Node root;

		public PointTree(Vector3[] points)
		{
			int numNodes = 0;
			Node root = Node.Branch(points, 0, ref numNodes);
			Debug.Log($"Created node tree with {numNodes} nodes.");
		}

		public Vector3 ClosestPointTo(Vector3 target)
		{
			float maxDist = float.MaxValue;
			Node leaf = null;
			int iterations = 0;
			root.ClosestPoint(target, ref leaf, ref maxDist, ref iterations);
			Debug.Log($"Iterated through {iterations} nodes.");
			return leaf.point;
		}

		private class Node
		{
			public Vector3 point;
			public Node lesser;
			public Node greater;
			public int axis;

			public static Node Branch(Vector3[] pointRange, int depth, ref int numNodes)
			{
				if (pointRange.Length == 0)
					return null;

				Node newNode = new Node();

				int axis = depth % 3;
				pointRange = pointRange.OrderBy(v => v[axis]).ToArray();
				int median = pointRange.Length / 2;

				newNode.axis = axis;
				newNode.point = pointRange[median];

				if (pointRange.Length > 2)
				{
					Vector3[] lesserRange = new Vector3[median];
					Vector3[] greaterRange = new Vector3[pointRange.Length - median];

					Array.Copy(pointRange, 0, lesserRange, 0, lesserRange.Length);
					Array.Copy(pointRange, lesserRange.Length, greaterRange, 0, greaterRange.Length);

					newNode.lesser = Branch(lesserRange, axis + 1, ref numNodes);
					newNode.greater = Branch(greaterRange, axis + 1, ref numNodes);
				}

				numNodes++;

				return newNode;
			}

			public void ClosestPoint(Vector3 target, ref Node leaf, ref float maxDist, ref int iterations)
			{
				bool isLeaf = lesser == null;

				if (isLeaf)
				{
					float dist = Vector3.Distance(point, target);
					if (dist < maxDist)
					{
						maxDist = dist;
						leaf = this;
					}
				}
				else
				{
					bool targetGreaterOnAxis = target[axis] > point[axis];
					Node targetSide = targetGreaterOnAxis ? greater : lesser;

					targetSide.ClosestPoint(target, ref leaf, ref maxDist, ref iterations);

					float axisDistToOther = Mathf.Abs(point[axis] - target[axis]);
					bool otherSideIsWithinMaxDist = axisDistToOther < maxDist;

					if (otherSideIsWithinMaxDist)
					{
						Node otherSide = targetGreaterOnAxis ? lesser : greater;
						otherSide.ClosestPoint(target, ref leaf, ref maxDist, ref iterations);
					}
				}

				iterations++;
			}
		}
	}
}
