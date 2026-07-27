using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// One correspondence a colocator can align with: a reference observed live in the current
	/// (pre-correction) world coordinates, paired with the canon pose that same reference has
	/// in the map's world frame. The fit moves tracking space so observed lands on canon.
	/// </summary>
	public struct ColocationReference
	{
		public Pose observed;
		public Pose canon;

		/// <summary>
		/// Whether this reference's observed rotation is trustworthy enough to align with on
		/// its own. Anchors are (the runtime tracks a full pose); a tag's rotation comes from
		/// a single noisy image estimate, so several tags must be triangulated by position
		/// instead. One rotation-bearing reference fully constrains a fit; positions alone
		/// need three.
		/// </summary>
		public bool hasReliableRotation;

		public ColocationReference(Pose observed, Pose canon, bool hasReliableRotation = false)
		{
			this.observed = observed;
			this.canon = canon;
			this.hasReliableRotation = hasReliableRotation;
		}
	}

	/// <summary>
	/// Supplies a colocator with references each frame. A colocator consumes references from
	/// any number of sources and does not care where they come from — anchors, tags, or
	/// anything added later. Sources own their own references' lifecycles.
	/// </summary>
	public interface IColocationReferenceSource
	{
		/// <summary>
		/// Appends every reference that is trustworthy right now (tracked/visible, with a
		/// known canon pose). Called every frame; implementations should not allocate.
		/// </summary>
		void GetColocationReferences(List<ColocationReference> results);
	}
}
