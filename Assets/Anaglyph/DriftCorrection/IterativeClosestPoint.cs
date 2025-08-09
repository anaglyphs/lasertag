using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class IterativeClosestPoint
{
	public static async Task<float4x4> IterateClosestPoints(NativeArray<float3> subject, float4x4 subjTrans, NativeArray<PointTree.Node> targetTree, NativeArray<float3> knnResults)
	{
		if(subject.Length != knnResults.Length)
			throw new ArgumentException("KNN results must be same length as target!");

		PointTree.FindClosestPoints findJob = new()
		{
			pointTransform = subjTrans,
			points = subject,
			nodes = targetTree,
			found = knnResults,
		};

		var findJobHandle = findJob.ScheduleParallelByRef(knnResults.Length, 0, default);
		while (!findJobHandle.IsCompleted)
			await Awaitable.NextFrameAsync();
		findJobHandle.Complete();

		return FitCorrespondingPoints(subject, subjTrans, knnResults, float4x4.identity);
	}

	public static float4x4 FitCorrespondingPoints(ReadOnlySpan<float3> subject, float4x4 subjTrans, ReadOnlySpan<float3> target, float4x4 targTrans)
	{
		if (subject.Length != target.Length)
			throw new ArgumentException("subject must be same length as target!");

		float3 subjCentroid = float3.zero;
		
		foreach (float3 v in subject)
			subjCentroid += v;
		subjCentroid /= subject.Length;
		subjCentroid = math.transform(subjTrans, subjCentroid);

		float3 targCentroid = float3.zero;
		foreach (float3 v in subject)
			targCentroid += v;
		targCentroid /= target.Length;
		targCentroid = math.transform(targTrans, targCentroid);

		float3x3 covariance = new();
		for (int i = 0; i < target.Length; i++)
		{
			float3 subjPoint = math.transform(subjTrans, subject[i]) - subjCentroid;
			float3 targPoint = math.transform(targTrans, target[i]) - targCentroid;

			for (int x = 0; x < 3; x++)
				for (int y = 0; y < 3; y++)
					covariance[x][y] += targPoint[y] * subjPoint[x];
		}

		quaternion rot = svd.svdRotation(covariance);

		float3 pos = targCentroid - math.mul(rot, subjCentroid);
		float4x4 deltaTrans = float4x4.TRS(pos, rot, new float3(1));
		return math.mul(deltaTrans, subjTrans);
	}
}
