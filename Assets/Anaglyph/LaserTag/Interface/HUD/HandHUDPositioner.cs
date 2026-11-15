using Anaglyph.XR;
using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Interface.HUD
{
	[DefaultExecutionOrder(999999)]
	public class HandHUDPositioner : MonoBehaviour
	{
		[SerializeField] public float horizontalOffset = 0.15f;

		private HandInput follow;

		// 1 puts the HUD left of the controller: toward the body for a right-handed player
		public static int ControllerSide => Dominance.Hand == Handedness.Right ? 1 : -1;

		public void SetHand(HandInput hand)
		{
			follow = hand;
		}

		private void LateUpdate()
		{
			if (follow == null || !follow.IsTracking || MainXRRig.Instance == null) return;

			Transform camTrans = MainXRRig.Camera.transform;
			Vector3 camPos = camTrans.position;

			Vector3 handPos = MainXRRig.TrackingSpace.TransformPoint(follow.Position);

			Vector3 lookDir = (handPos - camPos).normalized;
			float upLerp = Mathf.Abs(Vector3.Dot(camTrans.forward, Vector3.up));
			Vector3 lookUpDir = Vector3.Lerp(Vector3.up, camTrans.up, upLerp);
			Vector3 offs = Vector3.Cross(lookDir, lookUpDir).normalized * (horizontalOffset * ControllerSide);
			transform.position = handPos + offs;

			lookDir = (transform.position - camPos).normalized;
			transform.rotation = Quaternion.LookRotation(lookDir, lookUpDir);
		}
	}
}
