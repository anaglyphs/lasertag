using Anaglyph.Netcode;
using Anaglyph.XRTemplate.CameraReader;
using Anaglyph.XRTemplate.QuestCV;
using AprilTag;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEngine;
using static UnityEngine.GameObject;

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

		private const float Lerp = 0.3f;

		private bool colocationActive = false;

		public Dictionary<int, AprilTagAnchor> foundTags = new();

		private GameObject tagAnchorPrefab;

		public AprilTagColocator()
		{
			tagAnchorPrefab = Resources.Load<GameObject>("April Tag Anchor");
		}

		public void Colocate()
		{
			IsColocated = false;
			colocationActive = true;

			foundTags.Clear();

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
					foundTag.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
					Colocation.LerpTrackingSpace(foundTag.transform.GetWorldPose(), foundTag.DesiredPose, Lerp / poses.Count);

					IsColocated = true;

				} else if(manager.IsHost)
				{
					GameObject g = GameObject.Instantiate(tagAnchorPrefab, pose.Position, pose.Rotation);
					g.TryGetComponent(out AprilTagAnchor anchor);
					anchor.transform.localScale = Vector3.one * AprilTagTracker.Instance.tagSizeMeters;
					anchor.desiredPoseSync.Value = new NetworkPose(pose.Position, pose.Rotation);
					anchor.NetworkObject.Spawn();

				}
			}
		}

		public void StopColocation()
		{
			CameraManager.Instance.StopCapture();
			AprilTagTracker.Instance.OnDetectTags -= OnDetectTags;

			colocationActive = false;
			IsColocated = false;
		}
	}
}
