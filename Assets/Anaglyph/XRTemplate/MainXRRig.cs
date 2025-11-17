using System;
using UnityEngine;

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

		private void Awake()
		{
			Instance = this;
		}

		private void LateUpdate()
		{
			ForceGlobalUp();
		}

		public void ForceGlobalUp()
		{
			var a = transform.eulerAngles;
			if (a.x == 0 || a.z == 0)
				return;

			var r = transform.rotation;
			var flatRot = r.Flatten();
			var delta = r.Inverse() * flatRot;
			transform.RotateAroundPoint(camera.transform.position, delta);
		}

		public void AlignSpace(Matrix4x4 current, Matrix4x4 target)
		{
			var t = TrackingSpace;

			var rigMat = t.localToWorldMatrix;
			var rel = target.inverse * current;
			rigMat = rel * rigMat;

			t.position = rigMat.GetPosition();
			t.rotation = rigMat.rotation;

			ForceGlobalUp();
		}
	}
}