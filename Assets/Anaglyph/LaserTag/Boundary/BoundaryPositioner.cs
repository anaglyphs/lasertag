using Anaglyph.XRTemplate;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(9999)]
	public class BoundaryPositioner : MonoBehaviour
	{
		[SerializeField] private float radius = 20;

		private Camera cam;

		// The last recenter point, expressed in tracking-space-local coordinates
		// (flattened to the floor plane). Meta pauses the headset once it moves
		// more than ~20m from here, so the boundary ring is centered on it.
		// Stored local so it stays anchored to the physical space as the tracking
		// space is moved (e.g. during colocation alignment).
		private Vector3 recenterLocalPos = Vector3.zero;

		// The recenter point in world space, for anything else that needs to
		// align to the same center (e.g. the boundary shader's radial pattern).
		public Vector3 RecenterWorldPos => MainXRRig.TrackingSpace.TransformPoint(recenterLocalPos);

		private void Awake()
		{
			cam = Camera.main;
		}

		private void OnEnable()
		{
			if (OVRManager.display != null)
				OVRManager.display.RecenteredPose += OnRecenter;
		}

		private void OnDisable()
		{
			if (OVRManager.display != null)
				OVRManager.display.RecenteredPose -= OnRecenter;
		}

		private void OnRecenter()
		{
			// On recenter the headset's current position becomes the new center
			// of the allowed radius.
			Vector3 camLocalPos = MainXRRig.TrackingSpace.InverseTransformPoint(cam.transform.position);
			recenterLocalPos = new(camLocalPos.x, 0, camLocalPos.z);
		}

		private void LateUpdate()
		{
			Vector3 camLocalPos = MainXRRig.TrackingSpace.InverseTransformPoint(cam.transform.position);
			Vector3 offsetFlat = new(camLocalPos.x - recenterLocalPos.x, 0, camLocalPos.z - recenterLocalPos.z);

			if (offsetFlat.magnitude < 0.01f)
				return;

			Quaternion localRot = Quaternion.LookRotation(offsetFlat, Vector3.up);
			Vector3 localPos = recenterLocalPos + localRot * (Vector3.forward * radius) + Vector3.up * camLocalPos.y;

			Pose boundaryLocalPose = new(localPos, localRot);
			Pose boundaryPose = MainXRRig.TrackingSpace.TransformPose(boundaryLocalPose);

			transform.position = boundaryPose.position;
			transform.rotation = boundaryPose.rotation;
		}
	}
}