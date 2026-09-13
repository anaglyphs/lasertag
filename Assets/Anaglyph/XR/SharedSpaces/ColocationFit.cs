using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>Non-mutating gravity-preserving fit, shared by alignment, validation and frame reconciliation.</summary>
	public static class ColocationFit
	{
		public static bool TryEvaluate(IReadOnlyList<ColocationConstraint> constraints, out Pose delta,
			out float maximumError, out float maximumAngularError)
		{
			delta = Pose.identity; maximumError = maximumAngularError = 0;
			if (constraints == null || constraints.Count == 0) return false;
			int rotationIndex = -1;
			float baseline = 0;
			for (int i = 0; i < constraints.Count; i++)
			{
				var c = constraints[i];
				if (!Finite(c.observed) || !Finite(c.canon)) return false;
				if (c.hasReliableRotation) rotationIndex = i;
				for (int j = 0; j < i; j++)
				{
					Vector3 observed = c.observed.position - constraints[j].observed.position;
					Vector3 target = c.canon.position - constraints[j].canon.position;
					observed.y = target.y = 0;
					baseline = Mathf.Max(baseline, Mathf.Min(observed.magnitude, target.magnitude));
				}
			}
			if (baseline >= .1f)
			{
				List<(float3 subject, float3 target)> pairs = new(constraints.Count);
				foreach (var c in constraints) pairs.Add((c.observed.position, c.canon.position));
				Matrix4x4 matrix = BestFit.Find4DOF(pairs);
				delta = new Pose(matrix.GetPosition(), matrix.rotation);
			}
			else
			{
				if (rotationIndex < 0) return false;
				var c = constraints[rotationIndex];
				Quaternion relative = c.canon.rotation * Quaternion.Inverse(c.observed.rotation);
				Vector3 heading = relative * Vector3.forward; heading.y = 0;
				if (heading.sqrMagnitude < .01f) return false;
				Quaternion rotation = Quaternion.LookRotation(heading, Vector3.up);
				delta = new Pose(c.canon.position - rotation * c.observed.position, rotation);
			}
			if (!Finite(delta)) return false;
			foreach (var c in constraints)
			{
				maximumError = Mathf.Max(maximumError, Vector3.Distance(delta.position + delta.rotation * c.observed.position, c.canon.position));
				if (c.hasReliableRotation)
					maximumAngularError = Mathf.Max(maximumAngularError, Quaternion.Angle(delta.rotation * c.observed.rotation, c.canon.rotation));
			}
			return true;
		}
		private static bool Finite(Pose p) => Finite(p.position.x) && Finite(p.position.y) && Finite(p.position.z) &&
			Finite(p.rotation.x) && Finite(p.rotation.y) && Finite(p.rotation.z) && Finite(p.rotation.w) &&
			Mathf.Abs(Quaternion.Dot(p.rotation, p.rotation) - 1) < .01f;
		private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
	}
}
