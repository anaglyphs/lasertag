using System;
using Anaglyph.XR;
using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Interface.HUD
{
	[DefaultExecutionOrder(999999)]
	public class HandHUDPositioner : MonoBehaviour
	{
		[SerializeField] public float horizontalOffset = 0.15f;
		[SerializeField] private float handSwapTime = 0.3f;
		[SerializeField] private float handSwapThresh = 0.02f;

		private HandInput follow;

		private int _side = 1;
		private float swapTime = 0;

		// this is NOT controller handedness, but what side of the CONTROLLER the hud should be on
		public int ControllerSide => _side;
		
		// this is NOT controller handedness, but what side of the CONTROLLER the hud should be on
		public event Action<int> ControllerSideSwapped;

		public void SetHand(HandInput hand)
		{
			if (hand == follow)
				return;
			
			follow = hand;
			if (hand != null)
				_side = hand.Handedness == Handedness.Left ? 1 : -1;
		}

		private void SetSide(int side)
		{
			_side = side;
			ControllerSideSwapped?.Invoke(_side);
		}

		private void LateUpdate()
		{
			if (follow == null || !follow.IsTracking || MainXRRig.Instance == null) return;

			Transform camTrans = MainXRRig.Camera.transform;
			Vector3 camPos = camTrans.position;

			Vector3 handPos = MainXRRig.TrackingSpace.TransformPoint(follow.Position);

			// determine side
			// controller handedness does NOT always equal side the controller is ACTUALLY held on
			// e.g. when using the Striker Mavrik, the left controller is in the blaster's cradle
			// for tracking, but the user can hold the blaster on their right side
			Vector3 posCamSpace = camTrans.InverseTransformPoint(handPos);
			int currSide = posCamSpace.x >= 0 ? 1 : -1;
			if (currSide == _side || Mathf.Abs(posCamSpace.x) < handSwapThresh)
			{
				swapTime = Time.time;
			}
			else
			{
				if (Time.time > swapTime)
				{
					swapTime = Time.time;
					SetSide(currSide);
				}
			}

			Vector3 lookDir = (handPos - camPos).normalized;
			float upLerp = Mathf.Abs(Vector3.Dot(camTrans.forward, Vector3.up));
			Vector3 lookUpDir = Vector3.Lerp(Vector3.up, camTrans.up, upLerp);
			Vector3 offs = Vector3.Cross(lookDir, lookUpDir).normalized * (horizontalOffset * _side);
			transform.position = handPos + offs;
			
			lookDir = (transform.position - camPos).normalized;
			transform.rotation = Quaternion.LookRotation(lookDir, lookUpDir);
		}
	}
}
