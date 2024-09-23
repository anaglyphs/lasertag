using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	/// <summary>
	/// Transforms VR playspace so that the anchor matches its networked position
	/// </summary>
	[DefaultExecutionOrder(500)]
	public class CalibrateToNetworkedAnchors : MonoBehaviour
	{
		private XROrigin rig;

		[SerializeField] private Transform closestIndicator;
		[SerializeField] private float maxDistanceToAnchorTo = 3;

		private NetworkedSpatialAnchor anchoredTo = null;
		private bool shouldRecalibrateNextUpdate;

		private void Awake()
		{
			rig = FindObjectOfType<XROrigin>();
			OVRManager.display.RecenteredPose += HandleRecenter;
		}

		private void HandleRecenter()
		{
			shouldRecalibrateNextUpdate = true;

			Debug.Log("Recenter detected. Calibrating to anchor");
		}

		private void Update()
		{
			bool didFindAnchor = TryFindClosestAnchor(out var foundAnchor);

            if (didFindAnchor && anchoredTo != foundAnchor)
            {
				CalibrateToAnchor(foundAnchor);
			}
		}

        private void LateUpdate()
        {
            if (shouldRecalibrateNextUpdate)
            {
                CalibrateToAnchor(anchoredTo);
            }
        }

        private bool TryFindClosestAnchor(out NetworkedSpatialAnchor foundAnchor)
		{
			foundAnchor = null;

			float closestDistance = Mathf.Infinity;

			foreach (NetworkedSpatialAnchor anchor in NetworkedSpatialAnchor.allLocalizedAnchorManagers)
			{
				float distanceFromOrigin = Vector3.Distance(anchor.AttachedSpatialAnchor.transform.position, rig.Camera.transform.position);

				if (distanceFromOrigin < closestDistance)
				{
					foundAnchor = anchor;
					closestDistance = distanceFromOrigin;
				}
			}

			return foundAnchor != null;
		}

		public void CalibrateToAnchor(NetworkedSpatialAnchor anchor)
		{
			if (anchor == null)
				return;

			Matrix4x4 rigMat = Matrix4x4.TRS(rig.transform.position, rig.transform.rotation, Vector3.one);
			Matrix4x4 desiredMat = Matrix4x4.TRS(anchor.transform.position, anchor.transform.rotation, Vector3.one);
			Matrix4x4 anchorMat = Matrix4x4.TRS(anchor.AttachedSpatialAnchor.transform.position, anchor.AttachedSpatialAnchor.transform.rotation, Vector3.one);

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

			if (closestIndicator != null)
			{
				closestIndicator.SetPositionAndRotation(anchor.transform.position, anchor.transform.rotation);
			}
		}
	}
}