using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Anaglyph
{
	public class MeshICP : MonoBehaviour
	{
		public static int numNodes = 0;

		public Transform searcher;
		public Transform shower;

		private class Node
		{
			public Vector3 point;
			public Node lesser;
			public Node greater;

			public static Node NewNode(Vector3[] pointRange, int depth = 0)
			{
				if (pointRange.Length == 0)
					return null;

				Node newNode = new Node();

				int dim = depth % 3;
				pointRange = pointRange.OrderBy(v => v[dim]).ToArray();
				int median = pointRange.Length / 2;

				newNode.point = pointRange[median];

				if (pointRange.Length > 2)
				{
					Vector3[] lesserRange = new Vector3[median];
					Vector3[] greaterRange = new Vector3[pointRange.Length - median];

					Array.Copy(pointRange, 0, lesserRange, 0, lesserRange.Length);
					Array.Copy(pointRange, lesserRange.Length, greaterRange, 0, greaterRange.Length);

					newNode.lesser = NewNode(lesserRange, dim + 1);
					newNode.greater = NewNode(greaterRange, dim + 1);
				}

				numNodes++;

				return newNode;
			}

			public Node ClosestPoint(Vector3 to, int depth = 0)
			{
				if (lesser == null)
					return this;

				int dim = depth % 3;
				Node next = to[dim] > point[dim] ? greater : lesser;
				next = next.ClosestPoint(to, depth + 1);

				float maxDist = Vector3.Distance(to, next.point);

				if(Mathf.Abs(point[dim] - to[dim]) < maxDist)
				{
					next = to[dim] > point[dim] ? lesser : greater;
					next = next.ClosestPoint(to, depth + 1); ;
				}

				return next;
			}
		}

		private Node kdTree;

		private void Start()
		{
			List<Vector3> pointsList = new();

			MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();

			foreach(var meshFilter in meshFilters)
				pointsList.AddRange(meshFilter.sharedMesh.vertices);

			var points = pointsList.ToArray();

			kdTree = Node.NewNode(points);

			Debug.Log(numNodes);
		}

		private void Update()
		{
			Node closest = kdTree.ClosestPoint(searcher.position);
			shower.position = closest.point;
		}
	}
}
