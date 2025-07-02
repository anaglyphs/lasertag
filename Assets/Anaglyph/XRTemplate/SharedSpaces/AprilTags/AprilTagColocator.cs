using Anaglyph.XRTemplate.CameraReader;
using AprilTag;
using EnvisionCenter.XRTemplate.QuestCV;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AprilTagColocator : MonoBehaviour, IColocator
	{
		[SerializeField] private Transform tagIndicator;

		private void Start() {
			tagIndicator.gameObject.SetActive(false);
		}

		private static bool _isColocated;
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
		private const float Lerp = 0.1f;
		private bool colocationActive = false;

		public async void Colocate()
		{
			IsColocated = false;
			colocationActive = true;

			await CameraManager.Instance.Configure(0, 320, 240);
			await CameraManager.Instance.TryOpenCamera();
			AprilTagTracker.Instance.tagSizeMeters = tagSize;
			AprilTagTracker.Instance.OnDetectTags += OnDetectTags;

			tagIndicator.localScale = Vector3.one * tagSize;
		}

		private void OnDetectTags(IEnumerable<TagPose> poses)
		{
			NetworkManager manager = NetworkManager.Singleton;

			foreach(TagPose pose in poses)
			{
				if(!IsColocated)
					Colocation.TransformTrackingSpace(new Pose(pose.Position, pose.Rotation), Pose.identity);

				IsColocated = true;
				Colocation.LerpTrackingSpace(new Pose(pose.Position, pose.Rotation), Pose.identity, Lerp);

				tagIndicator.gameObject.SetActive(true);
				tagIndicator.SetPositionAndRotation(pose.Position, pose.Rotation);
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
