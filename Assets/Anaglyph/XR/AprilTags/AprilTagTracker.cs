using System;
using System.Collections.Generic;
using Anaglyph.Debugging;
using Anaglyph.Debugging.Visuals;
using AprilTag;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Pose = UnityEngine.Pose;

namespace Anaglyph.XR.AprilTags
{
	[DefaultExecutionOrder(-1000)]
	public class AprilTagTracker : MonoBehaviour
	{
		private ARCameraManager arCameraManager;

		private TagDetector detector;
		private Vector2Int detectorDimensions;
		private Pose lensPose = Pose.identity;
		private bool lookedForLens;

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

		/// <summary>
		/// Whether the frame these detections came from was rendered rather than captured.
		/// XR Simulation hands back a readback of a rendered camera, which has none of a
		/// physical sensor's motion blur or exposure lag, so consumers that distrust readings
		/// taken while the head moves have nothing to distrust here. Valid during the
		/// <see cref="OnDetectTags"/> callback.
		/// </summary>
		public bool FrameIsRendered { get; private set; }

		private void Start()
		{
			arCameraManager = FindFirstObjectByType<ARCameraManager>();

			// Desktop operators have no camera feed or tracked space to scan in.
			// Check scene capabilities so XR Simulation can still scan in the Editor.
			if (arCameraManager == null || MainXRRig.Instance == null)
			{
				enabled = false;
				return;
			}

			arCameraManager.frameReceived += OnFrameReceived;
		}

		private void OnEnable()
		{
			if (didStart)
				Start();
		}

		// Camera2 frames arrive with no AR Foundation frame event to start detection
		private void Update()
		{
			if (PassthroughFrames.UsesCamera2(arCameraManager))
				DetectLatestFrame(null);
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
		private long lastDetectedTimestampNs;

		private void OnFrameReceived(ARCameraFrameEventArgs args) => DetectLatestFrame(args.timestampNs);

		private async void DetectLatestFrame(long? frameEventTimestampNs)
		{
			if (busy) return;
			busy = true;

			XRCpuImage img = default;
			XRCameraIntrinsics intrins = default;
			bool gotImg = false;

			try
			{
				// if (args.textures != null && args.textures.Count > 0)
				// 	Shader.SetGlobalTexture(DebugCamTexID, args.textures[0]);

				long frameTimestampNs;
				LensPosition lensPosition;

				// Prefer Meta's per-eye API where it exists: it tells us which
				// physical camera the image came from, which the cross-platform
				// AR Foundation API can't express, and hands back that camera's
				// own intrinsics and capture time.
				if (PassthroughFrames.TryAcquire(arCameraManager, out PassthroughFrame frame))
				{
					img = frame.image;
					gotImg = true;
					intrins = frame.intrinsics;
					frameTimestampNs = frame.timestampNs;
					lensPosition = frame.lensPosition;
					if (frame.lensInHead.HasValue && !lookedForLens)
					{
						lookedForLens = true;
						lensPose = frame.lensInHead.Value;
					}
				}
				else
				{
					if (!PassthroughFrames.GenericImageAllowed || !frameEventTimestampNs.HasValue)
						return;

					bool gotIntrins = arCameraManager.TryGetIntrinsics(out intrins);
					gotImg = arCameraManager.TryAcquireLatestCpuImage(out img);

					if (!gotImg || !gotIntrins)
						return;

					frameTimestampNs = frameEventTimestampNs.Value;
					lensPosition = LensPosition.Unknown;
				}

				if (frameTimestampNs == lastDetectedTimestampNs)
					return;
				lastDetectedTimestampNs = frameTimestampNs;

				if (detector == null || detectorDimensions != img.dimensions)
				{
					detector = new TagDetector(img.width, img.height, 3);
					detectorDimensions = img.dimensions;
				}

				// The lens pose is fixed hardware geometry, so look it up once.
				if (Application.platform == RuntimePlatform.Android && !lookedForLens)
				{
					lookedForLens = true;

					bool foundLens = AndroidCamExtrinsicsHelper.TryFindCameraExtrinsics(
						arCameraManager.currentFacingDirection, img.dimensions, lensPosition,
						out lensPose, out _);

					if (!foundLens)
						Debug.LogError("AprilTagTracker: no camera extrinsics found, " +
						               "tag poses will be offset by the lens to head distance");
				}

				// XR Simulation's camera subsystem lies and
				// always returns true from TryGetIntrinsics
				// despite not filling anything in :/
				float fov = intrins.focalLength.y > 0
					? 2 * Mathf.Atan(img.height / 2f / intrins.focalLength.y)
					: 2 * Mathf.Atan(1f / MainXRRig.Camera.projectionMatrix.m11);

				FrameTimestampNs = frameTimestampNs;

				// A passthrough sensor delivers YUV; the RGB formats below are the simulator's
				// rendered readback.
				FrameIsRendered = img.format is
					XRCpuImage.Format.BGRA32 or XRCpuImage.Format.RGBA32;

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
							transformation = XRCpuImage.Transformation.None
						};

						EnsureProcessedImgSize(img.GetConvertedDataSize(convParams));

						img.Convert(convParams, processedImg);

						break;

					case XRCpuImage.Format.OneComponent8:
					{
						XRCpuImage.Plane luma = img.GetPlane(0);
						EnsureProcessedImgSize(img.width * img.height);
						for (int row = 0; row < img.height; row++)
							NativeArray<byte>.Copy(luma.data, row * luma.rowStride, processedImg, row * img.width, img.width);

						break;
					}

					case XRCpuImage.Format.BGRA32:
					case XRCpuImage.Format.RGBA32:
						// probably unity editor simulator.

						EnsureProcessedImgSize(img.GetGrayscaleDataSize());

						img.ConvertToGrayscale(processedImg, simulatorMirrorY);

						break;

					default:
						throw new Exception("unsupported image format");
				}

				float tagSizeMetersClamped = Mathf.Max(tagSizeMeters, 0.01f);

				await detector.Detect(processedImg, fov, tagSizeMetersClamped);

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
				if (gotImg)
					img.Dispose();

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

			detector?.Dispose();
		}
	}
}
