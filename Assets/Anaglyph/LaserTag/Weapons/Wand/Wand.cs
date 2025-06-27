using Anaglyph.Lasertag.Logistics;
using Anaglyph.XRTemplate;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Anaglyph.Lasertag.Weapons
{
	public class Wand : MonoBehaviour
	{
		[SerializeField] private GameObject boltPrefab;
		[SerializeField] private Transform emitFromTransform;
		[SerializeField] private double sampleFrequency = 1 / 120.0;
		[SerializeField] private float flickStartDegPerSec = 286f;
		[SerializeField] private float flickStopDegPerSec = 90f;
		[SerializeField] private float minFlickAngle = 70f;
		[SerializeField] private float minViewDotFireVec = 0.5f;
		[SerializeField] private float minTimeBetweenShots = 0.3f;


		private Camera mainCamera;
		private HandedHierarchy handedHierarchy;
		private double timeLastFrame;
		private double timeLastFire;
		private bool flickInProgress;
		private Vector3 flickRotationAxis;
		private Quaternion flickStartRot;

		private TrackedPoseDriver trackedPoseDriver;

		private void Awake()
		{
			handedHierarchy = GetComponentInParent<HandedHierarchy>();
			trackedPoseDriver = GetComponentInParent<TrackedPoseDriver>(true);
			mainCamera = Camera.main;
		}

		private void OnEnable()
		{
			timeLastFrame = OVRPlugin.GetTimeInSeconds();
		}

		private void Update()
		{
			var handedness = handedHierarchy.Handedness;

			if (handedness == InteractorHandedness.None)
				return;

			var node = handedness == InteractorHandedness.Left ?
				OVRPlugin.Node.HandLeft : OVRPlugin.Node.HandRight;

			double time = OVRPlugin.GetTimeInSeconds();
			double timeDelta = time - timeLastFrame;
			int numSamples = (int)(timeDelta / sampleFrequency);

			numSamples = Math.Min(numSamples, 50);
				
			for (int i = 0; i < numSamples; i++) {

				double timeB = timeLastFrame + (i * sampleFrequency);
				double timeA = timeB - sampleFrequency;

				OVRPose ovrPose = OVRPlugin.GetNodePoseStateAtTime(timeA, node).Pose.ToOVRPose();
				Pose poseA = new Pose(ovrPose.position, ovrPose.orientation);

				ovrPose = OVRPlugin.GetNodePoseStateAtTime(timeB, node).Pose.ToOVRPose();
				Pose poseB = new Pose(ovrPose.position, ovrPose.orientation);

				Quaternion deltaRot = poseA.rotation * Quaternion.Inverse(poseB.rotation);

				deltaRot.ToAngleAxis(out float deltaAngle, out Vector3 axis);
				float deltaAngleNoTwist = deltaAngle * Vector3.ProjectOnPlane(axis, transform.forward).magnitude;

				float deltaAngleSpeed = deltaAngleNoTwist / (float)sampleFrequency;

				if (!flickInProgress && deltaAngleSpeed > flickStartDegPerSec)
				{
					flickInProgress = true;
					flickRotationAxis = axis.normalized;
					flickStartRot = poseB.rotation;
				}
				else if (flickInProgress && (deltaAngleSpeed < flickStopDegPerSec))// || Vector3.Dot(flickRotationAxis, axis.normalized) < 0))
				{
					flickInProgress = false;

					if(Vector3.Dot(emitFromTransform.forward, mainCamera.transform.forward) > minViewDotFireVec
						&& Quaternion.Angle(poseB.rotation, flickStartRot) > minFlickAngle
						&& (float)(timeB - timeLastFire) > minTimeBetweenShots)
					{
						timeLastFire = timeB;
						Fire(poseB);
						break;
					}
				}
			}

			

			timeLastFrame = time;
		}

		public void Fire(Pose firePose)
		{
			if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.canFire)
				return;

			// jank
			trackedPoseDriver.transform.localPosition = firePose.position;
			trackedPoseDriver.transform.localRotation = firePose.rotation;

			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, emitFromTransform.position, emitFromTransform.rotation);

			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);
		}
	}
}
