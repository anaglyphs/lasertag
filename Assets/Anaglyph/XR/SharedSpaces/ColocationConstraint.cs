using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// One correspondence a colocator can align with: a reference pose observed live in the current
	/// (pre-correction) world coordinates, paired with the canon pose that same reference has
	/// in the map's world frame. The fit moves tracking space so observed lands on canon.
	/// </summary>
	public struct ColocationConstraint
	{
		public Pose observed;
		public Pose canon;

		/// <summary>
		/// Whether this constraint's observed rotation is trustworthy enough to align with on
		/// its own. Runtime anchors are; a vision-only source may instead expose position-only
		/// constraints that require multiple correspondences.
		/// </summary>
		public readonly bool hasReliableRotation;

		/// <summary>
		/// How far off its position was when measured, meters (1σ). 0, the default, leaves it
		/// unweighted: every such constraint counts the same, and it never ages.
		/// </summary>
		public readonly float positionSigma;

		/// <summary>
		/// How far off its rotation's yaw was when measured, radians (1σ), as a direct
		/// observation of the fit's yaw. 0, the default, leaves the yaw to the positions alone,
		/// as a position-only source needs.
		/// </summary>
		public readonly float yawSigma;

		/// <summary>
		/// The head's travel (<see cref="HeadPoseHistory.Path"/>) when the pairing of observed with
		/// canon was measured. Tracking drifts as the head walks on, so the colocator trusts it
		/// less the further the head has gone since (<see cref="ColocationDrift"/>). NaN, the
		/// default, is live: a reference the runtime tracks itself, measured as it stands.
		/// </summary>
		public readonly float measuredAt;

		public bool IsLive => float.IsNaN(measuredAt);

		public ColocationConstraint(Pose observed, Pose canon, bool hasReliableRotation = false) : this(observed, canon, hasReliableRotation, 0f, 0f, float.NaN) { }

		public ColocationConstraint(Pose observed, Pose canon, bool hasReliableRotation, float positionSigma, float yawSigma, float measuredAt)
		{
			this.observed = observed;
			this.canon = canon;
			this.hasReliableRotation = hasReliableRotation;
			this.positionSigma = positionSigma;
			this.yawSigma = yawSigma;
			this.measuredAt = measuredAt;
		}
	}

	/// <summary>
	/// How a constraint's uncertainty grows with the head's travel since it was measured:
	/// tracking drifts like a random walk, so σ² gains <c>rate² × meters</c>. And with how far
	/// from the head it lies: tracking errs differently in different places, so a correction
	/// measured across the room says less about the alignment here than one measured here,
	/// σ² gaining <c>(spatialRate × meters away)²</c>. The rates are the headset's, not any one
	/// source's, so the colocator holds them for every provider.
	/// </summary>
	public readonly struct ColocationDrift
	{
		public static readonly ColocationDrift None = default;

		/// <summary>Position σ gained after one meter walked, meters per √meter.</summary>
		public readonly float positionRate;
		/// <summary>Yaw σ gained after one meter walked, radians per √meter.</summary>
		public readonly float yawRate;
		/// <summary>The head's travel now, the same odometer constraints were stamped with.</summary>
		public readonly float path;
		/// <summary>Where the head is now, in the constraints' observed (current world) frame.</summary>
		public readonly Vector3 place;
		/// <summary>Position σ a constraint carries per meter it lies from <see cref="place"/>, meters per meter; 0 ignores place.</summary>
		public readonly float spatialRate;
		/// <summary>Likewise for yaw, radians per meter.</summary>
		public readonly float spatialYawRate;

		public ColocationDrift(float positionRate, float yawRate, float path) : this(positionRate, yawRate, path, default, 0f, 0f) { }

		public ColocationDrift(float positionRate, float yawRate, float path, Vector3 place, float spatialRate, float spatialYawRate)
		{
			this.positionRate = positionRate;
			this.yawRate = yawRate;
			this.path = path;
			this.place = place;
			this.spatialRate = spatialRate;
			this.spatialYawRate = spatialYawRate;
		}

		/// <summary>Meters walked since the constraint was measured; 0 for a live one.</summary>
		public float Age(in ColocationConstraint c) => c.IsLive ? 0f : Mathf.Max(0f, path - c.measuredAt);

		/// <summary>How far across the floor the constraint lies from the head, meters.</summary>
		public float Distance(in ColocationConstraint c)
		{
			Vector3 d = c.observed.position - place;
			return Mathf.Sqrt(d.x * d.x + d.z * d.z);
		}

		/// <summary>The position variance its distance from the head adds, meters².</summary>
		public float SpatialVariance(in ColocationConstraint c) => Square(spatialRate * Distance(c));

		/// <summary>The yaw variance its distance from the head adds, radians².</summary>
		public float SpatialYawVariance(in ColocationConstraint c) => Square(spatialYawRate * Distance(c));

		/// <summary>1/σ² of its position as it stands now, or 1 for an unweighted constraint.</summary>
		public float PositionWeight(in ColocationConstraint c) =>
			c.positionSigma > 0 ? 1f / Mathf.Max(c.positionSigma * c.positionSigma + positionRate * positionRate * Age(c) + SpatialVariance(c), 1e-8f) : 1f;

		/// <summary>1/σ² of its yaw as it stands now, or 0 when it carries no yaw observation.</summary>
		public float YawWeight(in ColocationConstraint c) =>
			c.hasReliableRotation && c.yawSigma > 0 ? 1f / Mathf.Max(c.yawSigma * c.yawSigma + yawRate * yawRate * Age(c) + SpatialYawVariance(c), 1e-10f) : 0f;

		private static float Square(float x) => x * x;
	}

	/// <summary>
	/// Supplies a colocator with constraints each frame, and owns the lifecycle of the
	/// physical references behind them: the colocator only fits what it is given, without
	/// caring how those references were established. Exactly one provider is activated by
	/// <see cref="Colocator"/> at a time.
	/// </summary>
	public interface IColocationConstraintProvider
	{
		/// <summary>
		/// Appends every constraint that is trustworthy right now (tracked/visible, with a
		/// known canon pose). Called every frame; implementations should not allocate.
		/// </summary>
		void GetColocationConstraints(List<ColocationConstraint> results);

		/// <summary>
		/// Whether the runtime this provider observes its references through exists in this
		/// process at all. False means no reference can ever be observed — not that none
		/// happens to be visible right now.
		/// </summary>
		bool IsAvailable { get; }

		bool IsRunning { get; }
		void StartProviding();
		void StopProviding();
	}
}
