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

		public static void TransformTrackingSpace(Pose fromPose, Pose toPose)
		{
			Transform r = MainXROrigin.Transform;
			Matrix4x4 rigMat = Matrix4x4.TRS(r.position, r.rotation, Vector3.one);
			Matrix4x4 desiredMat = Matrix4x4.TRS(toPose.position, toPose.rotation, Vector3.one);
			Matrix4x4 anchorMat = Matrix4x4.TRS(fromPose.position, fromPose.rotation, Vector3.one);

			// the rig relative to the anchor
			Matrix4x4 rigLocalToAnchor = anchorMat.inverse * rigMat;

			// that relative matrix relative to the desired transform
			Matrix4x4 relativeToDesired = desiredMat * rigLocalToAnchor;

			Vector3 targetRigPos = relativeToDesired.GetPosition();

			Vector3 targetForward = relativeToDesired.MultiplyVector(Vector3.forward);

			targetForward.y = 0;
			targetForward.Normalize();
			Quaternion targetRigRot = Quaternion.LookRotation(targetForward, Vector3.up);

			MainXROrigin.Transform.SetPositionAndRotation(targetRigPos, targetRigRot);
		}

		public static void LerpTrackingSpace(Pose fromPose, Pose toPose, float lerp)
		{
			Pose lerpedPose = new Pose();
			lerpedPose.position = Vector3.Lerp(fromPose.position, toPose.position, lerp);
			lerpedPose.rotation = Quaternion.Lerp(fromPose.rotation, toPose.rotation, lerp);

			TransformTrackingSpace(fromPose, lerpedPose);
		}
	}
}
