using System;
using System.Collections.Generic;
using Anaglyph.XRTemplate.AprilTags;
using AprilTag;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class TagTrackerIndicators : MonoBehaviour
    {
		[SerializeField] private AprilTagTracker tracker;

		[SerializeField] private Mesh indicatorMesh;
		[SerializeField] private Material indicatorMaterial;
        
		private IReadOnlyList<TagPose> latestTagPoses;

		private void Start()
        {
            tracker.OnDetectTags += OnTagsDetected;
        }

        private void OnDestroy()
        {
            if(tracker != null)
                tracker.OnDetectTags -= OnTagsDetected;
        }

		private void OnTagsDetected(IReadOnlyList<TagPose> tagPoses)
		{
			latestTagPoses = tagPoses;
		}

		private void LateUpdate()
        {
            if (!tracker || !tracker.enabled)
                return;
            
            Vector3 scale = Vector3.one * (tracker.tagSizeMeters * 3);

			if (latestTagPoses != null)
			{
				foreach (TagPose tagPose in latestTagPoses)
				{
					var model = Matrix4x4.TRS(tagPose.Position, tagPose.Rotation, scale);
					Graphics.DrawMesh(indicatorMesh, model, indicatorMaterial, 0, MainXRRig.Camera, 0);
				}
			}
		}
    }
}
