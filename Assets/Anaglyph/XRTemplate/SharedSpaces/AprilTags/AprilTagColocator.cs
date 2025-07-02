using Anaglyph.XRTemplate.CameraReader;
using AprilTag;
using EnvisionCenter.XRTemplate.QuestCV;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AprilTagColocator : IColocator
	{
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
		}

		private void OnDetectTags(IEnumerable<TagPose> poses)
		{
			NetworkManager manager = NetworkManager.Singleton;

			foreach(TagPose pose in poses)
			{
				Colocation.LerpTrackingSpace(new Pose(pose.Position, pose.Rotation), Pose.identity, Lerp);
			}
		}

		public void StopColocation()
		{
			CameraManager.Instance.CloseCamera();
			AprilTagTracker.Instance.OnDetectTags -= OnDetectTags;

			colocationActive = false;
			IsColocated = false;
		}
	}
}
