using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(-999)]
	public class MainXRRig : MonoBehaviour
	{
		public static MainXRRig Instance { get; private set; }

		public new Camera camera;
		public Transform trackingSpace;

		public static Camera Camera => Instance.camera;
		public static Transform TrackingSpace => Instance.trackingSpace;
		
		public static event Action Recentered = delegate { };
		private readonly List<XRInputSubsystem> xrSubsystems = new();

		private void Awake()
		{
			Instance = this;
			
			SubsystemManager.GetSubsystems(xrSubsystems);
			foreach (XRInputSubsystem sub in xrSubsystems)
				sub.trackingOriginUpdated += HandleRecenter;
		}

		private void OnDestroy()
		{
			foreach (XRInputSubsystem sub in xrSubsystems)
				sub.trackingOriginUpdated -= HandleRecenter;
		}
		
		private void HandleRecenter(XRInputSubsystem obj)
		{
			Recentered.Invoke();
		}

		public void ForceGlobalUp()
		{
			if (Vector3.Angle(transform.up, Vector3.up) < 0.1f)
				return;

			Quaternion r = transform.rotation;
			Quaternion flatRot = r.Flatten();
			Quaternion delta = r.Inverse() * flatRot;
			transform.RotateAroundPoint(camera.transform.position, delta);
		}

		public void AlignSpace(Matrix4x4 current, Matrix4x4 target, float lerp = 1f, bool forceGlobalUp = true)
		{
			Transform t = TrackingSpace;

			Matrix4x4 rigMat = t.localToWorldMatrix;
			Matrix4x4 targetMat = target * current.inverse * rigMat;

			Vector3 targetPos = targetMat.GetPosition();
			Quaternion targetRot = targetMat.rotation;
			Vector3 rigPos = t.position;
			Quaternion rigRot = t.rotation;

			t.position = Vector3.Lerp(rigPos, targetPos, lerp);
			t.rotation = Quaternion.Slerp(rigRot, targetRot, lerp);

			if (forceGlobalUp)
				ForceGlobalUp();
		}

		public void ShiftSpace(Matrix4x4 shift)
		{
			Transform t = TrackingSpace;

			Matrix4x4 rigMat = t.localToWorldMatrix;
			Matrix4x4 targetMat = shift * rigMat;

			t.position = targetMat.GetPosition();
			t.rotation = targetMat.rotation;
		}
	}
}