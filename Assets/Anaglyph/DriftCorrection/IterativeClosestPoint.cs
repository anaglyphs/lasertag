using System;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class IterativeClosestPoint
{
	public static async Task<float4x4> Iterate(NativeArray<float3> subject, float4x4 subjTrans,
		NativeArray<PointTree.Node> targetTree, NativeArray<float3> knnResults)
	{
		if (subject.Length != knnResults.Length)
			throw new ArgumentException("KNN results must be same length as target!");

		PointTree.FindClosestPoints findJob = new()
		{
			pointTransform = subjTrans,
			points = subject,
			nodes = targetTree,
			found = knnResults
		};

		JobHandle findJobHandle = findJob.ScheduleParallelByRef(knnResults.Length, 0, default);
		while (!findJobHandle.IsCompleted)
			await Awaitable.NextFrameAsync();
		findJobHandle.Complete();

		return FitCorresponding(subject, subjTrans, knnResults, float4x4.identity);
	}

	public static float4x4 FitCorresponding(ReadOnlySpan<float3> subject, float4x4 subjTrans,
		ReadOnlySpan<float3> target, float4x4 targTrans)
	{
		if (subject.Length != target.Length)
			throw new ArgumentException("subject must be same length as target!");

		float3 subjCentroid = float3.zero;
		float3 targCentroid = float3.zero;

		for (int i = 0; i < subject.Length; i++)
		{
			float3 subj = subject[i];
			float3 targ = target[i];

			subjCentroid += subj;
			targCentroid += targ;
		}

		subjCentroid /= subject.Length;
		targCentroid /= target.Length;

		subjCentroid = math.transform(subjTrans, subjCentroid);
		targCentroid = math.transform(targTrans, targCentroid);


		// 3-dof rotation is NOT NEEDED
		// float3x3 covariance = new();
		// for (int i = 0; i < target.Length; i++)
		// {
		// 	float3 subjPoint = math.transform(subjTrans, subject[i]) - subjCentroid;
		// 	float3 targPoint = math.transform(targTrans, target[i]) - targCentroid;
		//
		// 	for (int x = 0; x < 3; x++)
		// 		for (int y = 0; y < 3; y++)
		// 			covariance[x][y] += targPoint[y] * subjPoint[x];
		// }
		// quaternion rot = svd.svdRotation(covariance);

		// 1-dof yaw rotation
		float a = 0, b = 0;
		for (int i = 0; i < subject.Length; i++)
		{
			float3 subj = math.transform(subjTrans, subject[i]) - subjCentroid;
			float3 targ = math.transform(targTrans, target[i]) - targCentroid;

			a += subj.x * targ.z - subj.z * targ.x;
			b += subj.x * targ.x + subj.z * targ.z;
		}

		float theta = math.atan2(a, b); // todo: flip if needed
		Quaternion rot = Quaternion.AngleAxis(math.degrees(-theta), Vector3.up);

		float3 pos = targCentroid - math.mul(rot, subjCentroid);
		float4x4 deltaTrans = float4x4.TRS(pos, rot, new float3(1));
		return deltaTrans;
	}

	public static float4x4 FitCorresponding(ReadOnlySpan<float3> subject, ReadOnlySpan<float3> target)
	{
		if (subject.Length != target.Length)
			throw new ArgumentException("subject must be same length as target!");

		float3 subjCentroid = float3.zero;
		float3 targCentroid = float3.zero;

		for (int i = 0; i < subject.Length; i++)
		{
			float3 subj = subject[i];
			float3 targ = target[i];

			subjCentroid += subj;
			targCentroid += targ;
		}

		subjCentroid /= subject.Length;
		targCentroid /= target.Length;


		// 3-dof rotation is NOT NEEDED
		// float3x3 covariance = new();
		// for (int i = 0; i < target.Length; i++)
		// {
		// 	float3 subjPoint = math.transform(subjTrans, subject[i]) - subjCentroid;
		// 	float3 targPoint = math.transform(targTrans, target[i]) - targCentroid;
		//
		// 	for (int x = 0; x < 3; x++)
		// 		for (int y = 0; y < 3; y++)
		// 			covariance[x][y] += targPoint[y] * subjPoint[x];
		// }
		// quaternion rot = svd.svdRotation(covariance);

		// 1-dof yaw rotation
		float a = 0, b = 0;
		for (int i = 0; i < subject.Length; i++)
		{
			float3 subj = subject[i] - subjCentroid;
			float3 targ = target[i] - targCentroid;

			a += subj.x * targ.z - subj.z * targ.x;
			b += subj.x * targ.x + subj.z * targ.z;
		}

		float theta = math.atan2(a, b); // todo: flip if needed
		Quaternion rot = Quaternion.AngleAxis(math.degrees(-theta), Vector3.up);

		float3 pos = targCentroid - math.mul(rot, subjCentroid);
		float4x4 deltaTrans = float4x4.TRS(pos, rot, new float3(1));
		return deltaTrans;
	}
}