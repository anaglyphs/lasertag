using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// The colocator's gravity-preserving rigid fit, without applying it: the move of tracking
	/// space that lands each constraint's observed pose on its canon pose.
	/// </summary>
	public static class ColocationFit
	{
		// a diffuse prior: before the first constraint, the fit could be anywhere
		private const double PriorPosition = 100, PriorYaw = math.PI_DBL;

		// the linearization point's inputs, reused: the fit runs every frame
		private static readonly List<(float3 subject, float3 target)> pairs = new();
		private static readonly List<float> weights = new();
		private static readonly List<(float yaw, float weight)> yaws = new();

		/// <summary>
		/// The fit as a Kalman filter re-run over the constraints in the order they were
		/// measured, live ones last. Tracking drifts as a whole, so between measurements the
		/// fit's own uncertainty grows by <paramref name="drift"/> for the walk between them:
		/// an older measurement is worth less because everything since has had time to move,
		/// not because each one ages on its own. Every constraint of every source shares that
		/// one drift. Holds no state: the history is the list.
		/// </summary>
		public static bool TryEvaluate(IReadOnlyList<ColocationConstraint> constraints, in ColocationDrift drift, out Pose delta)
		{
			delta = Pose.identity;
			if (constraints == null || constraints.Count == 0) return false;
			int rotationIndex = -1;
			float rotationAt = float.NegativeInfinity;
			bool observesYaw = false;
			float baseline = 0;
			for (int i = 0; i < constraints.Count; i++)
			{
				var c = constraints[i];
				if (!Finite(c.observed) || !Finite(c.canon)) return false;
				if (c.hasReliableRotation && MeasuredAt(c, drift) >= rotationAt)
				{
					rotationIndex = i;
					rotationAt = MeasuredAt(c, drift);
				}
				observesYaw |= ObservesYaw(c);
				// only whether the spread clears the bar matters: a history runs to hundreds
				for (int j = 0; j < i && baseline < .1f; j++)
				{
					Vector3 observed = c.observed.position - constraints[j].observed.position;
					Vector3 target = c.canon.position - constraints[j].canon.position;
					observed.y = target.y = 0;
					baseline = Mathf.Max(baseline, Mathf.Min(observed.magnitude, target.magnitude));
				}
			}

			// the filter is linearized about this: each constraint weighted as it stands now
			if (baseline >= .1f)
			{
				// each constraint's position with its weight, and its yaw as a direct observation
				// where the source measured it
				pairs.Clear();
				weights.Clear();
				yaws.Clear();
				for (int i = 0; i < constraints.Count; i++)
				{
					ColocationConstraint c = constraints[i];
					pairs.Add((c.observed.position, c.canon.position));
					weights.Add(drift.PositionWeight(c));
					float yawWeight = drift.YawWeight(c);
					if (yawWeight > 0)
						yaws.Add((BestFit.TwistYaw(c.canon.rotation) - BestFit.TwistYaw(c.observed.rotation), yawWeight));
				}

				Matrix4x4 matrix = BestFit.Find4DOF(pairs, weights, yaws);
				delta = new Pose(matrix.GetPosition(), matrix.rotation);
			}
			else
			{
				// the latest rotation alone
				if (rotationIndex < 0) return false;
				var c = constraints[rotationIndex];
				Quaternion relative = c.canon.rotation * Quaternion.Inverse(c.observed.rotation);
				Vector3 heading = relative * Vector3.forward; heading.y = 0;
				if (heading.sqrMagnitude < .01f) return false;
				Quaternion rotation = Quaternion.LookRotation(heading, Vector3.up);
				delta = new Pose(c.canon.position - rotation * c.observed.position, rotation);
			}

			if (!Finite(delta)) return false;
			// nothing for the filter to fix yaw with: the latest rotation's heading stands
			if (baseline < .1f && !observesYaw) return true;
			delta = Filter(constraints, drift, delta);
			return Finite(delta);
		}

		/// <summary>
		/// The filter over (translation, yaw), yaw turning about the constraints' centroid so
		/// the two stay nearly independent however far the map is from the origin.
		/// </summary>
		private static Pose Filter(IReadOnlyList<ColocationConstraint> constraints, in ColocationDrift drift, Pose start)
		{
			int count = constraints.Count;
			double3 pivot = 0;
			for (int i = 0; i < count; i++) pivot += (float3)constraints[i].observed.position;
			pivot /= count;

			// delta(p) = R(φ)(p − pivot) + pivot + t
			double yaw = BestFit.TwistYaw(start.rotation);
			double3 translation = (float3)start.position - pivot + Rotate(yaw, pivot);

			NativeArray<Measurement> measurements = new(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
			NativeArray<double4> state = new(1, Allocator.TempJob);
			try
			{
				for (int i = 0; i < count; i++)
				{
					ColocationConstraint c = constraints[i];
					bool observesYaw = ObservesYaw(c);
					measurements[i] = new Measurement
					{
						observed = (float3)c.observed.position,
						canon = (float3)c.canon.position,
						yaw = observesYaw ? BestFit.TwistYaw(c.canon.rotation) - BestFit.TwistYaw(c.observed.rotation) : 0,
						sigma = c.positionSigma > 0 ? c.positionSigma : 1,
						yawSigma = observesYaw ? c.yawSigma : 0,
						at = MeasuredAt(c, drift),
						index = i,
					};
				}

				state[0] = new double4(translation, yaw);
				new FilterJob
				{
					measurements = measurements,
					state = state,
					pivot = pivot,
					qPosition = (double)drift.positionRate * drift.positionRate,
					qYaw = (double)drift.yawRate * drift.yawRate,
				}.Run();

				double4 x = state[0];
				Quaternion rotation = Quaternion.AngleAxis((float)math.degrees(x.w), Vector3.up);
				return new Pose((float3)(pivot + x.xyz - Rotate(x.w, pivot)), rotation);
			}
			finally
			{
				measurements.Dispose();
				state.Dispose();
			}
		}

		/// <summary>One constraint as the filter reads it; ordered by when it was measured.</summary>
		private struct Measurement : IComparable<Measurement>
		{
			public double3 observed, canon;
			/// <summary>The yaw it observes, radians; read only where <see cref="yawSigma"/> is positive.</summary>
			public double yaw;
			public double sigma, yawSigma;
			public float at;
			public int index;

			public int CompareTo(Measurement other)
			{
				int c = at.CompareTo(other.at);
				return c != 0 ? c : index.CompareTo(other.index);
			}
		}

		[BurstCompile]
		private struct FilterJob : IJob
		{
			public NativeArray<Measurement> measurements;
			/// <summary>In: the pose the filter is linearized about. Out: the fit.</summary>
			public NativeArray<double4> state;
			public double3 pivot;
			public double qPosition, qYaw;

			public void Execute()
			{
				measurements.Sort();
				double4 x = state[0];
				double4x4 p = double4x4.zero;
				p.c0.x = p.c1.y = p.c2.z = PriorPosition * PriorPosition;
				p.c3.w = PriorYaw * PriorYaw;

				float previousAt = float.NaN;
				double3 previousPlace = 0;
				for (int i = 0; i < measurements.Length; i++)
				{
					Measurement m = measurements[i];

					// the walk since the previous one: drift in translation, and in yaw turning
					// about where the head walked, which a pivot elsewhere sees as translation too
					double walked = float.IsNaN(previousAt) || !(m.at > previousAt) ? 0 : m.at - previousAt;
					if (walked > 0)
					{
						p.c0.x += qPosition * walked;
						p.c1.y += qPosition * walked;
						p.c2.z += qPosition * walked;
						double3 lever = Perpendicular(pivot + x.xyz - (previousPlace + m.observed) / 2);
						double4 g = new(lever, 1);
						p += qYaw * walked * OuterProduct(g, g);
					}

					previousAt = m.at;
					previousPlace = m.observed;

					// its position, one axis at a time: the noise is the same on each
					for (int axis = 0; axis < 3; axis++)
					{
						double3 turned = Rotate(x.w, m.observed - pivot);
						double3 predicted = turned + pivot + x.xyz;
						double3 slope = Perpendicular(turned);
						double4 h = axis switch
						{
							0 => new double4(1, 0, 0, slope.x),
							1 => new double4(0, 1, 0, 0),
							_ => new double4(0, 0, 1, slope.z),
						};
						Update(ref x, ref p, h, m.canon[axis] - predicted[axis], m.sigma * m.sigma);
					}

					if (m.yawSigma > 0)
						Update(ref x, ref p, new double4(0, 0, 0, 1), Wrap(m.yaw - x.w), m.yawSigma * m.yawSigma);
				}

				state[0] = x;
			}
		}

		private static void Update(ref double4 x, ref double4x4 p, double4 h, double residual, double noise)
		{
			double4 ph = math.mul(p, h);
			double s = math.dot(h, ph) + noise;
			if (!(s > 0)) return;
			double4 gain = ph / s;
			x += gain * residual;
			p -= OuterProduct(gain, ph);
			p = (p + math.transpose(p)) * .5;
		}

		private static double4x4 OuterProduct(double4 a, double4 b) => new(a * b.x, a * b.y, a * b.z, a * b.w);

		// Quaternion.AngleAxis(angle, up), which turns +x toward −z
		private static double3 Rotate(double angle, double3 v)
		{
			math.sincos(angle, out double sin, out double cos);
			return new double3(v.x * cos + v.z * sin, v.y, -v.x * sin + v.z * cos);
		}

		// how a vector turned by Rotate moves as the angle grows
		private static double3 Perpendicular(double3 v) => new(v.z, 0, -v.x);

		private static float MeasuredAt(in ColocationConstraint c, in ColocationDrift drift) => c.IsLive ? drift.path : c.measuredAt;

		private static bool ObservesYaw(in ColocationConstraint c) => c.hasReliableRotation && c.yawSigma > 0;

		private static double Wrap(double angle)
		{
			angle = math.fmod(angle + math.PI_DBL, 2 * math.PI_DBL);
			if (angle < 0) angle += 2 * math.PI_DBL;
			return angle - math.PI_DBL;
		}

		private static bool Finite(Pose p) => Finite(p.position.x) && Finite(p.position.y) && Finite(p.position.z) &&
			Finite(p.rotation.x) && Finite(p.rotation.y) && Finite(p.rotation.z) && Finite(p.rotation.w) &&
			Mathf.Abs(Quaternion.Dot(p.rotation, p.rotation) - 1) < .01f;
		private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
	}
}
