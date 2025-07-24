using Anaglyph;
using System.Collections.Generic;
using UnityEngine;

public class PointTreeTest : MonoBehaviour
{
	PointTree tree;

	public Transform target;
	public Transform pointIndicator;

	private void Start()
	{
		List<Vector3> vertices = new();
		MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();

		foreach (var meshFilter in meshFilters)
			vertices.AddRange(meshFilter.mesh.vertices);

		tree = new PointTree(vertices.ToArray());
	}

	private void Update()
	{
		Vector3 closest = tree.ClosestPointTo(target.position);
		pointIndicator.position = closest;
	}
}
