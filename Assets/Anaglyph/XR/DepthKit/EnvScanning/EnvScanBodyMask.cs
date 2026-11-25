using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// Fits this capsule mask around the local player's own torso and legs: upright, starting
	/// below the head so the depth eye stays outside, and reaching past the floor.
	/// </summary>
	[RequireComponent(typeof(EnvScanMask))]
	public class EnvScanBodyMask : MonoBehaviour
	{
		[SerializeField] private Transform head;

		[SerializeField] private float topBelowHead = 0.3f;
		[SerializeField] private float bottomBelowHead = 2.2f;
		[SerializeField] private float torsoBehindHead = 0.1f;
		[SerializeField] private float radius = 0.3f;
		[SerializeField] private float minClearanceFromEye = 0.08f;

		private void LateUpdate()
		{
			Vector3 headPos = head.position;
			Vector3 headForwardFlat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
			Vector3 torsoAxis = headPos - headForwardFlat * torsoBehindHead;

			Vector3 top = torsoAxis + Vector3.down * (topBelowHead + radius);
			Vector3 bottom = torsoAxis + Vector3.down * (bottomBelowHead - radius);
			EnvScanMask.FitCapsule(transform, top, bottom, radius, minClearanceFromEye, headPos);
		}
	}
}
