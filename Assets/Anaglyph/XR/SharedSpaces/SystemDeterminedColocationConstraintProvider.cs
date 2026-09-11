using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Leaves the world frame to the XR system. The application adds no tracking-origin
	/// offset; each device's runtime is responsible for giving identity the same meaning.
	/// No anchors, persistence, or reference synchronization are needed.
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
			if (IsRunning) return;
			IsRunning = true;
			if (trackingSpace != null)
				trackingSpace.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
		}

		public void StopProviding() => IsRunning = false;

		public void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (!IsRunning || !IsAvailable) return;

			// Keep the normal solver, agreement, and localization lifecycle in use. This is
			// the system-origin contract, not a claim that a physical anchor was observed.
			results.Add(new ColocationConstraint(
				new Pose(trackingSpace.position, trackingSpace.rotation), Pose.identity,
				hasReliableRotation: true));
		}
	}
}
