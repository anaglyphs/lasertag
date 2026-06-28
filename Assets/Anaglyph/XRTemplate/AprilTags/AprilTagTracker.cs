using UnityEngine;
using AprilTag;
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Pose = UnityEngine.Pose;

namespace Anaglyph.XRTemplate.AprilTags
{
	[DefaultExecutionOrder(-1000)]
	public class AprilTagTracker : MonoBehaviour
	{
		private ARCameraManager arCameraManager;

		private TagDetector detector;
		private Vector2Int detectorDimensions;
		private Pose lensPose = Pose.identity;

		public float tagSizeMeters = 0.12f;

		private List<TagPose> worldPoses = new(10);
		public IEnumerable<TagPose> WorldPoses => worldPoses;
		public event Action<IReadOnlyList<TagPose>> OnDetectTags = delegate { };

		// CLOCK_MONOTONIC ns of the most recent processed frame (== XrTime on
		// Quest). Valid during the OnDetectTags callback; feed to HeadPoseHistory.
		public long FrameTimestampNs { get; private set; }

		private void Start()
		{
			arCameraManager = FindFirstObjectByType<ARCameraManager>();

			if (arCameraManager == null)
				throw new Exception("No ARCameraManager found in scene");

			arCameraManager.frameReceived += OnFrameReceived;
		}

		private void OnEnable()
		{
			if (didStart)
				Start();
		}

		private void OnDisable()
		{
			if (arCameraManager)
				arCameraManager.frameReceived -= OnFrameReceived;
		}

		private bool busy;

		private async void OnFrameReceived(ARCameraFrameEventArgs args)
		{
			if (busy) return;
			busy = true;

			XRCpuImage img = default;
			XRCameraIntrinsics intrins = default;

			try
			{
				// if (args.textures != null && args.textures.Count > 0)
				// 	Shader.SetGlobalTexture(DebugCamTexID, args.textures[0]);

				bool gotIntrins = arCameraManager.TryGetIntrinsics(out intrins);
				bool gotImg = arCameraManager.TryAcquireLatestCpuImage(out img);

				bool gotAll = gotImg && gotIntrins && args.timestampNs.HasValue;
				if (!gotAll) return;

				if (detector == null || detectorDimensions != img.dimensions)
				{
					detector = new TagDetector(img.width, img.height, 1);
					detectorDimensions = img.dimensions;
				}

				switch (Application.platform)
				{
					case RuntimePlatform.Android:

						if (lensPose.Equals(Pose.identity))
							// assuming cam ID of one for arCameraManager?
							lensPose = AndroidCamExtrinsicsHelper.GetCameraExtrinsics(50);

						break;
				}

				float fov = 2 * Mathf.Atan(img.height / 2f / intrins.focalLength.y);
				long frameTimestampNs = args.timestampNs.Value;
				FrameTimestampNs = frameTimestampNs;

				NativeArray<byte> imgGreyscale = default;

				// on ARFoundation simulator, a plane holds BGRA data
				// on android, the plane holds greyscale single-byte values.
				// process the textures differently between platforms
				switch (img.format)
				{
					case XRCpuImage.Format.AndroidYuv420_888:
						// android conversion

						RectInt rect = new(0, 0, img.width, img.height);
						XRCpuImage.ConversionParams convParams = new()
						{
							inputRect = rect,
							outputDimensions = img.dimensions,
							outputFormat = TextureFormat.R8,
							transformation = XRCpuImage.Transformation.MirrorY
						};

						int size = img.GetConvertedDataSize(convParams);
						imgGreyscale = new NativeArray<byte>(size, Allocator.Temp);
						img.Convert(convParams, imgGreyscale);

						break;

					case XRCpuImage.Format.BGRA32:
						// probably unity editor simulator

						throw new NotImplementedException();
						break;

					default:
						throw new Exception("unsupported image format");
				}

				await detector.Detect(imgGreyscale, fov, tagSizeMeters);

				img.Dispose();
				imgGreyscale.Dispose();

				worldPoses.Clear();

				// Head pose (tracking-space-local) at the instant the frame was
				// captured -- not "now". Detection runs async and the camera
				// pipeline has latency, so the head has moved since capture.
				// Replaces OVRPlugin.GetNodePoseStateAtTime(FrameTimestamp, Head).
				Pose headPose = default;
				bool gotHistoricalPose = HeadPoseHistory.Instance != null &&
				                         HeadPoseHistory.Instance.TryGetLocalPose(frameTimestampNs, out headPose);

				if (!gotHistoricalPose)
				{
					// fallback: latest camera pose in tracking-space-local coords
					Matrix4x4 camLocal = MainXRRig.TrackingSpace.worldToLocalMatrix *
					                     MainXRRig.Camera.transform.localToWorldMatrix;
					headPose = new Pose(camLocal.GetPosition(), camLocal.rotation);
				}

				Matrix4x4 viewMat = Matrix4x4.TRS(headPose.position, headPose.rotation, Vector3.one);
				Matrix4x4 cameraMat = Matrix4x4.TRS(lensPose.position, lensPose.rotation, Vector3.one);
				Matrix4x4 cameraRelativeToRig = viewMat * cameraMat;
				viewMat = MainXRRig.TrackingSpace.localToWorldMatrix * cameraRelativeToRig;

				foreach (TagPose pose in detector.DetectedTags)
				{
					TagPose worldPose = new(
						pose.ID,
						viewMat.MultiplyPoint(pose.Position),
						viewMat.rotation * pose.Rotation * Quaternion.Euler(-90, 0, 0));

					worldPoses.Add(worldPose);
				}

				OnDetectTags.Invoke(worldPoses);
			}
			finally
			{
				img.Dispose();
				busy = false;
			}
		}
	}
}