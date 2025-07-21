using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph
{
	public class MeshICP : MonoBehaviour
	{
		private PointTree tree;

		public MeshFilter toCorrect;

		private void Start()
		{
			List<Vector3> pointsList = new();

			MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();

			foreach(var meshFilter in meshFilters)
				pointsList.AddRange(meshFilter.sharedMesh.vertices);

			var points = pointsList.ToArray();

			tree = new(points);
		}
	}
}
