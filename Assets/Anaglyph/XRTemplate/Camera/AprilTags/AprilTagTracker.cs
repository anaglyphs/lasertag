using UnityEngine;
using AprilTag;
using System;
using System.Collections.Generic;
using UnityEngine.XR;
using EnvisionCenter.XRTemplate.DisplayCapture.AprilTags;
using Anaglyph.XRTemplate.CameraReader;
using Anaglyph.XRTemplate;

namespace EnvisionCenter.XRTemplate.QuestCV
{
	[DefaultExecutionOrder(-1000)]
	public class AprilTagTracker : MonoBehaviour
	{
		public static AprilTagTracker Instance { get; private set; }

		private TagDetector detector;

		public float tagSizeMeters = 0.12f;

		private Texture2D tex;

		private List<TagPose> worldPoses = new(10);
		public IEnumerable<TagPose> WorldPoses => worldPoses;
		public event Action<IReadOnlyList<TagPose>> OnDetectTags = delegate { };

		public double FrameTimestamp { get; private set; }

		private void Awake()
		{
			Instance = this;
		}

		private void OnEnable()
		{
			CameraManager.ImageAvailable += OnReceivedNewFrame;
			TrackingLoop();
		}

		private void OnDisable()
		{
			CameraManager.ImageAvailable -= OnReceivedNewFrame;
			newFrameAvailable = false;
		}

		// called on another thread so we do this
		private bool newFrameAvailable = false;
		private void OnReceivedNewFrame(Texture2D t)
		{
			tex = t;
			newFrameAvailable = true;
		}

		private async void TrackingLoop()
		{
#if UNITY_EDITOR

			while(enabled)
			{
				await Awaitable.FixedUpdateAsync();

				worldPoses.Clear();

				foreach (SimulatedTag tag in SimulatedTag.Visible)
				{
					if(tag.isInView)
						worldPoses.Add(tag.GetTagPoseInWorldSpace());
				}

				OnDetectTags.Invoke(worldPoses);
			}

			return;
#endif

			while(enabled)
			{
				await Awaitable.NextFrameAsync();

				if (!newFrameAvailable)
					continue;

				if (!Application.isFocused)
					continue;

				newFrameAvailable = false;

				if (detector == null)
					detector = new TagDetector(tex.width, tex.height, 1);

				var intrins = CameraManager.Instance.CamIntrinsics;
				var fov = 2 * Mathf.Atan((intrins.Resolution.y / 2f) / intrins.FocalLength.y);
				var size = tagSizeMeters;

				FrameTimestamp = CameraManager.Instance.TimestampNanoseconds * 0.000000001f;
				OVRPlugin.PoseStatef headPoseState = OVRPlugin.GetNodePoseStateAtTime(FrameTimestamp, OVRPlugin.Node.Head);

				var imgBytes = CameraManager.Instance.CamTex.GetPixelData<byte>(0);
				await detector.Detect(imgBytes, fov, size);

				worldPoses.Clear();

				// nanoseconds to milliseconds
				OVRPose headPose = headPoseState.Pose.ToOVRPose();
				Matrix4x4 viewMat = Matrix4x4.TRS(headPose.position, headPose.orientation, Vector3.one);
				var lensPose = CameraManager.Instance.CamPoseOnDevice;
				Matrix4x4 cameraMat = Matrix4x4.TRS(lensPose.position, lensPose.rotation, Vector3.one);
				Matrix4x4 cameraRelativeToRig = viewMat * cameraMat;
				viewMat = MainXROrigin.Transform.localToWorldMatrix * cameraRelativeToRig;
				

				foreach (var pose in detector.DetectedTags)
				{
					TagPose worldPose = new(
						pose.ID,
						viewMat.MultiplyPoint(pose.Position),
						viewMat.rotation * pose.Rotation * Quaternion.Euler(-90, 0, 0));

					worldPoses.Add(worldPose);
				}

				OnDetectTags.Invoke(worldPoses);
			}
		}
	}
}