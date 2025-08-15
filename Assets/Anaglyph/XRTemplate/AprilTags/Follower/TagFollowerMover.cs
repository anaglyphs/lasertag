using AprilTag;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate.AprilTags
{
	public class TagFollowerMover : MonoBehaviour
	{
		[SerializeField] private AprilTagTracker tracker;

		public static TagFollowerMover Instance { get; private set; }

		public Dictionary<int, TagFollower> allFollowers = new();

		private void Awake()
		{
			Instance = this;
		}

		private void OnEnable() => tracker.OnDetectTags += OnDetectTags;
		private void OnDisable() => tracker.OnDetectTags -= OnDetectTags;

		private void OnDetectTags(IEnumerable<TagPose> tagPoses)
		{
			foreach (TagPose tagPose in tagPoses)
			{
				if (!allFollowers.ContainsKey(tagPose.ID))
					continue;

				TagFollower follower = allFollowers[tagPose.ID];

				follower.OnDetect(new Pose(tagPose.Position, tagPose.Rotation));
			}
		}
	}
}