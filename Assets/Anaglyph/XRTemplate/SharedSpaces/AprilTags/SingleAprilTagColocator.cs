using AprilTag;
using Anaglyph.XRTemplate.CameraReader;
using EnvisionCenter.XRTemplate.QuestCV;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class SingleAprilTagColocator : MonoBehaviour, IColocator
	{
		[SerializeField] private Transform tagIndicator;

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

		private void OnDetectTags(IEnumerable<TagPose> poses)
		{
			if (!colocationActive)
				return;

			foreach(TagPose pose in poses)
			{
				float lerp = IsColocated ? iterativeLerp : 1f;
				IsColocated = true;
				Pose tagPose = new Pose(pose.Position, pose.Rotation);

				Matrix4x4 indicatorPose = Matrix4x4.TRS(pose.Position, pose.Rotation, Vector3.one);
				indicatorPose = MainXROrigin.Transform.worldToLocalMatrix * indicatorPose;

				Colocation.LerpTrackingSpace(tagPose, Pose.identity, lerp);

				indicatorPose = MainXROrigin.Transform.localToWorldMatrix * indicatorPose;
				tagIndicator.gameObject.SetActive(true);
				tagIndicator.SetPositionAndRotation(indicatorPose.GetPosition(), indicatorPose.rotation);
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
