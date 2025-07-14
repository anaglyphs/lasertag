using Anaglyph.XRTemplate.CameraReader;
using AprilTag;
using EnvisionCenter.XRTemplate.QuestCV;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using System.Threading.Tasks;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AprilTagColocator : MonoBehaviour, IColocator
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
			NetworkManager manager = NetworkManager.Singleton;

			foreach(TagPose pose in poses)
			{
				float lerp = IsColocated ? iterativeLerp : 1f;
				IsColocated = true;
				Colocation.TransformOrigin(new Pose(pose.Position, pose.Rotation), Pose.identity, true, lerp);

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
