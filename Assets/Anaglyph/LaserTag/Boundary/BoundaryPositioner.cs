using System;
using Anaglyph.XRTemplate;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(9999)]
    public class BoundaryPositioner : MonoBehaviour
    {
		[SerializeField] private float radius = 20;

		private new Camera camera;

		private void Awake()
		{
			camera = Camera.main;
		}

		private void LateUpdate()
		{
			Vector3 camLocalPos = MainXRRig.TrackingSpace.InverseTransformPoint(camera.transform.position);
			Vector3 camLocalPosFlat = new Vector3(camLocalPos.x, 0, camLocalPos.z);

			if (camLocalPosFlat.magnitude == 0)
				return;

			Quaternion localRot = Quaternion.LookRotation(camLocalPosFlat, Vector3.up);
			Vector3 localPos = (localRot * (Vector3.forward * radius)) + (Vector3.up * camLocalPos.y);

			Pose boundaryLocalPose = new(localPos, localRot);
			Pose boundaryPose = MainXRRig.TrackingSpace.TransformPose(boundaryLocalPose);

			transform.position = boundaryPose.position;
			transform.rotation = boundaryPose.rotation;
		}
	}
}
