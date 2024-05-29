using Unity.XR.CoreUtils;
using UnityEngine;

namespace XRTemplate
{
    public class XROriginFollower : MonoBehaviour
    {
		private Transform xrFloorOffsetTransform;
		private void OnEnable()
		{
			xrFloorOffsetTransform = FindObjectOfType<XROrigin>().CameraFloorOffsetObject.transform;
		}

		private void LateUpdate()
		{
			transform.SetPositionAndRotation(xrFloorOffsetTransform.position, xrFloorOffsetTransform.rotation);
		}

		public void ResetRigPos()
		{
			xrFloorOffsetTransform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
		}
	}
}
