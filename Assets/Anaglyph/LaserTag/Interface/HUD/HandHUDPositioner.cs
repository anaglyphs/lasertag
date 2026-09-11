using Anaglyph.XR;
using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Interface.HUD
{
	[DefaultExecutionOrder(-999999)]
	public class HandHUDPositioner : MonoBehaviour
	{
		[SerializeField] public float horizontalOffset = 0.15f;
		[SerializeField] private float handSwapTime = 0.3f;
		[SerializeField] private float handSwapThresh = 0.02f;

		private HandInput follow;

		private int side = 1;
		private float swapTimer = float.MaxValue;

		// Stick with the hand being followed while it still tracks, so a HUD on the hand you are
		// holding the blaster in doesn't jump when the other controller wakes up.
		private HandInput FindHandToFollow()
		{
			if (follow != null && follow.IsTracking)
				return follow;

			foreach (HandInput handInput in HandInput.Hands.Values)
				if (handInput.IsTracking)
					return handInput;

			return null;
		}

		private void LateUpdate()
		{
			follow = FindHandToFollow();
			if (follow == null || MainXRRig.Instance == null) return;

			Transform camTrans = MainXRRig.Camera.transform;
			Vector3 camPos = camTrans.position;

			Vector3 pos = MainXRRig.TrackingSpace.TransformPoint(follow.Position);

			// determine side
			// controller handedness DOES NOT always equal side the controller is ACTUALLY on
			// e.g. when using the Striker Mavrik, the left controller is in the blaster's cradle
			// for tracking, but the user can hold the blaster on their right side
			Vector3 posCamSpace = camTrans.InverseTransformPoint(pos);
			int currSide = posCamSpace.x >= 0 ? -1 : 1;
			if (currSide == side || Mathf.Abs(posCamSpace.x) < handSwapThresh)
			{
				swapTimer = 0;
			}
			else
			{
				swapTimer += Time.deltaTime;
				if (swapTimer > handSwapTime)
					side = currSide;
			}

			Vector3 camToHandFlat = Vector3.ProjectOnPlane(pos - camPos, Vector3.up);
			Vector3 offs = Vector3.Cross(Vector3.up, camToHandFlat).normalized * (horizontalOffset * side);
			transform.position = pos + offs;

			Vector3 lookDir = (transform.position - camPos).normalized;
			float upLerp = Mathf.Abs(Vector3.Dot(camTrans.forward, Vector3.up));
			Vector3 lookUpDir = Vector3.Lerp(Vector3.up, camTrans.up, upLerp);

			transform.rotation = Quaternion.LookRotation(lookDir, lookUpDir);
		}
	}
}
