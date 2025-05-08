using Anaglyph.XRTemplate.CameraReader;
using Anaglyph.XRTemplate.QuestCV;
using Anaglyph.XRTemplate.SharedSpaces;
using AprilTag;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.Lasertag.Gallery
{
	public class BasicTagColocator : MonoBehaviour, IColocator
	{
		[SerializeField] private GameObject anchorPrefab;
		private GameObject currentAnchor;

		[SerializeField] private int targetTagID = 1;
		[SerializeField] private int numFramesToColocate = 10;
		private int numFramesDetected;

		private void Awake()
		{
			CameraManager.Instance.Configure(0, 320, 240);
			AprilTagTracker.Instance.OnDetectTags += OnDetectTags;
			AprilTagTracker.Instance.enabled = false;

			Colocation.SetActiveColocator(this);
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

		public void Colocate()
		{
			if (IsColocated)
				StopColocation();

			CameraManager.Instance.StartCapture();
			AprilTagTracker.Instance.enabled = true;
		}

		private void OnDetectTags(IReadOnlyList<TagPose> poses)
		{
			foreach (TagPose pose in poses)
			{
				if (pose.ID == targetTagID)
				{
					numFramesDetected++;

					if (numFramesDetected == numFramesToColocate)
					{
						var forw = pose.Rotation * -Vector3.up;
						forw.y = 0;
						forw.Normalize();

						var quat = Quaternion.LookRotation(forw, Vector3.up);



						Anchor(pose.Position, quat);

						CameraManager.Instance.StopCapture();
						AprilTagTracker.Instance.enabled = false;
					}

					return;
				}
			}

			numFramesDetected = 0;
		}

		private void Anchor(Vector3 pos, Quaternion rot)
		{
			currentAnchor = Instantiate(anchorPrefab, pos, rot);
			IsColocated = true;
		}

		private void LateUpdate()
		{
			if (!IsColocated)
				return;

			Vector3 pos = currentAnchor.transform.position;
			Quaternion rot = currentAnchor.transform.rotation;
			
			Colocation.TransformTrackingSpace(new UnityEngine.Pose(pos, rot));
		}

		public void StopColocation()
		{
			if(IsColocated)
				Destroy(currentAnchor);

			CameraManager.Instance.StopCapture();
			AprilTagTracker.Instance.enabled = false;

			IsColocated = false;
		}
	}
}
