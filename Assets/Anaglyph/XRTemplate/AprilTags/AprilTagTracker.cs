using UnityEngine;
using AprilTag;
using System;
using System.Collections.Generic;
using UnityEngine.XR;
using EnvisionCenter.XRTemplate.DisplayCapture.AprilTags;

namespace EnvisionCenter.XRTemplate.QuestCV
{
	[DefaultExecutionOrder(-1000)]
	public class AprilTagTracker : MonoBehaviour
	{
		public static AprilTagTracker Instance { get; private set; }

		private TagDetector detector;

		public float horizontalFovDeg = 82f;
		public float tagSizeMeters = 0.12f;
		[SerializeField] private int decimation = 4;

		private Texture2D texture;

		private List<TagPose> worldPoses = new(10);
		public IEnumerable<TagPose> WorldPoses => worldPoses;
		public event Action<IEnumerable<TagPose>> OnDetectTags = delegate { };

		private List<XRNodeState> nodeStates = new();

		private void Awake()
		{
			Instance = this;
			detector = new TagDetector(1024, 1024, decimation);
		}

		private void OnEnable()
		{
			// DisplayCaptureManager.OnNewFrame += OnReceivedNewFrame;
			TrackingLoop();
		}

		private void OnDisable()
		{
			// DisplayCaptureManager.OnNewFrame -= OnReceivedNewFrame;
			newFrameAvailable = false;
		}

		// called on another thread so we do this
		private bool newFrameAvailable = false;
		private void OnReceivedNewFrame(Texture2D t)
		{
			texture = t;
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
				await Awaitable.EndOfFrameAsync();

				if (!newFrameAvailable)
					continue;

				var fov = horizontalFovDeg;
				var size = tagSizeMeters;
				Color32[] pixels = texture.GetPixels32();
				await detector.SchedulePoseEstimationJob(pixels, fov * Mathf.Deg2Rad, size);

				worldPoses.Clear();

				// nanoseconds to milliseconds
				var timestamp = 0;//  DisplayCaptureManager.Instance.TimestampNanoseconds * 0.000000001f;
				OVRPlugin.PoseStatef headPoseState = OVRPlugin.GetNodePoseStateAtTime(timestamp, OVRPlugin.Node.Head);
				OVRPose headPose = headPoseState.Pose.ToOVRPose();
				Matrix4x4 headTransform = Matrix4x4.TRS(headPose.position, headPose.orientation, Vector3.one);

				foreach (var pose in detector.DetectedTags)
				{
					TagPose worldPose = new(
						pose.ID,
						headTransform.MultiplyPoint(pose.Position),
						headTransform.rotation * pose.Rotation * Quaternion.Euler(-90, 0, 0));

					worldPoses.Add(worldPose);
				}

				newFrameAvailable = false;

				OnDetectTags.Invoke(worldPoses);
			}
		}
	}
}