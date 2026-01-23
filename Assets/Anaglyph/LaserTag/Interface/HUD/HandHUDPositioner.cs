using Anaglyph.XRTemplate;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
    public class HandHUDPositioner : MonoBehaviour
    {
	    [SerializeField] public float horizontalOffset = 0.15f;
	    [SerializeField] private float handSwapTime = 0.3f;
	    [SerializeField] private float handSwapThresh = 0.02f;
	    
	    private Camera mainCamera;
	    private InputDevice follow;

	    private enum HandSide
	    {
		    Left = -1,
		    None = 0,
		    Right = 1
	    }

	    private HandSide side = HandSide.None;
	    private float swapTimer = float.MaxValue;
	    
	    private readonly XRNode[] nodesToCheck = { XRNode.RightHand, XRNode.LeftHand };
	    
	    private void OnEnable()
	    {
		    mainCamera = Camera.main;

		    InputDevices.deviceConnected += OnDeviceEvent;
		    InputDevices.deviceDisconnected += OnDeviceEvent;
		    FindController();
	    }

	    private void OnDisable()
	    {
		    InputDevices.deviceConnected -= OnDeviceEvent;
		    InputDevices.deviceDisconnected -= OnDeviceEvent;
	    }

	    private void OnDeviceEvent(InputDevice obj) => FindController();

	    private void FindController()
	    {
		    foreach (var node in nodesToCheck)
		    {
			    var controller = InputDevices.GetDeviceAtXRNode(node);
			    if (controller.isValid)
			    {
				    follow = controller;
				    break;
			    }
		    }
	    }

	    private void LateUpdate()
	    {
		    if (!follow.isValid) return;
		    
		    var camTrans = mainCamera.transform;
		    var camPos = camTrans.position;

		    var chars = follow.characteristics;

		    if (chars.HasFlag(InputDeviceCharacteristics.HeldInHand 
		                      & InputDeviceCharacteristics.TrackedDevice))
		    {
			    Vector3 pos = Vector3.zero;
			    follow.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
			    pos = MainXRRig.TrackingSpace.TransformPoint(pos);
			    
			    // determine side
			    Vector3 posCamSpace = camTrans.InverseTransformPoint(pos);
			    HandSide currSide = posCamSpace.x >= 0 ? HandSide.Left : HandSide.Right;
			    bool farEnough = Mathf.Abs(posCamSpace.x) > handSwapThresh;
			    if (farEnough && currSide != side)
			    {
				    swapTimer += Time.deltaTime;
				    if (swapTimer > handSwapTime)
					    side = currSide;
			    }
			    else
				    swapTimer = 0;

			    Vector3 camToHand = (pos - camPos).normalized * (int)side;
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
}
