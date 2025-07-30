using System;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public interface IColocator
	{
		public bool IsColocated { get; }
		public event Action<bool> IsColocatedChange;

		public void Colocate();
		public void StopColocation();
	}

	public static class Colocation
	{
		private static IColocator activeColocator;
		public static IColocator ActiveColocator => activeColocator;

		private static bool _isColocated;
		public static event Action<bool> IsColocatedChange;
		private static void SetIsColocated(bool b) => IsColocated = b;
		public static bool IsColocated
		{
			get => _isColocated;
			set
			{
				bool changed = value != _isColocated;
				_isColocated = value;
				if (changed)
					IsColocatedChange?.Invoke(_isColocated);
			}
		}

		public static void SetActiveColocator(IColocator colocator)
		{
			if (activeColocator != null)
			{
				activeColocator.IsColocatedChange -= SetIsColocated;
			}

			activeColocator = colocator;

			if (activeColocator != null)
			{
				IsColocated = activeColocator.IsColocated;
				activeColocator.IsColocatedChange += SetIsColocated;
			}
		}

		public static void TransformTrackingSpace(Pose fromPose)
		 => TransformTrackingSpace(fromPose, new Pose(Vector3.zero, Quaternion.identity));

		public static Pose FlattenPoseRotation(Pose pose)
		{
			Vector3 forward = pose.rotation * Vector3.forward;
			forward.y = 0f;

			if (forward == Vector3.zero)
				return new Pose(pose.position, Quaternion.identity); // Fallback if rotation is vertical

			Quaternion yOnlyRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
			return new Pose(pose.position, yOnlyRotation);
		}

		public static void TransformTrackingSpace(Pose from, Pose to, bool enforceUp = true)
		{
			var root = MainXROrigin.Transform;

			if(enforceUp)
			{
				from = FlattenPoseRotation(from);
				to = FlattenPoseRotation(to);
			}

			Matrix4x4 rigMat = Matrix4x4.TRS(root.position, root.rotation, Vector3.one);
			Matrix4x4 anchorMat = Matrix4x4.TRS(from.position, from.rotation, Vector3.one);
			Matrix4x4 desiredMat = Matrix4x4.TRS(to.position, to.rotation, Vector3.one);

			//if (enforceUp)
			//{
			//	Vector3 f = anchorMat.MultiplyVector(Vector3.forward);
			//	Vector3 flatForward = new Vector3(f.x, 0, f.z).normalized;
			//	Quaternion correctedRot = Quaternion.LookRotation(flatForward, Vector3.up);
			//	anchorMat = Matrix4x4.TRS(anchorMat.GetPosition(), correctedRot, Vector3.one);
			//}

			// the rig relative to the anchor
			Matrix4x4 rigLocalToAnchor = anchorMat.inverse * rigMat;

			// that relative matrix relative to the desired transform
			Matrix4x4 relativeToDesired = desiredMat * rigLocalToAnchor;

			Vector3 targetRigPos = relativeToDesired.GetPosition();

			root.SetPositionAndRotation(relativeToDesired.GetPosition(), relativeToDesired.rotation);
		}

		public static void LerpTrackingSpace(Pose from, Pose to, float lerp, bool enforceUp = true)
		{

			Pose lerpedPose = new Pose();
			lerpedPose.position = Vector3.Lerp(from.position, to.position, lerp);
			lerpedPose.rotation = Quaternion.Lerp(from.rotation, to.rotation, lerp);

			TransformTrackingSpace(from, lerpedPose, enforceUp);
		}
	}
}
