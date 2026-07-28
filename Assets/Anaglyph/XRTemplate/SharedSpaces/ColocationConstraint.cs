using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// One correspondence a colocator can align with: a reference observed live in the current
	/// (pre-correction) world coordinates, paired with the canon pose that same reference has
	/// in the map's world frame. The fit moves tracking space so observed lands on canon.
	/// </summary>
	public struct ColocationConstraint
	{
		public Pose observed;
		public Pose canon;

		/// <summary>
		/// Whether this reference's observed rotation is trustworthy enough to align with on
		/// its own. Runtime anchors are; a vision-only source may instead expose position-only
		/// references that require multiple correspondences.
		/// </summary>
		public bool hasReliableRotation;

		public ColocationConstraint(Pose observed, Pose canon, bool hasReliableRotation = false)
		{
			this.observed = observed;
			this.canon = canon;
			this.hasReliableRotation = hasReliableRotation;
		}
	}

	/// <summary>
	/// Supplies a colocator with references each frame. A colocator consumes references from
	/// a source without caring how its physical references were established. Sources own their
	/// references' lifecycles.
	/// </summary>
	public interface IColocationConstraintSource
	{
		/// <summary>
		/// Appends every reference that is trustworthy right now (tracked/visible, with a
		/// known canon pose). Called every frame; implementations should not allocate.
		/// </summary>
		void GetColocationReferences(List<ColocationConstraint> results);
	}

	/// <summary>
	/// A self-contained source that owns the lifecycle of the physical references it exposes.
	/// Exactly one provider is activated by <see cref="ReferenceColocator"/> at a time.
	/// </summary>
	public interface IColocationConstraintProvider : IColocationConstraintSource
	{
		bool IsRunning { get; }
		void StartProviding();
		void StopProviding();
	}
}
