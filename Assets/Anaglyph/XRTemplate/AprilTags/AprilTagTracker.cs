using UnityEngine;
using AprilTag;
using System;
using System.Collections.Generic;
using Anaglyph.Debugging.Visuals;
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

		[Tooltip("XR Simulation feeds us a GPU readback, whose first row is the " +
		         "bottom of the image. Turn off if editor tags stop being detected.")]
		[SerializeField] private bool simulatorMirrorY = true;

		private List<TagPose> worldPoses = new(10);
		public IEnumerable<TagPose> WorldPoses => worldPoses;
		public event Action<IReadOnlyList<TagPose>> OnDetectTags = delegate { };

		private NativeArray<byte> processedImg;

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

		private void LateUpdate()
		{
			if(AnaglyphDebugging.DebugMode)
				foreach (TagPose worldPose in worldPoses)
					DebugAxisVisual.DrawDebugAxis(worldPose.Position, worldPose.Rotation, Color.orange, tagSizeMeters);
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

						// TODO: get cam id programmatically
						if (lensPose.Equals(Pose.identity))
							lensPose = AndroidCamExtrinsicsHelper.GetCameraExtrinsics(50);

						break;
				}

				// XR Simulation's camera subsystem returns true from TryGetIntrinsics
				// but never populates the struct, so focalLength is 0 and the naive
				// fov works out to 180 degrees -- which makes the tag pose solver
				// spit out garbage. Fall back to the XR camera's projection, which
				// is what the simulation camera renders with anyway.
				float fov = intrins.focalLength.y > 0
					? 2 * Mathf.Atan(img.height / 2f / intrins.focalLength.y)
					: 2 * Mathf.Atan(1f / MainXRRig.Camera.projectionMatrix.m11);
				long frameTimestampNs = args.timestampNs.Value;
				FrameTimestampNs = frameTimestampNs;

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

						EnsureProcessedImgSize(img.GetConvertedDataSize(convParams));

						img.Convert(convParams, processedImg);

						break;

					case XRCpuImage.Format.BGRA32:
					case XRCpuImage.Format.RGBA32:
						// probably unity editor simulator.

						EnsureProcessedImgSize(img.GetGrayscaleDataSize());

						img.ConvertToGrayscale(processedImg, simulatorMirrorY);

						break;

					default:
						throw new Exception("unsupported image format");
				}

				await detector.Detect(processedImg, fov, tagSizeMeters);

				img.Dispose();

				worldPoses.Clear();
				
				Pose headPose = default;
				bool gotHistoricalPose = HeadPoseHistory.Instance != null &&
				                         HeadPoseHistory.Instance.TryGetLocalPose(frameTimestampNs, out headPose);

				if (!gotHistoricalPose)
				{
					Debug.LogWarning($"AprilTagTracker: Frame {frameTimestampNs} has no historical pose");
					return;
				}

				Matrix4x4 headMat = Matrix4x4.TRS(headPose.position, headPose.rotation, Vector3.one);
				Matrix4x4 lensMat = Matrix4x4.TRS(lensPose.position, lensPose.rotation, Vector3.one);
				Matrix4x4 localViewMat = headMat * lensMat;
				Matrix4x4 viewMat = MainXRRig.TrackingSpace.localToWorldMatrix * localViewMat;

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
				busy = false;
			}
		}

		private void EnsureProcessedImgSize(int size)
		{
			if (processedImg.IsCreated && processedImg.Length == size)
				return;

			if (processedImg.IsCreated)
				processedImg.Dispose();

			processedImg = new NativeArray<byte>(size, Allocator.Persistent);
		}

		private void OnDestroy()
		{
			if (processedImg.IsCreated)
				processedImg.Dispose();
		}
	}
}