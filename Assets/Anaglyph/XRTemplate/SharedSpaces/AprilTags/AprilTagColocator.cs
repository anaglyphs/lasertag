using Anaglyph.XRTemplate.CameraReader;
using AprilTag;
using EnvisionCenter.XRTemplate.QuestCV;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using Unity.Mathematics;

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

		private void OnDetectTags(IEnumerable<TagPose> results)
		{
			if (!colocationActive)
				return;

			bool isSessionOwner = manager.CurrentSessionOwner == manager.LocalClientId;

			foreach (TagPose result in results)
			{
				var id = result.ID;
				var pos = result.Position;
				var rot = result.Rotation;

				AprilTagAnchor anchor = AprilTagAnchor.AllAnchors[id];

				if (anchor == null)
				{
					if (isSessionOwner)
					{
						var anchorObj = Instantiate(anchorePrefab, pos, rot);

						anchor = anchorObj.GetComponent<AprilTagAnchor>();
						anchor.idSync.Value = id;
						anchor.desiredPoseSync.Value = new(pos, rot);

						anchor.NetworkObject.Spawn();
					}
					else continue;
				}
				else
				{
					anchor.transform.position = pos;
					anchor.transform.rotation = rot;

					if (anchor.IsOwner && !anchor.IsLocked)
					{
						Pose oldPose = anchor.DesiredPose;
						Pose targetPose = new(pos, rot);

						Pose newPose = LerpPose(oldPose, targetPose, iterativeLerp);

						anchor.desiredPoseSync.Value = new(newPose);
					}
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
