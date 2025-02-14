using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public interface IAnchor
	{
		public bool Anchored { get; }
		public Pose TrackedPose { get; }
		public Pose AnchoredPose { get; }
	}
}
