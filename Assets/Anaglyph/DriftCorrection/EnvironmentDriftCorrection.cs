using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph
{
	public class EnvironmentDriftCorrection : MonoBehaviour
	{
		[SerializeField] private GameObject environmentObject;
		[SerializeField] private MeshFilter testDepthMesh;
		[SerializeField] int icpIterations = 10;

		NativeArray<PointTree.Node> tree;

		private void Awake()
		{
			List<float3> vertices = new();
			MeshFilter[] meshFilters = environmentObject.GetComponentsInChildren<MeshFilter>();

			foreach (var meshFilter in meshFilters)
				foreach (var vertex in meshFilter.mesh.vertices)
					vertices.Add(vertex);

			tree = new NativeArray<PointTree.Node>(vertices.Count, Allocator.Persistent);
			NativeArray<float3> points = new NativeArray<float3>(vertices.ToArray(), Allocator.TempJob);

			PointTree.BuildJob buildJob = new()
			{
				points = points,
				nodes = tree,
			};

			// buildJob.Run();
			buildJob.Schedule().Complete();

			points.Dispose();
		}

		private async void OnEnable()
		{
			float3[] points = new float3[testDepthMesh.mesh.vertices.Length];
			for (int i = 0; i < points.Length; i++)
				points[i] = testDepthMesh.mesh.vertices[i];

			NativeArray<float3> knnResults = new(points.Length, Allocator.Persistent);
			NativeArray<float3> newPoints = new(points, Allocator.Persistent);

			
			while (enabled)
			{
				float4x4 trans = transform.localToWorldMatrix;
				Matrix4x4 mat = await IterativeClosestPoint.Iterate(newPoints, trans, tree, knnResults);
				mat = mat * transform.localToWorldMatrix;

				testDepthMesh.transform.SetPositionAndRotation(mat.GetPosition(), mat.rotation);
			}

			knnResults.Dispose();
			newPoints.Dispose();
		}
	}
}
