using Anaglyph.Netcode;
using Anaglyph.XRTemplate.CameraReader;
using Anaglyph.XRTemplate.QuestCV;
using AprilTag;
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

		private const float Lerp = 0.1f;

		private bool colocationActive = false;

		public static Dictionary<int, AprilTagAnchor> foundTags = new();

		private GameObject tagAnchorPrefab;

		public void Colocate()
		{
			IsColocated = false;
			colocationActive = true;

			CameraManager.Instance.Configure(0, 320, 240);
			CameraManager.Instance.StartCapture();
			AprilTagTracker.Instance.OnDetectTags += OnDetectTags;
		}

		private void OnDetectTags(IReadOnlyList<TagPose> poses)
		{
			NetworkManager manager = NetworkManager.Singleton;

			foreach(TagPose pose in poses)
			{
				bool tagWasFound = foundTags.ContainsKey(pose.ID);

				if(tagWasFound)
				{
					var foundTag = foundTags[pose.ID];

					Colocation.LerpTrackingSpace(new(pose.Position, pose.Rotation), foundTag.DesiredPose, Lerp / poses.Count);
					foundTag.transform.SetPositionAndRotation(pose.Position, pose.Rotation);

					IsColocated = true;

				} else if(manager.IsHost)
				{
					if(tagAnchorPrefab == null)
						tagAnchorPrefab = Resources.Load<GameObject>("April Tag Anchor");

					GameObject g = GameObject.Instantiate(tagAnchorPrefab, pose.Position, pose.Rotation);
					g.TryGetComponent(out AprilTagAnchor anchor);
					anchor.transform.localScale = Vector3.one * AprilTagTracker.Instance.tagSizeMeters;
					anchor.desiredPoseSync.Value = new NetworkPose(pose.Position, pose.Rotation);
					anchor.idSync.Value = pose.ID;
					anchor.NetworkObject.Spawn();

				}
			}
		}

		public void StopColocation()
		{
			foundTags.Clear();

			CameraManager.Instance.StopCapture();
			AprilTagTracker.Instance.OnDetectTags -= OnDetectTags;

			colocationActive = false;
			IsColocated = false;
		}
	}
}
