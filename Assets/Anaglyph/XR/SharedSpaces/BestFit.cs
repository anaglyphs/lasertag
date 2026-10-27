using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	public static class BestFit
	{
		public static float4x4 Find4DOF(IReadOnlyList<(float3 subject, float3 target)> corresponding) => Find4DOF(corresponding, null, null);

		/// <summary>
		/// The yaw-and-translation transform laying the subjects on the targets, each pair
		/// counted by its weight (1 when none), with direct observations of the yaw
		/// (<paramref name="yaws"/>: radians about world up in Unity's sense, each with its
		/// weight) joining the positions' say: weighted least squares over both.
		/// </summary>
		public static float4x4 Find4DOF(IReadOnlyList<(float3 subject, float3 target)> corresponding, IReadOnlyList<float> weights,
			IReadOnlyList<(float yaw, float weight)> yaws)
		{
			double3 subjCentroid = 0, targCentroid = 0;
			double sum = 0;
			for (int i = 0; i < corresponding.Count; i++)
			{
				double w = Weight(weights, i);
				subjCentroid += w * (double3)corresponding[i].subject;
				targCentroid += w * (double3)corresponding[i].target;
				sum += w;
			}

			subjCentroid /= sum;
			targCentroid /= sum;

			// 1-dof yaw rotation: the positions' cost over the angle φ is −2(b cos φ − a sin φ)
			double a = 0, b = 0;
			for (int i = 0; i < corresponding.Count; i++)
			{
				double w = Weight(weights, i);
				double3 subj = (double3)corresponding[i].subject - subjCentroid;
				double3 targ = (double3)corresponding[i].target - targCentroid;

				a += w * (subj.x * targ.z - subj.z * targ.x);
				b += w * (subj.x * targ.x + subj.z * targ.z);
			}

			double phi = math.atan2(-a, b);
			if (yaws != null && yaws.Count > 0)
			{
				// Newton on the joint cost: the positions' term and each yaw's weighted squared difference
				for (int iteration = 0; iteration < 8; iteration++)
				{
					double gradient = 2 * (b * math.sin(phi) + a * math.cos(phi));
					double hessian = 2 * (b * math.cos(phi) - a * math.sin(phi));
					foreach ((float yaw, float weight) in yaws)
					{
						gradient += 2 * weight * Wrap(phi - yaw);
						hessian += 2 * weight;
					}

					if (hessian <= 0) break;
					double step = -gradient / hessian;
					phi += step;
					if (math.abs(step) < 1e-7) break;
				}
			}

			Quaternion rot = Quaternion.AngleAxis((float)math.degrees(phi), Vector3.up);
			float3 pos = (float3)targCentroid - math.mul(rot, (float3)subjCentroid);
			return float4x4.TRS(pos, rot, new float3(1.0));
		}

		private static double Weight(IReadOnlyList<float> weights, int i) =>
			weights == null || i >= weights.Count || !(weights[i] > 0) ? 1 : weights[i];

		/// <summary>Rotation about world up from the swing-twist decomposition, radians, in the sense of Quaternion.AngleAxis(angle, up).</summary>
		public static float TwistYaw(Quaternion q)
		{
			if (math.abs(q.y) < 1e-8f && math.abs(q.w) < 1e-8f) return 0f;
			return (float)Wrap(2.0 * math.atan2(q.y, q.w));
		}

		private static double Wrap(double angle)
		{
			angle = math.fmod(angle + math.PI_DBL, 2 * math.PI_DBL);
			if (angle < 0) angle += 2 * math.PI_DBL;
			return angle - math.PI_DBL;
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