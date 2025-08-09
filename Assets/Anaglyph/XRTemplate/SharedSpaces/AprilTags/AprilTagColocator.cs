using Anaglyph.XRTemplate.CameraReader;
using AprilTag;
using EnvisionCenter.XRTemplate.QuestCV;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AprilTagColocator : MonoBehaviour, IColocator
	{
		[SerializeField] private GameObject anchorePrefab;
		[SerializeField] private Transform tagIndicator;

		public float lockDistance = 5;

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

		private void Update()
		{
			Vector3 desiredCentroid = Vector3.zero;
			Vector3 localCentroid = Vector3.zero;

			if (!colocationActive)
				return;

			foreach (AprilTagAnchor anchor in AprilTagAnchor.AllAnchors.Values)
			{
				if (anchor.IsOwner && !anchor.IsLocked)
				{
					Vector3 anchorPos = anchor.transform.position;
					Vector3 headPos = MainXROrigin.Instance.Camera.transform.position;

					if (Vector3.Distance(anchorPos, headPos) > lockDistance)
						anchor.isLockedSync.Value = true;
				}

				desiredCentroid += anchor.DesiredPose.position;
				localCentroid += anchor.transform.position;
			}
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

			bool isSessionOwner = manager.CurrentSessionOwner == manager.LocalClientId;

			foreach (TagPose result in results)
			{
				bool foundAnchor = AprilTagAnchor.AllAnchors.TryGetValue(result.ID, out var anchor);

				if (!foundAnchor)
				{
					if (isSessionOwner)
					{
						var anchorObj = Instantiate(anchorePrefab);

						anchor = anchorObj.GetComponent<AprilTagAnchor>();
						anchor.idSync.Value = result.ID;
						anchor.desiredPoseSync.Value = new(result.Position, result.Rotation);

						anchor.NetworkObject.Spawn();
					}
					else continue;
				}

				anchor.transform.position = result.Position;
				anchor.transform.rotation = result.Rotation;
				anchor.transform.localScale = Vector3.one * tagSize;

				if (anchor.IsOwner && !anchor.IsLocked)
				{
					Pose oldPose = anchor.DesiredPose;
					Pose targetPose = anchor.GetPose();
					Pose newPose = LerpPose(oldPose, targetPose, iterativeLerp);

					anchor.desiredPoseSync.Value = new(newPose);
				}
				else
				{
					Colocation.LerpTrackingSpace(anchor.GetPose(), anchor.DesiredPose, iterativeLerp / results.Count);
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
