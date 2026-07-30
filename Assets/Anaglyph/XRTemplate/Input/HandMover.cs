using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Input
{
	[DefaultExecutionOrder(-9999)]
	public class HandMover : MonoBehaviour
	{
		[SerializeField] private HandSubject handSubject;
		[SerializeField] private bool usePointPose;

		private void Awake()
		{
			if (!handSubject)
				handSubject = GetComponentInParent<HandSubject>();
		}

		private void Update()
		{
			UpdatePosition();
		}

		private void LateUpdate()
		{
			UpdatePosition();
		}

		private void UpdatePosition()
		{
			if (handSubject.Current == null) return;

			Vector3 pos;
			Quaternion rot;

			if (usePointPose)
			{
				pos = handSubject.PointPosition;
				rot = handSubject.PointRotation;
			}
			else
			{
				pos = handSubject.Position;
				rot = handSubject.Rotation;

#if UNITY_EDITOR
				if (!XRSettings.enabled) rot *= Quaternion.Euler(-90, 0, 0);
#endif
			}

			transform.SetLocalPositionAndRotation(pos, rot);
		}
	}
}