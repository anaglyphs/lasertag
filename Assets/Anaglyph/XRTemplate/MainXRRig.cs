using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class MainXRRig : MonoBehaviour
	{
		private static MainXRRig Instance;

		public new Camera camera;
		public Transform trackingSpace;

		public static Camera Camera => Instance.camera;
		public static Transform TrackingSpace => Instance.trackingSpace;

		private void Awake()
		{
			Instance = this;
		}

		/// <summary>
		/// Transforms the rig so that `current` becomes `desired`
		/// </summary>
		/// <param name="current"></param>
		/// <param name="target"></param>
		/// <param name="enforceUp"></param>
		/// <param name="flattenUp"></param>
		public static void MatchPoseToTarget(Pose current, Pose target, bool enforceUp = true, bool flattenUp = false)
		{
			var root = MainXRRig.TrackingSpace;

			if (enforceUp)
			{
				current = FlattenPoseRotation(current, flattenUp);
				target = FlattenPoseRotation(target, flattenUp);
			}

			Matrix4x4 rigMat = Matrix4x4.TRS(root.position, root.rotation, Vector3.one);
			Matrix4x4 localMat = Matrix4x4.TRS(current.position, current.rotation, Vector3.one);
			Matrix4x4 targetMat = Matrix4x4.TRS(target.position, target.rotation, Vector3.one);

			Matrix4x4 rigLocalToAnchor = localMat.inverse * rigMat;
			Matrix4x4 relativeToDesired = targetMat * rigLocalToAnchor;

			Vector3 targetRigPos = relativeToDesired.GetPosition();

			root.SetPositionAndRotation(relativeToDesired.GetPosition(), relativeToDesired.rotation);
		}

		public static void LerpPoseToTarget(Pose current, Pose target, float lerp, bool enforceUp = true, bool flattenUp = false)
		{

			Pose lerpedPose = new Pose();
			lerpedPose.position = Vector3.Lerp(current.position, target.position, lerp);
			lerpedPose.rotation = Quaternion.Lerp(current.rotation, target.rotation, lerp);

			MatchPoseToTarget(current, lerpedPose, enforceUp, flattenUp);
		}

		public static void MatchPoseToIdentity(Pose fromPose)
 => MatchPoseToTarget(fromPose, new Pose(Vector3.zero, Quaternion.identity));

		private static Pose FlattenPoseRotation(Pose pose, bool flattenUp = false)
		{
			Vector3 forward = pose.rotation * Vector3.forward;

			if (flattenUp && Mathf.Abs(forward.y) > Mathf.Sqrt(2) / 2f)
				forward = pose.rotation * Vector3.up;

			forward.y = 0f;

			if (forward == Vector3.zero)
				return new Pose(pose.position, Quaternion.identity); // Fallback if rotation is vertical

			Quaternion yOnlyRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
			return new Pose(pose.position, yOnlyRotation);
		}
	}
}