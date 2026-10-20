using System;
using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.InferenceEngine;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// Sparse features of one image: XFeat keypoints in the model image's pixels
	/// (<see cref="XFeatExtractor.Width"/> × <see cref="XFeatExtractor.Height"/>, y down),
	/// strongest first, with unit 64-d descriptors.
	/// </summary>
	public sealed class FeatureSet
	{
		public const int DescriptorSize = 64;

		public int Count;
		public float2[] keypoints;
		public float[] scores;
		/// <summary><see cref="Count"/> × <see cref="DescriptorSize"/>, row per keypoint.</summary>
		public float[] descriptors;

		public FeatureSet(int count)
		{
			Count = count;
			keypoints = new float2[count];
			scores = new float[count];
			descriptors = new float[count * DescriptorSize];
		}

		/// <summary>The strongest <paramref name="count"/> features (they are stored strongest first).</summary>
		public FeatureSet Strongest(int count)
		{
			count = math.min(count, Count);
			FeatureSet result = new(count);
			Array.Copy(keypoints, result.keypoints, count);
			Array.Copy(scores, result.scores, count);
			Array.Copy(descriptors, result.descriptors, count * DescriptorSize);
			return result;
		}
	}

	internal static class XFeatModels
	{
		public static Model Load(string name)
		{
			ModelAsset asset = Resources.Load<ModelAsset>("XFeat/" + name);
			if (asset == null) throw new InvalidOperationException($"XFeat model Resources/XFeat/{name} is missing");
			return ModelLoader.Load(asset);
		}

		/// <summary>
		/// Schedules the worker's layers spread over <paramref name="frames"/> frames, so no
		/// one frame carries the whole network: on the GPU that keeps the dispatches from
		/// crowding out rendering, on the CPU the Burst jobs from crowding out the game.
		/// Spread by frames rather than layers per frame, since the networks differ a lot in
		/// layer count (XFeat ~50, LighterGlue ~700 mostly tiny ones). 1 or less schedules
		/// everything at once.
		/// </summary>
		public static async Awaitable ScheduleAsync(Worker worker, int layerCount, int frames)
		{
			if (frames <= 1)
			{
				worker.Schedule();
				return;
			}

			int perFrame = math.max(1, (layerCount + frames - 1) / frames);
			IEnumerator layers = worker.ScheduleIterable();
			int scheduled = 0;
			while (layers.MoveNext())
				if (++scheduled % perFrame == 0)
					await Awaitable.NextFrameAsync();
		}

	}

	/// <summary>
	/// XFeat sparse extraction. The network (see Tools~/export_xfeat.py) runs detection
	/// and 5×5 NMS on the GPU and returns the dense score map and descriptor field; the
	/// top-K and descriptor sampling run in a Burst job, since Inference Engine's GPU TopK
	/// over every pixel cost ~40× the whole network.
	/// </summary>
	public sealed class XFeatExtractor : IDisposable
	{
		public const int Width = 640, Height = 480, MaxKeypoints = 1024;
		private const int Cell = 8, FieldWidth = Width / Cell, FieldHeight = Height / Cell;

		private readonly Worker worker;
		private readonly Tensor<float> input;
		private readonly int layerCount;

		public bool IsBusy { get; private set; }
		public BackendType Backend { get; }

		/// <summary>Frames each extraction is spread over; 1 or less for all at once.</summary>
		public int FramesPerRun { get; set; }

		/// <param name="backend">GPUCompute, or CPU (Burst jobs on worker threads) to keep it off the render GPU.</param>
		public XFeatExtractor(BackendType backend = BackendType.GPUCompute, int framesPerRun = 1)
		{
			Backend = backend;
			FramesPerRun = framesPerRun;
			Model model = XFeatModels.Load("xfeat_dense");
			layerCount = model.layers.Count;
			worker = new Worker(model, backend);
			input = new Tensor<float>(new TensorShape(1, 1, Height, Width));
		}

		/// <summary>Features of a <see cref="Width"/>×<see cref="Height"/> grayscale image, 0..1, row 0 on top.</summary>
		public async Awaitable<FeatureSet> ExtractAsync(NativeArray<float> image)
		{
			if (IsBusy) return null;
			IsBusy = true;
			try
			{
				input.Upload(image);
				worker.SetInput(0, input);
				await XFeatModels.ScheduleAsync(worker, layerCount, FramesPerRun);

				using Tensor<float> score = await ((Tensor<float>)worker.PeekOutput("score")).ReadbackAndCloneAsync();
				using Tensor<float> feats = await ((Tensor<float>)worker.PeekOutput("feats")).ReadbackAndCloneAsync();
				return await SparsifyAsync(score, feats);
			}
			finally
			{
				IsBusy = false;
			}
		}

		// the tensors must outlive the job: the caller disposes them after this returns
		private static async Awaitable<FeatureSet> SparsifyAsync(Tensor<float> score, Tensor<float> feats)
		{
			const int d = FeatureSet.DescriptorSize;
			NativeArray<float2> keypoints = new(MaxKeypoints, Allocator.Persistent);
			NativeArray<float> scores = new(MaxKeypoints, Allocator.Persistent);
			NativeArray<float> descriptors = new(MaxKeypoints * d, Allocator.Persistent);
			NativeReference<int> count = new(Allocator.Persistent);
			try
			{
				JobHandle handle = new SparsifyJob
				{
					score = score.AsReadOnlyNativeArray(),
					feats = feats.AsReadOnlyNativeArray(),
					keypoints = keypoints,
					scores = scores,
					descriptors = descriptors,
					count = count,
				}.Schedule();
				JobHandle.ScheduleBatchedJobs();
				while (!handle.IsCompleted) await Awaitable.NextFrameAsync();
				handle.Complete();

				int n = count.Value;
				FeatureSet result = new(n);
				NativeArray<float2>.Copy(keypoints, result.keypoints, n);
				NativeArray<float>.Copy(scores, result.scores, n);
				NativeArray<float>.Copy(descriptors, result.descriptors, n * d);
				return result;
			}
			finally
			{
				keypoints.Dispose();
				scores.Dispose();
				descriptors.Dispose();
				count.Dispose();
			}
		}

		public void Dispose()
		{
			input?.Dispose();
			worker?.Dispose();
		}

		[BurstCompile]
		private struct SparsifyJob : IJob
		{
			[ReadOnly] public NativeArray<float>.ReadOnly score;
			[ReadOnly] public NativeArray<float>.ReadOnly feats;
			public NativeArray<float2> keypoints;
			public NativeArray<float> scores;
			public NativeArray<float> descriptors;
			public NativeReference<int> count;

			private struct Peak : IComparable<Peak>
			{
				public float score;
				public int index;

				// strongest first, ties to the earlier pixel
				public int CompareTo(Peak other) => other.score != score ? other.score.CompareTo(score) : index.CompareTo(other.index);
			}

			public void Execute()
			{
				NativeList<Peak> peaks = new(4096, Allocator.Temp);
				for (int i = 0; i < score.Length; i++)
					if (score[i] > 0) peaks.Add(new Peak { score = score[i], index = i });
				peaks.Sort();

				int n = math.min(MaxKeypoints, peaks.Length);
				for (int k = 0; k < n; k++)
				{
					Peak p = peaks[k];
					float2 pixel = new(p.index % Width, p.index / Width);
					keypoints[k] = pixel;
					scores[k] = p.score;
					SampleDescriptor(pixel, k * FeatureSet.DescriptorSize);
				}

				count.Value = n;
			}

			// grid_sample(bilinear, zeros padding, align_corners=False) at XFeat's grid
			// normalization 2x/(W-1)-1, then unit length
			private void SampleDescriptor(float2 pixel, int offset)
			{
				const int d = FeatureSet.DescriptorSize, plane = FieldWidth * FieldHeight;
				float fx = pixel.x * FieldWidth / (Width - 1) - 0.5f;
				float fy = pixel.y * FieldHeight / (Height - 1) - 0.5f;
				int x0 = (int)math.floor(fx), y0 = (int)math.floor(fy);
				float tx = fx - x0, ty = fy - y0;

				float sum = 0;
				for (int c = 0; c < d; c++)
				{
					int b = c * plane;
					float v = Tap(b, x0, y0) * (1 - tx) * (1 - ty) + Tap(b, x0 + 1, y0) * tx * (1 - ty) +
					          Tap(b, x0, y0 + 1) * (1 - tx) * ty + Tap(b, x0 + 1, y0 + 1) * tx * ty;
					descriptors[offset + c] = v;
					sum += v * v;
				}

				float inv = sum > 0 ? math.rsqrt(sum) : 0;
				for (int c = 0; c < d; c++) descriptors[offset + c] *= inv;
			}

			private float Tap(int planeBase, int x, int y) =>
				x < 0 || y < 0 || x >= FieldWidth || y >= FieldHeight ? 0 : feats[planeBase + y * FieldWidth + x];
		}
	}

	/// <summary>
	/// LighterGlue, XFeat's LightGlue: attends over both images' keypoints and returns
	/// mutual matches above a confidence. Both images must be in the extractor's
	/// <see cref="XFeatExtractor.Width"/>×<see cref="XFeatExtractor.Height"/> pixels; the
	/// positional encoding is normalized for that size inside the network.
	/// </summary>
	public sealed class LighterGlueMatcher : IDisposable
	{
		/// <summary>Below this many keypoints on either side there is nothing worth matching.</summary>
		public const int MinKeypoints = 16;

		private readonly Worker worker;
		private readonly int layerCount;

		public bool IsBusy { get; private set; }

		/// <summary>Frames each match is spread over; 1 or less for all at once.</summary>
		public int FramesPerRun { get; set; }

		public LighterGlueMatcher(BackendType backend = BackendType.GPUCompute, int framesPerRun = 1)
		{
			FramesPerRun = framesPerRun;
			Model model = XFeatModels.Load("lighterglue");
			layerCount = model.layers.Count;
			worker = new Worker(model, backend);
		}

		/// <summary>
		/// For every feature of <paramref name="a"/>, the index of its match in
		/// <paramref name="b"/> or -1, and the match confidence (0 when unmatched).
		/// Null when either side is too sparse, or the matcher is busy.
		/// </summary>
		public async Awaitable<(int[] matches, float[] confidence)> MatchAsync(FeatureSet a, FeatureSet b)
		{
			if (IsBusy || a == null || b == null || a.Count < MinKeypoints || b.Count < MinKeypoints) return (null, null);
			IsBusy = true;
			try
			{
				using Tensor<float> kpts0 = new(new TensorShape(a.Count, 2), Flatten(a.keypoints));
				using Tensor<float> desc0 = new(new TensorShape(a.Count, FeatureSet.DescriptorSize), a.descriptors);
				using Tensor<float> kpts1 = new(new TensorShape(b.Count, 2), Flatten(b.keypoints));
				using Tensor<float> desc1 = new(new TensorShape(b.Count, FeatureSet.DescriptorSize), b.descriptors);

				worker.SetInput("kpts0", kpts0);
				worker.SetInput("desc0", desc0);
				worker.SetInput("kpts1", kpts1);
				worker.SetInput("desc1", desc1);
				await XFeatModels.ScheduleAsync(worker, layerCount, FramesPerRun);

				using Tensor<float> matches = await ((Tensor<float>)worker.PeekOutput("matches0")).ReadbackAndCloneAsync();
				using Tensor<float> scores = await ((Tensor<float>)worker.PeekOutput("mscores0")).ReadbackAndCloneAsync();

				return ToMatches(matches, scores, a.Count);
			}
			finally
			{
				IsBusy = false;
			}
		}

		private static (int[] matches, float[] confidence) ToMatches(Tensor<float> matches, Tensor<float> scores, int count)
		{
			ReadOnlySpan<float> m = matches.AsReadOnlySpan(), s = scores.AsReadOnlySpan();
			int[] result = new int[count];
			float[] confidence = new float[count];
			for (int i = 0; i < count; i++)
			{
				result[i] = (int)m[i];
				confidence[i] = s[i];
			}

			return (result, confidence);
		}

		private static float[] Flatten(float2[] points)
		{
			float[] flat = new float[points.Length * 2];
			for (int i = 0; i < points.Length; i++)
			{
				flat[2 * i] = points[i].x;
				flat[2 * i + 1] = points[i].y;
			}

			return flat;
		}

		public void Dispose() => worker?.Dispose();
	}
}
