using UnityEngine;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(30000)]
    public class OrientWatch : MonoBehaviour
    {
		public Transform watchBandTransform;
		public Transform headTransform;

		private void LateUpdate()
		{
			Vector3 localHeadPos = transform.InverseTransformPoint(headTransform.position);
			Vector3 localHeadPosOnPlane = Vector3.ProjectOnPlane(localHeadPos, Vector3.forward);
			Vector3 globalHeadPosOnPlane = transform.TransformPoint(localHeadPosOnPlane);

		

			//watchBandTransform.rotation = Quaternion.LookRotation(globalHeadPosOnPlane, watchBandTransform) .LookAt(globalHeadPosOnPlane);
		}
	}
}
