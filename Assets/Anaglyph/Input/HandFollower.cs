using UnityEngine;

namespace Anaglyph.Input
{
	// Drives this transform from the assigned hand's pose, replacing a
	// per-hand TrackedPoseDriver. Pose is reported in the rig's tracking space,
	// so this must live under the XR Origin's tracking root and writes LOCAL
	// position/rotation (the same space TrackedPoseDriver uses).
	public class HandFollower : MonoBehaviour
	{
		[SerializeField] private HandSubject hand;

		private void LateUpdate()
		{
			if (hand.Current == null) return;

			transform.SetLocalPositionAndRotation(hand.Position, hand.Rotation);
		}
	}
}
