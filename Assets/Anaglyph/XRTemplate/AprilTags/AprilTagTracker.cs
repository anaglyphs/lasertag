using UnityEngine;
using AprilTag;
using System;
using System.Collections.Generic;
using Anaglyph.XRTemplate.DeviceCameras;

namespace Anaglyph.XRTemplate.AprilTags
{
	[DefaultExecutionOrder(-1000)]
	public class AprilTagTracker : MonoBehaviour
	{
		[SerializeField] private CameraReader cameraReader;

		private TagDetector detector;

		public float tagSizeMeters = 0.12f;

		private Texture2D tex;

		private List<TagPose> worldPoses = new(10);
		public IEnumerable<TagPose> WorldPoses => worldPoses;
		public event Action<IReadOnlyList<TagPose>> OnDetectTags = delegate { };

		public double FrameTimestamp { get; private set; }

		private void OnEnable()
		{
			cameraReader.ImageAvailable += OnReceivedNewFrame;
			TrackingLoop();
		}

		private void OnDisable()
		{
			cameraReader.ImageAvailable -= OnReceivedNewFrame;
			newFrameAvailable = false;
		}

		// called on another thread so we do this
		private bool newFrameAvailable = false;

		private void OnReceivedNewFrame(Texture2D t)
		{
			if (!Application.isFocused)
				return;

			tex = t;
			newFrameAvailable = true;
		}

		private async void TrackingLoop()
		{
#if UNITY_EDITOR

			while (enabled)
			{
				await Awaitable.FixedUpdateAsync();

				worldPoses.Clear();

				foreach (var tag in SimulatedTag.Visible)
					if (tag.isInView)
						worldPoses.Add(tag.GetTagPoseInWorldSpace());

				OnDetectTags.Invoke(worldPoses);
			}

			return;
#endif

			while (enabled)
			{
				await Awaitable.NextFrameAsync();

				if (!newFrameAvailable)
					continue;

				newFrameAvailable = false;

				if (detector == null)
					detector = new TagDetector(tex.width, tex.height, 1);

				var intrins = cameraReader.HardwareIntrinsics;
				var fov = 2 * Mathf.Atan(intrins.Resolution.y / 2f / intrins.FocalLength.y);
				var size = tagSizeMeters;

				FrameTimestamp = cameraReader.TimestampNs * 0.000000001f;
				var headPoseState = OVRPlugin.GetNodePoseStateAtTime(FrameTimestamp, OVRPlugin.Node.Head);

				var imgBytes = cameraReader.Texture.GetPixelData<byte>(0);
				await detector.Detect(imgBytes, fov, size);

				worldPoses.Clear();

				// nanoseconds to milliseconds
				var headPose = headPoseState.Pose.ToOVRPose();
				var viewMat = Matrix4x4.TRS(headPose.position, headPose.orientation, Vector3.one);
				var lensPose = cameraReader.HardwarePose;
				var cameraMat = Matrix4x4.TRS(lensPose.position, lensPose.rotation, Vector3.one);
				var cameraRelativeToRig = viewMat * cameraMat;
				viewMat = MainXRRig.TrackingSpace.localToWorldMatrix * cameraRelativeToRig;


				foreach (var pose in detector.DetectedTags)
				{
					TagPose worldPose = new(
						pose.ID,
						viewMat.MultiplyPoint(pose.Position),
						viewMat.rotation * pose.Rotation * Quaternion.Euler(-90, 0, 0));

					worldPoses.Add(worldPose);
				}

				if (worldPoses.Count == 0)
					continue;

				OnDetectTags.Invoke(worldPoses);
			}
		}
	}
}