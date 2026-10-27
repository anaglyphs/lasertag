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

		/// <summary>How much its position counts in a fit over several constraints (1/σ², or any relative measure); 1 by default.</summary>
		public readonly float positionWeight;

		/// <summary>
		/// How much its rotation's yaw counts as a direct observation of the fit's yaw. 0, the
		/// default, leaves the yaw to the positions alone, as a position-only source needs; a
		/// source that measured each constraint's yaw gives 1/σ².
		/// </summary>
		public readonly float yawWeight;

		public ColocationConstraint(Pose observed, Pose canon, bool hasReliableRotation = false) : this(observed, canon, hasReliableRotation, 1f, 0f) { }

		public ColocationConstraint(Pose observed, Pose canon, bool hasReliableRotation, float positionWeight, float yawWeight)
		{
			this.observed = observed;
			this.canon = canon;
			this.hasReliableRotation = hasReliableRotation;
			this.positionWeight = positionWeight;
			this.yawWeight = yawWeight;
		}
	}

	/// <summary>
	/// Supplies a colocator with constraints each frame. A colocator consumes constraints from
	/// a source without caring how its physical references were established. Sources own their
	/// references' lifecycles.
	/// </summary>
	public interface IColocationConstraintSource
	{
		/// <summary>
		/// Appends every constraint that is trustworthy right now (tracked/visible, with a
		/// known canon pose). Called every frame; implementations should not allocate.
		/// </summary>
		void GetColocationConstraints(List<ColocationConstraint> results);
	}

	/// <summary>
	/// A self-contained source that owns the lifecycle of the physical references it exposes.
	/// Exactly one provider is activated by <see cref="Colocator"/> at a time.
	/// </summary>
	public interface IColocationConstraintProvider : IColocationConstraintSource
	{
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
