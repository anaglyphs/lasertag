using System;
using Anaglyph.XR.AprilTags;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>Pinhole intrinsics in an image's pixels, y down, origin at the top-left pixel's center.</summary>
	[Serializable]
	public struct PinholeCamera
	{
		public float fx, fy, cx, cy;

		public PinholeCamera(float fx, float fy, float cx, float cy)
		{
			this.fx = fx;
			this.fy = fy;
			this.cx = cx;
			this.cy = cy;
		}

		/// <summary>Unit ray through a pixel, in Unity camera space (x right, y up, z forward).</summary>
		public float3 Ray(float2 pixel) => math.normalize(new float3((pixel.x - cx) / fx, -(pixel.y - cy) / fy, 1));

		/// <summary>Pixel a Unity camera-space point projects to; false behind the camera.</summary>
		public bool Project(float3 p, out float2 pixel)
		{
			pixel = default;
			if (p.z <= 1e-3f) return false;
			pixel = new float2(cx + fx * p.x / p.z, cy - fy * p.y / p.z);
			return true;
		}

		public override string ToString() => $"f=({fx:F1}, {fy:F1}) c=({cx:F1}, {cy:F1})";
	}

	/// <summary>One passthrough frame, ready for <see cref="XFeatExtractor"/>.</summary>
	public sealed class CapturedImage : IDisposable
	{
		/// <summary><see cref="XFeatExtractor.Width"/>×<see cref="XFeatExtractor.Height"/> grayscale, 0..1, row 0 on top.</summary>
		public NativeArray<float> pixels = new(XFeatExtractor.Width * XFeatExtractor.Height, Allocator.Persistent);

		public PinholeCamera intrinsics;

		/// <summary>The lens in tracking-space coordinates at capture time (Unity camera axes).</summary>
		public Pose cameraInTrackingSpace;

		/// <summary>The lens in world coordinates, through the tracking space's pose at capture.</summary>
		public Pose cameraInWorld;

		/// <summary>The tracking space's world pose the frame was placed with.</summary>
		public Pose trackingSpace;

		public long timestampNs;

		/// <summary>Head rotation speed around capture, degrees per second: motion blur's proxy.</summary>
		public float angularSpeed;

		/// <summary>Where the camera's frame lies in <see cref="pixels"/> (min x, min y, max x, max y); outside it is padding.</summary>
		public float4 content;

		/// <summary>XR Simulation's rendered readback rather than a physical sensor.</summary>
		public bool rendered;

		public void Dispose()
		{
			if (pixels.IsCreated) pixels.Dispose();
		}
	}

	/// <summary>
	/// Grabs the passthrough camera's latest CPU image through
	/// <see cref="MetaPassthroughCamera"/> (or AR Foundation's generic image, as on phones), fits
	/// the whole frame into the extractor's square grayscale with matching intrinsics, and places
	/// the lens in the world at the frame's capture time through <see cref="HeadPoseHistory"/>.
	/// </summary>
	public sealed class PassthroughCapture : IDisposable
	{
		private readonly ARCameraManager cameraManager;
		private NativeArray<byte> gray;
		private Pose lensPose = Pose.identity;
		private bool lookedForLens;
		private bool loggedSize;

		/// <summary>XR Simulation's readback has its first row at the bottom.</summary>
		public bool simulatorMirrorY = true;

		/// <summary>Above 0: XR Simulation's frames are center-cropped to this width over height, as a phone's 4:3 sensor sees.</summary>
		public static float CropRenderedFramesToAspectForTests;

		public PassthroughCapture(ARCameraManager cameraManager)
		{
			this.cameraManager = cameraManager;
		}

		/// <summary>
		/// Fills <paramref name="into"/> from the camera's latest image. False when no image,
		/// intrinsics or historical head pose is available for it.
		/// </summary>
		/// <param name="fallbackTimestampNs">The frame event's timestamp, used when the per-eye API is absent.</param>
		/// <param name="maxAngularSpeed">A frame taken while the head turned faster than this (°/s) is given up before converting it: false, with <see cref="CapturedImage.angularSpeed"/> set.</param>
		public bool TryCapture(CapturedImage into, long? fallbackTimestampNs = null, float maxAngularSpeed = float.PositiveInfinity)
		{
			into.angularSpeed = 0;
			if (cameraManager == null || HeadPoseHistory.Instance == null || MainXRRig.Instance == null) return false;

			XRCpuImage image = default;
			bool gotImage = false;
			try
			{
				XRCameraIntrinsics intrinsics;
				long timestampNs;
				LensPosition lens;

				if (MetaPassthroughCamera.TryAcquireFrame(cameraManager, out MetaPassthroughCamera.Frame frame))
				{
					image = frame.image;
					gotImage = true;
					intrinsics = frame.intrinsics;
					timestampNs = frame.timestampNs;
					lens = frame.lensPosition;
				}
				else
				{
					if (!cameraManager.TryGetIntrinsics(out intrinsics)) return false;
					gotImage = cameraManager.TryAcquireLatestCpuImage(out image);
					if (!gotImage) return false;
					lens = LensPosition.Unknown;

					// a phone's latest image can be a frame newer than the last frame event: its own stamp finds its pose
					if (HeadPoseHistory.Instance.SamplesAreCameraFrames && image.timestamp > 0)
						timestampNs = (long)Math.Round(image.timestamp * 1e9);
					else if (fallbackTimestampNs.HasValue)
						timestampNs = fallbackTimestampNs.Value;
					else
						return false;

#if UNITY_EDITOR
					// XR Simulation renders its camera from an Update, before the head moves to
					// this frame's pose: the image shows where the head was a frame earlier
					// (measured exactly with SimCaptureTiming), stamped with this frame's time
					if (HeadPoseHistory.Instance != null && HeadPoseHistory.Instance.TryGetPreviousSampleTime(timestampNs, out long previous))
						timestampNs = previous;
#endif
				}

				if (!HeadPoseHistory.Instance.TryGetLocalPose(timestampNs, out Pose head)) return false;

				// motion blur: how far the head turned over the 30 ms before capture
				into.angularSpeed = HeadPoseHistory.Instance.TryGetLocalPose(timestampNs - 30_000_000, out Pose before)
					? Quaternion.Angle(before.rotation, head.rotation) / 0.03f
					: float.PositiveInfinity;
				if (into.angularSpeed > maxAngularSpeed) return false;

				// a phone's camera poses are already the lens's
				if (Application.platform == RuntimePlatform.Android && !lookedForLens && !HeadPoseHistory.Instance.SamplesAreCameraFrames)
				{
					lookedForLens = true;
					if (!AndroidCamExtrinsicsHelper.TryFindCameraExtrinsics(cameraManager.currentFacingDirection,
						    image.dimensions, lens, out lensPose, out _))
						Debug.LogError($"[{nameof(PassthroughCapture)}] no camera extrinsics found: poses will be off by the lens to head offset");
				}

				if (!TryGetGrayscale(image, out NativeArray<byte> source, out int sourceStride, out int width, out int height, out into.rendered)) return false;
				int sourceOffsetX = 0, sourceOffsetY = 0;
				int uncroppedWidth = width, uncroppedHeight = height;
				float cropAspect = CropRenderedFramesToAspectForTests;
				if (into.rendered && cropAspect > 0)
				{
					if (width > height * cropAspect)
					{
						int cropped = Mathf.RoundToInt(height * cropAspect);
						sourceOffsetX = (width - cropped) / 2;
						width = cropped;
					}
					else
					{
						int cropped = Mathf.RoundToInt(width / cropAspect);
						sourceOffsetY = (height - cropped) / 2;
						height = cropped;
					}
				}
				if (!loggedSize)
				{
					// which camera mode the runtime picked: AR Foundation can't choose one on Quest
					loggedSize = true;
					Debug.Log($"[{nameof(PassthroughCapture)}] camera frames are {width}×{height}, fitted into {XFeatExtractor.Width}×{XFeatExtractor.Height}");
				}

				// intrinsics of the full image; XR Simulation reports none, so take its render camera's
				float fx = intrinsics.focalLength.x, fy = intrinsics.focalLength.y;
				float cx = intrinsics.principalPoint.x, cy = intrinsics.principalPoint.y;
				if (fy <= 0)
				{
					fy = fx = uncroppedHeight * 0.5f * MainXRRig.Camera.projectionMatrix.m11;
					cx = uncroppedWidth * 0.5f;
					cy = uncroppedHeight * 0.5f;
				}
				else if (intrinsics.resolution.x > 0 && intrinsics.resolution.x != uncroppedWidth)
				{
					float s = (float)uncroppedWidth / intrinsics.resolution.x;
					fx *= s;
					fy *= s;
					cx *= s;
					cy *= s;
				}

				cx -= sourceOffsetX;
				cy -= sourceOffsetY;

				// the whole frame, fitted inside the model's square input: a 1280×1280 frame fills
				// it, a 4:3 one keeps its full width between padded bands (edge pixels repeated,
				// and no keypoints taken near them), so no field of view is ever cropped away
				const int outW = XFeatExtractor.Width, outH = XFeatExtractor.Height;
				float scale = math.min((float)outW / width, (float)outH / height);
				float x0 = (width - outW / scale) * 0.5f, y0 = (height - outH / scale) * 0.5f;
				float4 sensorContent = new(-x0 * scale, -y0 * scale, (width - x0) * scale, (height - y0) * scale);
				PinholeCamera sensorIntrinsics = new(fx * scale, fy * scale, (cx - x0 + 0.5f) * scale - 0.5f, (cy - y0 + 0.5f) * scale - 0.5f);

				// XFeat's descriptors aren't rotation-invariant: a phone held upright sees its scene
				// a quarter turn off, so every frame is turned to keep gravity pointing down the image
				Quaternion sensorRotation = head.rotation * lensPose.rotation;
				int quarterTurns = QuarterTurnsToUpright(sensorRotation);

				new ResampleJob
				{
					src = source,
					srcStride = sourceStride,
					srcOffsetX = sourceOffsetX,
					srcOffsetY = sourceOffsetY,
					srcWidth = width,
					srcHeight = height,
					x0 = x0,
					y0 = y0,
					scale = scale,
					quarterTurns = quarterTurns,
					dst = into.pixels,
				}.Schedule(outH, 16).Complete();

				into.content = TurnContent(sensorContent, quarterTurns);
				into.intrinsics = TurnIntrinsics(sensorIntrinsics, quarterTurns);
				into.timestampNs = timestampNs;
				into.cameraInTrackingSpace = new Pose(head.position + head.rotation * lensPose.position,
					sensorRotation * Quaternion.AngleAxis(90 * quarterTurns, Vector3.forward));

				Transform space = MainXRRig.TrackingSpace;
				into.trackingSpace = new Pose(space.position, space.rotation);
				into.cameraInWorld = new Pose(space.TransformPoint(into.cameraInTrackingSpace.position),
					space.rotation * into.cameraInTrackingSpace.rotation);
				return true;
			}
			finally
			{
				if (gotImage) image.Dispose();
			}
		}

		// a turn of 90° about the view axis takes the camera's right to its up (Unity's z rotation),
		// so the turned camera's up is, per turn: up, -right, -up, right
		private static int QuarterTurnsToUpright(Quaternion sensorInTrackingSpace)
		{
			float upAlongGravity = (sensorInTrackingSpace * Vector3.up).y;
			float rightAlongGravity = (sensorInTrackingSpace * Vector3.right).y;
			if (math.abs(upAlongGravity) >= math.abs(rightAlongGravity)) return upAlongGravity >= 0 ? 0 : 2;
			return rightAlongGravity >= 0 ? 3 : 1;
		}

		// output pixel (x, y) of the turned image samples sensor-oriented pixel (u, v):
		// one turn: x = N-1-v, y = u
		private static PinholeCamera TurnIntrinsics(PinholeCamera c, int quarterTurns)
		{
			const float last = XFeatExtractor.Width - 1;
			return quarterTurns switch
			{
				1 => new PinholeCamera(c.fy, c.fx, last - c.cy, c.cx),
				2 => new PinholeCamera(c.fx, c.fy, last - c.cx, last - c.cy),
				3 => new PinholeCamera(c.fy, c.fx, c.cy, last - c.cx),
				_ => c,
			};
		}

		private static float4 TurnContent(float4 c, int quarterTurns)
		{
			const float size = XFeatExtractor.Width;
			return quarterTurns switch
			{
				1 => new float4(size - c.w, c.x, size - c.y, c.z),
				2 => new float4(size - c.z, size - c.w, size - c.x, size - c.y),
				3 => new float4(c.y, size - c.z, c.w, size - c.x),
				_ => c,
			};
		}

		// the luma plane itself for the sensor's YUV, valid until the image is disposed; the
		// simulator's color readback converted
		private bool TryGetGrayscale(XRCpuImage image, out NativeArray<byte> pixels, out int stride, out int width, out int height, out bool rendered)
		{
			width = image.width;
			height = image.height;
			rendered = false;
			pixels = default;
			stride = width;

			switch (image.format)
			{
				case XRCpuImage.Format.AndroidYuv420_888:
				case XRCpuImage.Format.IosYpCbCr420_8BiPlanarFullRange:
				case XRCpuImage.Format.OneComponent8:
				{
					XRCpuImage.Plane luma = image.GetPlane(0);
					if (luma.pixelStride != 1) return false;
					pixels = luma.data;
					stride = luma.rowStride;
					return true;
				}

				case XRCpuImage.Format.BGRA32:
				case XRCpuImage.Format.RGBA32:
					rendered = true;
					EnsureGray(width * height);
					image.ConvertToGrayscale(gray, simulatorMirrorY);
					pixels = gray;
					return true;

				default:
					Debug.LogWarning($"[{nameof(PassthroughCapture)}] unsupported camera image format {image.format}");
					return false;
			}
		}

		private void EnsureGray(int size)
		{
			if (gray.IsCreated && gray.Length == size) return;
			if (gray.IsCreated) gray.Dispose();
			gray = new NativeArray<byte>(size, Allocator.Persistent);
		}

		public void Dispose()
		{
			if (gray.IsCreated) gray.Dispose();
		}

		// bilinear, which at the usual 2:1 is exactly a 2×2 box average
		[BurstCompile]
		private struct ResampleJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<byte> src;
			public int srcStride, srcOffsetX, srcOffsetY, srcWidth, srcHeight;
			public float x0, y0;
			public float scale;
			public int quarterTurns;
			[WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> dst;

			public void Execute(int y)
			{
				const int w = XFeatExtractor.Width, last = w - 1;
				for (int x = 0; x < w; x++)
				{
					int2 sensorPixel = quarterTurns switch
					{
						1 => new int2(y, last - x),
						2 => new int2(last - x, last - y),
						3 => new int2(last - y, x),
						_ => new int2(x, y),
					};
					float sy = math.clamp((sensorPixel.y + 0.5f) / scale - 0.5f + y0, 0, srcHeight - 1);
					int iy = math.min((int)sy, srcHeight - 2);
					float fy = sy - iy;
					float sx = math.clamp((sensorPixel.x + 0.5f) / scale - 0.5f + x0, 0, srcWidth - 1);
					int ix = math.min((int)sx, srcWidth - 2);
					float fx = sx - ix;
					int i = (iy + srcOffsetY) * srcStride + srcOffsetX + ix;
					float top = math.lerp(src[i], src[i + 1], fx);
					float bottom = math.lerp(src[i + srcStride], src[i + srcStride + 1], fx);
					dst[y * w + x] = math.lerp(top, bottom, fy) * (1f / 255f);
				}
			}
		}
	}
}
