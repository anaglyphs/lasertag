using Anaglyph.XR;
using Anaglyph.XR.Input;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.LaserTag.Interface.HUD
{
	public class HandHUDPositioner : MonoBehaviour
	{
		[SerializeField] public float horizontalOffset = 0.15f;
		[SerializeField] private float handSwapTime = 0.3f;
		[SerializeField] private float handSwapThresh = 0.02f;

		private Camera mainCamera;
		private HandInput follow;

		private int side;
		private float swapTimer = float.MaxValue;

		private void OnEnable()
		{
			mainCamera = Camera.main;
			
			InputDevices.deviceConnected += OnDeviceEvent;
			InputDevices.deviceDisconnected += OnDeviceEvent;
			if(didStart) FindController();
		}

		private void Start()
		{
			FindController();
		}

		private void OnDisable()
		{
			InputDevices.deviceConnected -= OnDeviceEvent;
			InputDevices.deviceDisconnected -= OnDeviceEvent;
		}

		private void OnDeviceEvent(InputDevice obj)
		{
			FindController();
		}

		private void FindController()
		{
			foreach (HandInput handInput in HandInput.Hands.Values)
			{
				if (handInput.IsTracking)
				{
					follow = handInput;
					break;
				}
			}
		}

		private void LateUpdate()
		{
			if (follow == null || !follow.IsTracking) return;

			Transform camTrans = mainCamera.transform;
			Vector3 camPos = camTrans.position;

			Vector3 pos = follow.Position;
			pos = MainXRRig.TrackingSpace.TransformPoint(pos);

			// determine side
			// controller handedness DOES NOT always equal side the controller is ACTUALLY on
			// e.g. when using the Striker Mavrik, the left controller is in the blaster's cradle
			// for tracking, but the user can hold the blaster on their right side
			Vector3 posCamSpace = camTrans.InverseTransformPoint(pos);
			int currSide = posCamSpace.x >= 0 ? -1 : 1;
			bool farEnough = Mathf.Abs(posCamSpace.x) > handSwapThresh;
			if (farEnough && currSide != side)
			{
				swapTimer += Time.deltaTime;
				if (swapTimer > handSwapTime)
					side = currSide;
			}
			else
			{
				swapTimer = 0;
			}
			
			Vector3 camToHand = (pos - camPos).normalized * side;
			Vector3 offs = Vector3.Cross(camTrans.up, camToHand);
			offs.y = 0;
			offs = offs.normalized * horizontalOffset;
			transform.position = pos + offs;

			Vector3 lookDir = (transform.position - camPos).normalized;
			Vector3 camForw = camTrans.forward;
			float upLerp = Mathf.Abs(Vector3.Dot(camForw, Vector3.up));
			Vector3 lookUpDir = Vector3.Lerp(Vector3.up, camTrans.up, upLerp);

			Quaternion rot = Quaternion.LookRotation(lookDir, lookUpDir);

			transform.rotation = rot;
		}
	}
}
