using Anaglyph.XRTemplate.CameraReader;
using AprilTag;
using EnvisionCenter.XRTemplate.QuestCV;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AprilTagColocator : MonoBehaviour, IColocator
	{
		[SerializeField] private GameObject anchorePrefab;
		[SerializeField] private Transform tagIndicator;


		public float lockDistance = 5;
		public float maxHeadSpeed = 2f;
		public float maxHeadAngSpeed = 2f;

		private NetworkManager manager => NetworkManager.Singleton;

		private async void Start() {
			tagIndicator.gameObject.SetActive(false);
			await EnsureConfigured();
		}

		private async Task EnsureConfigured() {
			if(!CameraManager.Instance.IsConfigured)
				await CameraManager.Instance.Configure(1, 320, 240);
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

		public float tagSize = 0.1f;
		public float iterativeLerp = 0.01f;
		private bool colocationActive = false;

		private bool CheckIfSessionOwner() => manager.CurrentSessionOwner == manager.LocalClientId;

		public async void Colocate()
		{
			IsColocated = false;
			colocationActive = true;

			await EnsureConfigured();
			await CameraManager.Instance.TryOpenCamera();
			AprilTagTracker.Instance.tagSizeMeters = tagSize;
			AprilTagTracker.Instance.OnDetectTags += OnDetectTags;

			tagIndicator.localScale = Vector3.one * tagSize;
		}

		private static Pose LerpPose(Pose old, Pose target, float lerp)
		{
			Vector3 pos = Vector3.Lerp(old.position, target.position, lerp);
			Quaternion rot = Quaternion.Lerp(old.rotation, target.rotation, lerp);

			return new(pos, rot);
		}

		private void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			if (!colocationActive)
				return;

			double timestamp = AprilTagTracker.Instance.FrameTimestamp;

			OVRPlugin.PoseStatef poseState = OVRPlugin.GetNodePoseStateAtTime(timestamp, OVRPlugin.Node.Head);
			var ovrAV = poseState.AngularVelocity;
			var ovrV = poseState.Velocity;
			Vector3 angVel = new(ovrAV.x, ovrAV.y, ovrAV.z);
			Vector3 vel = new(ovrV.x, ovrV.y, ovrV.z);

			if (vel.magnitude > maxHeadSpeed || angVel.magnitude > maxHeadAngSpeed)
				return;

			bool isSessionOwner = manager.CurrentSessionOwner == manager.LocalClientId;
			var allAnchors = AprilTagAnchor.AllAnchors;

			foreach (TagPose result in results)
			{
				bool foundAnchor = allAnchors.TryGetValue(result.ID, out var anchor);

				Pose tagPose = new(result.Position, result.Rotation);

				if (foundAnchor)
				{
					anchor.transform.localScale = Vector3.one * tagSize;
					anchor.transform.SetWorldPose(tagPose);

					if (!anchor.IsLocked && anchor.IsOwner)
					{
						anchor.desiredPoseSync.Value = new(LerpPose(anchor.DesiredPose, tagPose, iterativeLerp));
					}
					else
					{
						Matrix4x4 localToTrackingSpace = MainXROrigin.Transform.worldToLocalMatrix * anchor.transform.localToWorldMatrix;
						Colocation.LerpTrackingSpace(anchor.GetPose(), anchor.DesiredPose, iterativeLerp / results.Count);
						Matrix4x4 global = MainXROrigin.Transform.localToWorldMatrix * localToTrackingSpace;
						anchor.transform.position = global.GetPosition();
						anchor.transform.rotation = global.rotation;
					}

					IsColocated = true;
				}
				else if (isSessionOwner)
				{
					var anchorObj = Instantiate(anchorePrefab);

					anchor = anchorObj.GetComponent<AprilTagAnchor>();

					anchor.idSync.Value = result.ID;
					anchor.transform.SetWorldPose(tagPose);
					anchor.transform.localScale = Vector3.one * tagSize;
					anchor.desiredPoseSync.Value = new(tagPose);

					anchor.NetworkObject.SpawnWithOwnership(manager.LocalClientId);

					IsColocated = true;
				}
			}

			foreach (AprilTagAnchor anchor in allAnchors.Values)
			{
				if (anchor.IsOwner && !anchor.IsLocked)
				{
					Vector3 anchorPos = anchor.transform.position;
					Vector3 headPos = MainXROrigin.Instance.Camera.transform.position;

					if (Vector3.Distance(anchorPos, headPos) > lockDistance)
						anchor.isLockedSync.Value = true;
				}
			}
		}

		public void StopColocation()
		{
			CameraManager.Instance.CloseCamera();
			AprilTagTracker.Instance.OnDetectTags -= OnDetectTags;

			tagIndicator.gameObject.SetActive(false);

			colocationActive = false;
			IsColocated = false;
		}
	}
}
