using AprilTag;
using Anaglyph.XRTemplate.DeviceCameras;
using Anaglyph.XRTemplate.AprilTags;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class DualTagColocator : MonoBehaviour, IColocator
	{
		[SerializeField] private float tagLerp = 0.1f;
        
        [Tooltip("In meters/second")]
        public float maxHeadSpeed = 2f;
        [Tooltip("In radians/second")]
        public float maxHeadAngSpeed = 2f;

        public float tagSize = 0.1f;
        private bool colocationActive = false;
		public bool ColocationActive => colocationActive;

		[SerializeField] private CameraReader cameraReader;
		[SerializeField] private AprilTagTracker tagTracker;

		public CameraReader CameraReader => cameraReader;
		public AprilTagTracker TagTracker => tagTracker;

        private Vector3 originLocalPos = Vector3.zero;
        private Vector3 directionLocalPos = Vector3.zero; 

		private void Awake()
		{
			cameraReader = FindAnyObjectByType<CameraReader>();
			tagTracker = FindAnyObjectByType<AprilTagTracker>();
		}

		private bool _isColocated;
		public event Action<bool> IsColocatedChange;

        public bool IsColocated
        {
            get => _isColocated;
            private set
            {
                bool changed = value != _isColocated;
                _isColocated = value;
                if (changed)
                    IsColocatedChange?.Invoke(_isColocated);
            }
        }

        public async void Colocate()
        {
            originLocalPos = Vector3.zero;
            directionLocalPos = Vector3.zero;
            
            IsColocated = false;
            colocationActive = true;

            await cameraReader.TryOpenCamera();
            tagTracker.tagSizeMeters = tagSize;
            tagTracker.OnDetectTags += OnDetectTags;
        }

        private void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			if (!colocationActive)
				return;
            
            var headState = OVRPlugin.GetNodePoseStateAtTime(tagTracker.FrameTimestamp, OVRPlugin.Node.Head);
            var v = headState.Velocity;
            Vector3 vel = new(v.x, v.y, v.z);
            float headSpeed = vel.magnitude;
            var av = headState.AngularVelocity;
            Vector3 angVel = new(av.x, av.y, av.z);
            float angHeadSpeed = angVel.magnitude;

            bool headIsStable = headSpeed < maxHeadSpeed && angHeadSpeed < maxHeadAngSpeed;

            if (!headIsStable)
                return;

			foreach (TagPose result in results)
            {
                int id = result.ID;
                
                if(id != 0 && id != 1)
                    continue;
                
				Vector3 globalPos = result.Position;

				Matrix4x4 worldToTracking = MainXRRig.TrackingSpace.worldToLocalMatrix;
                Vector3 localPos = worldToTracking.MultiplyPoint(globalPos);
                
                if(id == 0)
                    originLocalPos = localPos;
                else
                    directionLocalPos = localPos;
                
                if(originLocalPos == Vector3.zero || directionLocalPos == Vector3.zero)
                    continue;
                
                Vector3 localForward = directionLocalPos - originLocalPos;
                localForward.y = 0;
                localForward = localForward.normalized;

                Quaternion rot = Quaternion.LookRotation(localForward, Vector3.up);
                
                Matrix4x4 tfLocal = Matrix4x4.TRS(originLocalPos, rot, Vector3.one);
                Matrix4x4 trackingToWorld = MainXRRig.TrackingSpace.localToWorldMatrix;
                var tfGlobal = trackingToWorld * tfLocal;

                Pose pose = new(tfGlobal.GetPosition(), tfGlobal.rotation);

                float l = tagLerp;

                if (!IsColocated)
                    l = 1.0f;
                
                MainXRRig.LerpPoseToTarget(pose, Pose.identity, l);
                IsColocated = true;
            }
		}

		public void StopColocation()
		{
            originLocalPos = Vector3.zero;
            directionLocalPos = Vector3.zero;
            
			cameraReader.CloseCamera();
			tagTracker.OnDetectTags -= OnDetectTags;

			colocationActive = false;
			IsColocated = false;
		}
	}
}
