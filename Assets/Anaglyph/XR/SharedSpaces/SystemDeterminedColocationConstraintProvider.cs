using System;
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
		private readonly Func<Transform> getTrackingSpace;
		private Transform trackingSpace => getTrackingSpace();

		public SystemDeterminedColocationConstraintProvider(Transform trackingSpace) : this(() => trackingSpace) { }

		/// <summary>Resolves the tracking space on use, so construction needn't wait for the rig to wake.</summary>
		public SystemDeterminedColocationConstraintProvider(Func<Transform> trackingSpace)
		{
			getTrackingSpace = trackingSpace ?? throw new ArgumentNullException(nameof(trackingSpace));
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
