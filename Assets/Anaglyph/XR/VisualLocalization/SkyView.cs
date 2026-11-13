using System;
using Unity.Burst;
using Unity.Collections;
using Unity.InferenceEngine;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// Which directions around the headset are open sky and which are something solid,
	/// however far: a sky segmentation of the passthrough camera's frames, gathered into a
	/// coarse map of world directions as the player looks around. It sees buildings and
	/// trees far past the depth sensor's reach, but not how far they are. A network that
	/// also estimates distance has each look's estimate handed on through
	/// <see cref="LookedWithDistance"/>.
	/// </summary>
	public class SkyView : MonoBehaviour
	{
		public static SkyView Instance { get; private set; }

		/// <summary>
		/// What one look saw, as square maps squeezed across the frame, row 0 on top, valid
		/// only during the call. A point across the frame (0..1 each way) lies at image
		/// pixel content.xy + across * (content.zw - content.xy) - 0.5.
		/// </summary>
		/// <param name="skyShare">How much of each cell is sky, 0..1.</param>
		/// <param name="logNearness">Natural log of one over each cell's depth along the camera's forward axis, up to one constant shared by the whole look.</param>
		public delegate void DistanceLook(ReadOnlySpan<float> skyShare, ReadOnlySpan<float> logNearness, int cellsPerSide,
			Pose camera, PinholeCamera intrinsics, float4 content);

		/// <summary>Raised after each look, when the network estimates distance.</summary>
		public event DistanceLook LookedWithDistance;

		private const int AzimuthCells = 24, ElevationCells = 7;
		private const float CellDegrees = 15f, LowestElevationDegrees = -15f;
		private const int MaskSamplesPerSide = 64, MinSamplesPerCell = 4;

		[Tooltip("A sky segmentation network: one square grayscale \"image\" in, a square \"sky\" map out, the sky share per pixel or per block of pixels. It may also give \"log_nearness\", a distance estimate on the same grid")]
		[SerializeField] private ModelAsset model;
		[Tooltip("Where the network runs: GPUCompute, or CPU (Burst jobs on worker threads)")]
		[SerializeField] private BackendType backend = BackendType.GPUCompute;
		[Tooltip("Frames each segmentation is spread over, so no one frame carries the network")]
		[SerializeField] private int framesPerLook = 4;
		[SerializeField] private float secondsBetweenLooks = 1f;
		[Tooltip("The head must have turned or walked this far since the last look: the same view says nothing new")]
		[SerializeField] private float degreesTurnedForNewLook = 20f;
		[SerializeField] private float metersWalkedForNewLook = 3f;
		[Tooltip("No look is taken while the head turns faster than this, °/s")]
		[SerializeField] private float maxAngularSpeed = 90f;
		[Tooltip("A direction seen from farther away than this counts as unseen: what lay that way from there lies elsewhere from here")]
		[SerializeField] private float metersUntilForgotten = 15f;
		[Tooltip("How far a new look moves a direction already seen towards what it shows")]
		[SerializeField, Range(0.05f, 1f)] private float newLookWeight = 0.5f;

		private readonly float[] skyShare = new float[AzimuthCells * ElevationCells];
		private readonly bool[] seen = new bool[AzimuthCells * ElevationCells];
		private readonly Vector3[] seenFrom = new Vector3[AzimuthCells * ElevationCells];
		private readonly float[] lookSum = new float[AzimuthCells * ElevationCells];
		private readonly int[] lookSamples = new int[AzimuthCells * ElevationCells];

		private SkySegmenter segmenter;
		private bool looking, hasLooked;
		private float nextLookTime;
		private Pose lastLookCamera;

		/// <summary>Segmentations gathered since this was enabled.</summary>
		public int LookCount { get; private set; }

		private void Awake() => Instance = this;

		private void Start()
		{
			try
			{
				segmenter = new SkySegmenter(model, backend);
				Debug.Log($"[{nameof(SkyView)}] {model.name} at {segmenter.Size}×{segmenter.Size}, {segmenter.LayerCount} layers on {backend} over {framesPerLook} frames");
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
				enabled = false;
			}
		}

		private void OnDisable()
		{
			Array.Clear(seen, 0, seen.Length);
			hasLooked = false;
			LookCount = 0;
		}

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
			// a look in flight still holds the network, and lets go of it when it ends
			if (!looking) segmenter?.Dispose();
		}

		/// <summary>
		/// How much of what lies in <paramref name="direction"/> is sky, 0..1. False when the
		/// camera hasn't seen that way from near <paramref name="from"/>.
		/// </summary>
		public bool TryGetSkyShare(Vector3 from, Vector3 direction, out float share)
		{
			share = 0;
			if (!TryGetCell(direction, out int cell) || !seen[cell]) return false;
			if ((seenFrom[cell] - from).sqrMagnitude > metersUntilForgotten * metersUntilForgotten) return false;
			share = skyShare[cell];
			return true;
		}

		private void Update()
		{
			if (looking || Time.unscaledTime < nextLookTime) return;

			VisualLocalizer localizer = VisualLocalizer.Instance;
			if (localizer == null || !localizer.IsAvailable || localizer.Urgent || MainXRRig.Instance == null) return;

			Transform head = MainXRRig.Camera.transform;
			if (hasLooked && Vector3.Distance(head.position, lastLookCamera.position) < metersWalkedForNewLook &&
			    Quaternion.Angle(head.rotation, lastLookCamera.rotation) < degreesTurnedForNewLook) return;

			Look(localizer);
		}

		private async void Look(VisualLocalizer localizer)
		{
			looking = true;
			try
			{
				Pose camera = default;
				PinholeCamera intrinsics = default;
				float4 content = default;
				bool captured = await localizer.ReadFrameAsync(maxAngularSpeed, image =>
				{
					camera = image.cameraInWorld;
					intrinsics = image.intrinsics;
					content = image.content;
					segmenter.SetImage(image.pixels, XFeatExtractor.Width, XFeatExtractor.Height, image.content);
				});
				if (!captured || !this) return;

				using Tensor<float> sky = await segmenter.SegmentAsync(framesPerLook);
				if (!this) return;

				Gather(sky.AsReadOnlySpan(), sky.shape[3], camera, intrinsics, content);
				lastLookCamera = camera;
				hasLooked = true;
				LookCount++;

				if (!segmenter.EstimatesDistance || LookedWithDistance == null) return;
				using Tensor<float> logNearness = await segmenter.ReadLogNearnessAsync();
				if (!this) return;
				LookedWithDistance?.Invoke(sky.AsReadOnlySpan(), logNearness.AsReadOnlySpan(), sky.shape[3], camera, intrinsics, content);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
				if (this) enabled = false;
			}
			finally
			{
				looking = false;
				nextLookTime = Time.unscaledTime + secondsBetweenLooks;
				if (!this) segmenter.Dispose();
			}
		}

		// every direction the frame covers takes the mean of the mask where it looks that way,
		// sampled evenly across the frame however coarse or fine the mask is
		private void Gather(ReadOnlySpan<float> sky, int size, Pose camera, PinholeCamera intrinsics, float4 content)
		{
			Array.Clear(lookSum, 0, lookSum.Length);
			Array.Clear(lookSamples, 0, lookSamples.Length);

			for (int sampleY = 0; sampleY < MaskSamplesPerSide; sampleY++)
			for (int sampleX = 0; sampleX < MaskSamplesPerSide; sampleX++)
			{
				float2 acrossFrame = (new float2(sampleX, sampleY) + 0.5f) / MaskSamplesPerSide;
				float2 imagePixel = content.xy + acrossFrame * (content.zw - content.xy) - 0.5f;
				Vector3 direction = camera.rotation * intrinsics.Ray(imagePixel);
				if (!TryGetCell(direction, out int cell)) continue;
				int2 maskPixel = math.min((int2)(acrossFrame * size), size - 1);
				lookSum[cell] += sky[maskPixel.y * size + maskPixel.x];
				lookSamples[cell]++;
			}

			for (int cell = 0; cell < lookSum.Length; cell++)
			{
				if (lookSamples[cell] < MinSamplesPerCell) continue;
				float lookShare = lookSum[cell] / lookSamples[cell];
				bool seenFromNearby = seen[cell] &&
				                      (seenFrom[cell] - camera.position).sqrMagnitude < metersUntilForgotten * metersUntilForgotten;
				skyShare[cell] = seenFromNearby ? Mathf.Lerp(skyShare[cell], lookShare, newLookWeight) : lookShare;
				seenFrom[cell] = camera.position;
				seen[cell] = true;
			}
		}

		private static bool TryGetCell(Vector3 direction, out int cell)
		{
			cell = 0;
			direction.Normalize();
			float elevation = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
			int row = Mathf.FloorToInt((elevation - LowestElevationDegrees) / CellDegrees);
			if (row < 0) return false;
			row = Mathf.Min(row, ElevationCells - 1);

			float azimuth = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
			int column = Mathf.FloorToInt(Mathf.Repeat(azimuth, 360f) / CellDegrees) % AzimuthCells;
			cell = row * AzimuthCells + column;
			return true;
		}
	}

	/// <summary>
	/// A sky segmentation network: a square grayscale image in, a square map of sky share
	/// out, per pixel or per block of pixels. Some also estimate distance on the same grid.
	/// </summary>
	public sealed class SkySegmenter : IDisposable
	{
		private const string LogNearnessOutput = "log_nearness";

		private readonly Worker worker;
		private readonly CommandBuffer commands;
		private readonly Tensor<float> input;
		private NativeArray<float> pixels;

		/// <summary>Side of the network's square image, pixels.</summary>
		public int Size { get; }
		public int LayerCount { get; }
		public bool EstimatesDistance { get; }

		public SkySegmenter(ModelAsset asset, BackendType backend = BackendType.GPUCompute)
		{
			if (asset == null) throw new InvalidOperationException("No sky segmentation model is assigned");
			Model model = ModelLoader.Load(asset);
			TensorShape inputShape = model.inputs[0].shape.ToTensorShape();
			Size = inputShape[3];
			LayerCount = model.layers.Count;
			EstimatesDistance = model.outputs.Exists(output => output.name == LogNearnessOutput);
			worker = new Worker(model, backend);
			commands = XFeatModels.CommandsFor(backend, "SkySegmenter");
			input = new Tensor<float>(inputShape);
			pixels = new NativeArray<float>(Size * Size, Allocator.Persistent);
		}

		/// <summary>
		/// The image to segment: the part of a grayscale image (0..1, row 0 on top) inside
		/// <paramref name="content"/> (min x, min y, max x, max y), squeezed to the network's square.
		/// </summary>
		public void SetImage(NativeArray<float> image, int width, int height, float4 content)
		{
			new ShrinkJob
			{
				source = image,
				sourceWidth = width,
				sourceHeight = height,
				content = content,
				size = Size,
				shrunk = pixels,
			}.Schedule(Size, 8).Complete();
		}

		/// <summary>Sky share across the set image, a square map as fine as the network makes it, row 0 on top. The caller disposes it.</summary>
		public async Awaitable<Tensor<float>> SegmentAsync(int frames)
		{
			input.Upload(pixels);
			worker.SetInput(0, input);
			await XFeatModels.ScheduleAsync(worker, commands, LayerCount, frames);
			return await ((Tensor<float>)worker.PeekOutput("sky")).ReadbackAndCloneAsync();
		}

		/// <summary>
		/// The latest segmentation's distance estimate, on the sky map's grid: the natural log
		/// of one over each cell's depth, up to one constant shared by the whole image. The
		/// caller disposes it.
		/// </summary>
		public Awaitable<Tensor<float>> ReadLogNearnessAsync() =>
			((Tensor<float>)worker.PeekOutput(LogNearnessOutput)).ReadbackAndCloneAsync();

		public void Dispose()
		{
			input?.Dispose();
			worker?.Dispose();
			commands?.Release();
			if (pixels.IsCreated) pixels.Dispose();
		}

		// each output pixel is the mean of the source pixels it covers
		[BurstCompile]
		private struct ShrinkJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<float> source;
			public int sourceWidth, sourceHeight;
			public float4 content;
			public int size;
			[WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> shrunk;

			public void Execute(int y)
			{
				float2 sourcePerPixel = (content.zw - content.xy) / size;
				int y0 = math.clamp((int)(content.y + y * sourcePerPixel.y), 0, sourceHeight - 1);
				int y1 = math.clamp((int)math.ceil(content.y + (y + 1) * sourcePerPixel.y), y0 + 1, sourceHeight);
				for (int x = 0; x < size; x++)
				{
					int x0 = math.clamp((int)(content.x + x * sourcePerPixel.x), 0, sourceWidth - 1);
					int x1 = math.clamp((int)math.ceil(content.x + (x + 1) * sourcePerPixel.x), x0 + 1, sourceWidth);
					float sum = 0;
					for (int sy = y0; sy < y1; sy++)
					for (int sx = x0; sx < x1; sx++)
						sum += source[sy * sourceWidth + sx];
					shrunk[y * size + x] = sum / ((y1 - y0) * (x1 - x0));
				}
			}
		}
	}
}
