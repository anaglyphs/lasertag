using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// Fits this capsule mask around an untracked arm, from a shoulder estimated off the head
	/// to the tracked hand. Widens as the hand nears the shoulder, to cover wherever the bent
	/// elbow can be. Expects Unity's primitive capsule mesh (radius 0.5, height 2).
	/// </summary>
	[RequireComponent(typeof(EnvScanMask))]
	public class EnvScanArmMask : MonoBehaviour
	{
		public enum Side { Left = -1, Right = 1 }

		[SerializeField] private Transform head;
		[SerializeField] private Transform hand;
		[SerializeField] private Side side = Side.Right;

		[SerializeField] private float shoulderBelowHead = 0.25f;
		[SerializeField] private float shoulderOutFromHead = 0.18f;
		[SerializeField] private float upperArmLength = 0.32f;
		[SerializeField] private float armThickness = 0.08f;
		[SerializeField] private float reachPastHand = 0.12f;
		[SerializeField] private float minClearanceFromEye = 0.08f;

		private void LateUpdate()
		{
			Vector3 headPos = head.position;
			Vector3 headRightFlat = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
			Vector3 shoulder = headPos + Vector3.down * shoulderBelowHead
			                           + headRightFlat * ((int)side * shoulderOutFromHead);

			Vector3 shoulderToHand = hand.position - shoulder;
			float shoulderToHandDist = shoulderToHand.magnitude;
			Vector3 armDir = shoulderToHandDist > 0.001f ? shoulderToHand / shoulderToHandDist : Vector3.down;
			Vector3 tip = hand.position + armDir * reachPastHand;

			float halfReach = shoulderToHandDist / 2;
			float elbowOffsetFromArmLine = Mathf.Sqrt(Mathf.Max(0, upperArmLength * upperArmLength - halfReach * halfReach));
			float radius = armThickness + elbowOffsetFromArmLine;

			EnvScanMask.FitCapsule(transform, shoulder, tip, radius, minClearanceFromEye, headPos);
		}
	}
}
