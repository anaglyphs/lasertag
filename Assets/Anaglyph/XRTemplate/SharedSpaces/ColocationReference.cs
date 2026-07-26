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

		public ColocationReference(Pose observed, Pose canon)
		{
			this.observed = observed;
			this.canon = canon;
		}
	}

	/// <summary>
	/// Feeds colocators their references each frame. Colocators consume references; they do
	/// not own anchors or canon poses — the map system does, and it sits in an assembly this
	/// one cannot see, so the dependency points this way.
	/// </summary>
	public interface IColocationReferenceSource
	{
		/// <summary>
		/// Appends every reference that is trustworthy right now (tracked, with a known canon
		/// pose). Called every frame; implementations should not allocate.
		/// </summary>
		void GetColocationReferences(List<ColocationReference> results);
	}
}
