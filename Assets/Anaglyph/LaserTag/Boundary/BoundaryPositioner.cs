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
			var camLocalPos = MainXRRig.TrackingSpace.InverseTransformPoint(camera.transform.position);
			var camLocalPosFlat = new Vector3(camLocalPos.x, 0, camLocalPos.z);

			if (camLocalPosFlat.magnitude < 0.01f)
				return;

			var localRot = Quaternion.LookRotation(camLocalPosFlat, Vector3.up);
			var localPos = localRot * (Vector3.forward * radius) + Vector3.up * camLocalPos.y;

			Pose boundaryLocalPose = new(localPos, localRot);
			var boundaryPose = MainXRRig.TrackingSpace.TransformPose(boundaryLocalPose);

			transform.position = boundaryPose.position;
			transform.rotation = boundaryPose.rotation;
		}
	}
}