using Anaglyph.XRTemplate;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
    public class HandHUDPositioner : MonoBehaviour
    {
	    [SerializeField] public float horizontalOffset = 0.15f;
	    
	    private Camera mainCamera;
	    private InputDevice follow;
	    
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

			    bool isRight = chars.HasFlag(InputDeviceCharacteristics.Right);
			    Vector3 handToCam = (camPos - pos).normalized * (isRight ? -1 : 1);
			    Vector3 offs = Vector3.Cross(handToCam, camTrans.up);
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
