using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;

public class PointTreeBurstTest : MonoBehaviour
{
	NativeArray<PointTreeBurst.Node> tree;

	public Transform target;
	public Transform pointIndicator;

	private void Start()
	{
		List<float3> vertices = new();
		MeshFilter[] meshFilters = GetComponentsInChildren<MeshFilter>();

		foreach (var meshFilter in meshFilters)
			foreach (var vertex in meshFilter.mesh.vertices)
				vertices.Add(vertex);

		tree = new NativeArray<PointTreeBurst.Node>(vertices.Count, Allocator.Persistent);
		NativeArray<float3> points = new NativeArray<float3>(vertices.ToArray(), Allocator.TempJob);

		PointTreeBurst.BuildTreeJob buildJob = new()
		{
			points = points,
			nodes = tree,
		};

		// buildJob.Run();
		buildJob.Schedule().Complete();

		points.Dispose();
	}

	private void Update()
	{
		NativeArray<float3> results = new(1, Allocator.TempJob);
		NativeArray<float3> points = new(1, Allocator.TempJob);
		points[0] = target.position;

		PointTreeBurst.FindClosestPoints findJob = new()
		{
			pointTransform = float4x4.identity,
			points = points,
			nodes = tree,
			found = results,
		};

		findJob.ScheduleParallelByRef(results.Length, 0, default).Complete();

		pointIndicator.position = results[0];

		points.Dispose();
		results.Dispose();
	}
}
