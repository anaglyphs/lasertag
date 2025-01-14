using Anaglyph.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	/// <summary>
	/// Transforms VR playspace so that the anchor matches its networked position
	/// </summary>
	[DefaultExecutionOrder(500)]
	[RequireComponent(typeof(NetworkedSpatialAnchor))]
	public class ColocationAnchor : MonoBehaviour
	{
		private static XROrigin rig;
		private static ColocationAnchor activeAnchor;

		[SerializeField] private NetworkedSpatialAnchor networkedAnchor;

		[SerializeField] private float colocateAtDistance = 3;

		private void OnValidate()
		{
			TryGetComponent(out networkedAnchor);
		}

		private void Awake()
		{
			if(rig == null)
				rig = FindFirstObjectByType<XROrigin>();

			OVRManager.display.RecenteredPose += HandleRecenter;
		}

		private void HandleRecenter()
		{
			if(activeAnchor == this)
				CalibrateToAnchor(this);
		}

        private void LateUpdate()
        {
			float distanceFromOrigin = Vector3.Distance(networkedAnchor.transform.position, rig.Camera.transform.position);

			if (distanceFromOrigin < colocateAtDistance || activeAnchor == null)
				CalibrateToAnchor(this);
		}

		public static void CalibrateToAnchor(ColocationAnchor anchor)
		{
			if (anchor == null || anchor == activeAnchor || !anchor.networkedAnchor.Localized)
				return;

			activeAnchor = anchor;

			Matrix4x4 rigMat = Matrix4x4.TRS(rig.transform.position, rig.transform.rotation, Vector3.one);
			NetworkPose anchorOriginalPose = anchor.networkedAnchor.OriginalPoseSync.Value;
			Matrix4x4 desiredMat = Matrix4x4.TRS(anchorOriginalPose.position, anchorOriginalPose.rotation, Vector3.one);
			Matrix4x4 anchorMat = Matrix4x4.TRS(anchor.transform.position, anchor.transform.rotation, Vector3.one);

			// the rig relative to the anchor
			Matrix4x4 rigLocalToAnchor = anchorMat.inverse * rigMat;

			// that relative matrix relative to the desired transform
			Matrix4x4 relativeToDesired = desiredMat * rigLocalToAnchor;

			Vector3 targetRigPos = relativeToDesired.GetPosition();

			Vector3 targetForward = relativeToDesired.MultiplyVector(Vector3.forward);
			targetForward.y = 0;
			targetForward.Normalize();
			Quaternion targetRigRot = Quaternion.LookRotation(targetForward, Vector3.up);

			rig.transform.SetPositionAndRotation(targetRigPos, targetRigRot);
		}
	}
}