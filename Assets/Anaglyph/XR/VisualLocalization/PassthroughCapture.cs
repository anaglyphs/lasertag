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

		/// <summary>XR Simulation's rendered readback rather than a physical sensor.</summary>
		public bool rendered;

		public void Dispose()
		{
			if (pixels.IsCreated) pixels.Dispose();
		}
	}

	/// <summary>
	/// Grabs the passthrough camera's latest CPU image through
	/// <see cref="MetaPassthroughCamera"/> (or AR Foundation's generic image), turns it
	/// into the extractor's 640×480 grayscale with matching intrinsics, and places the
	/// lens in the world at the frame's capture time through <see cref="HeadPoseHistory"/>.
	/// The image is center-cropped to 4:3 first, so any sensor mode works.
	/// </summary>
	public sealed class PassthroughCapture : IDisposable
	{
		private readonly ARCameraManager cameraManager;
		private NativeArray<byte> gray;
		private Pose lensPose = Pose.identity;
		private bool lookedForLens;

		/// <summary>XR Simulation's readback has its first row at the bottom.</summary>
		public bool simulatorMirrorY = true;

		public PassthroughCapture(ARCameraManager cameraManager)
		{
			this.cameraManager = cameraManager;
		}

		/// <summary>
		/// Fills <paramref name="into"/> from the camera's latest image. False when no image,
		/// intrinsics or historical head pose is available for it.
		/// </summary>
		/// <param name="fallbackTimestampNs">The frame event's timestamp, used when the per-eye API is absent.</param>
		public bool TryCapture(CapturedImage into, long? fallbackTimestampNs = null)
		{
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
					if (!gotImage || !fallbackTimestampNs.HasValue) return false;
					timestampNs = fallbackTimestampNs.Value;
					lens = LensPosition.Unknown;
				}

				if (!HeadPoseHistory.Instance.TryGetLocalPose(timestampNs, out Pose head)) return false;

				// motion blur: how far the head turned over the 30 ms before capture
				into.angularSpeed = HeadPoseHistory.Instance.TryGetLocalPose(timestampNs - 30_000_000, out Pose before)
					? Quaternion.Angle(before.rotation, head.rotation) / 0.03f
					: float.PositiveInfinity;

				if (Application.platform == RuntimePlatform.Android && !lookedForLens)
				{
					lookedForLens = true;
					if (!AndroidCamExtrinsicsHelper.TryFindCameraExtrinsics(cameraManager.currentFacingDirection,
						    image.dimensions, lens, out lensPose, out _))
						Debug.LogError($"[{nameof(PassthroughCapture)}] no camera extrinsics found: poses will be off by the lens to head offset");
				}

				if (!ToGrayscale(image, out int width, out int height, out into.rendered)) return false;

				// intrinsics of the full image; XR Simulation reports none, so take its render camera's
				float fx = intrinsics.focalLength.x, fy = intrinsics.focalLength.y;
				float cx = intrinsics.principalPoint.x, cy = intrinsics.principalPoint.y;
				if (fy <= 0)
				{
					fy = fx = height * 0.5f * MainXRRig.Camera.projectionMatrix.m11;
					cx = width * 0.5f;
					cy = height * 0.5f;
				}
				else if (intrinsics.resolution.x > 0 && intrinsics.resolution.x != width)
				{
					float s = (float)width / intrinsics.resolution.x;
					fx *= s;
					fy *= s;
					cx *= s;
					cy *= s;
				}

				// the largest centered 4:3 rect, scaled to the model size
				const int outW = XFeatExtractor.Width, outH = XFeatExtractor.Height;
				int cropW = math.min(width, height * outW / outH);
				int cropH = cropW * outH / outW;
				int x0 = (width - cropW) / 2, y0 = (height - cropH) / 2;
				float scale = (float)outW / cropW;

				new ResampleJob
				{
					src = gray,
					srcWidth = width,
					srcHeight = height,
					x0 = x0,
					y0 = y0,
					scale = scale,
					dst = into.pixels,
				}.Schedule(outH, 16).Complete();

				into.intrinsics = new PinholeCamera(fx * scale, fy * scale, (cx - x0 + 0.5f) * scale - 0.5f, (cy - y0 + 0.5f) * scale - 0.5f);
				into.timestampNs = timestampNs;
				into.cameraInTrackingSpace = new Pose(head.position + head.rotation * lensPose.position, head.rotation * lensPose.rotation);

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

		// the luma plane as is for the sensor's YUV; the simulator's color readback converted
		private bool ToGrayscale(XRCpuImage image, out int width, out int height, out bool rendered)
		{
			width = image.width;
			height = image.height;
			rendered = false;
			EnsureGray(width * height);

			switch (image.format)
			{
				case XRCpuImage.Format.AndroidYuv420_888:
				case XRCpuImage.Format.OneComponent8:
				{
					XRCpuImage.Plane luma = image.GetPlane(0);
					if (luma.pixelStride != 1) return false;
					for (int y = 0; y < height; y++)
						NativeArray<byte>.Copy(luma.data, y * luma.rowStride, gray, y * width, width);
					return true;
				}

				case XRCpuImage.Format.BGRA32:
				case XRCpuImage.Format.RGBA32:
					rendered = true;
					image.ConvertToGrayscale(gray, simulatorMirrorY);
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
			public int srcWidth, srcHeight, x0, y0;
			public float scale;
			[WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> dst;

			public void Execute(int y)
			{
				const int w = XFeatExtractor.Width;
				float sy = math.clamp((y + 0.5f) / scale - 0.5f + y0, 0, srcHeight - 1);
				int iy = math.min((int)sy, srcHeight - 2);
				float fy = sy - iy;
				for (int x = 0; x < w; x++)
				{
					float sx = math.clamp((x + 0.5f) / scale - 0.5f + x0, 0, srcWidth - 1);
					int ix = math.min((int)sx, srcWidth - 2);
					float fx = sx - ix;
					int i = iy * srcWidth + ix;
					float top = math.lerp(src[i], src[i + 1], fx);
					float bottom = math.lerp(src[i + srcWidth], src[i + srcWidth + 1], fx);
					dst[y * w + x] = math.lerp(top, bottom, fy) * (1f / 255f);
				}
			}
		}
	}
}
