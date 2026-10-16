using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Leaves the world frame to the XR system. The tracking space is never moved; each
	/// device's runtime is responsible for giving its frame the same meaning. No anchors,
	/// persistence, or reference synchronization are needed.
	/// </summary>
	public sealed class SystemDeterminedColocationConstraintProvider : IColocationConstraintProvider
	{
		private readonly Transform trackingSpace;

		public SystemDeterminedColocationConstraintProvider(Transform trackingSpace)
		{
			this.trackingSpace = trackingSpace;
		}

		public bool IsAvailable => trackingSpace != null;
		public bool IsRunning { get; private set; }

		public void StartProviding()
		{
			IsRunning = true;
		}

		public void StopProviding() => IsRunning = false;

		public void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (!IsRunning || !IsAvailable) return;

			// Keep the normal solver, agreement, and localization lifecycle in use. This is
			// the system-origin contract, not a claim that a physical anchor was observed.
			// Observed and canon are the same, so the fit never moves the tracking space.
			Pose current = new(trackingSpace.position, trackingSpace.rotation);
			results.Add(new ColocationConstraint(current, current, hasReliableRotation: true));
		}
	}
}
