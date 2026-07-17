using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public static class BestFit
	{
		public static float4x4 Find4DOF(IReadOnlyList<(float3 subject, float3 target)> corresponding)
		{
			float3 subjCentroid = float3.zero;
			float3 targCentroid = float3.zero;

			for (int i = 0; i < corresponding.Count; i++)
			{
				float3 subj = corresponding[i].subject;
				float3 targ = corresponding[i].target;

				subjCentroid += subj;
				targCentroid += targ;
			}

			subjCentroid /= corresponding.Count;
			targCentroid /= corresponding.Count;

			// 1-dof yaw rotation
			float a = 0, b = 0;
			for (int i = 0; i < corresponding.Count; i++)
			{
				float3 subj = corresponding[i].subject - subjCentroid;
				float3 targ = corresponding[i].target - targCentroid;

				a += subj.x * targ.z - subj.z * targ.x;
				b += subj.x * targ.x + subj.z * targ.z;
			}

			float theta = math.atan2(a, b); // todo: flip if needed
			Quaternion rot = Quaternion.AngleAxis(math.degrees(-theta), Vector3.up);

			float3 pos = targCentroid - math.mul(rot, subjCentroid);
			return float4x4.TRS(pos, rot, new float3(1.0));
		}


		// public static float4x4 Find6DOF(IReadOnlyList<float3> subject, IReadOnlyList<float3> target)
		// {
		// 	if (subject.Count != target.Count)
		// 		throw new ArgumentException("subject must be same length as target!");
		//
		// 	float3 subjCentroid = float3.zero;
		// 	float3 targCentroid = float3.zero;
		//
		// 	for (int i = 0; i < subject.Count; i++)
		// 	{
		// 		float3 subj = subject[i];
		// 		float3 targ = target[i];
		//
		// 		subjCentroid += subj;
		// 		targCentroid += targ;
		// 	}
		//
		// 	subjCentroid /= subject.Count;
		// 	targCentroid /= target.Count;
		//
		// 	float3x3 covariance = new();
		// 	for (int i = 0; i < target.Count; i++)
		// 	{
		// 		float3 subjPoint = subject[i] - subjCentroid;
		// 		float3 targPoint = target[i] - targCentroid;
		//
		// 		for (int x = 0; x < 3; x++)
		// 		for (int y = 0; y < 3; y++)
		// 			covariance[x][y] += targPoint[y] * subjPoint[x];
		// 	}
		//
		// 	quaternion rot = svd.svdRotation(covariance);
		//
		// 	float3 pos = targCentroid - math.mul(rot, subjCentroid);
		//
		// 	return float4x4.TRS(pos, rot, new float3(1.0));
		// }
	}
}