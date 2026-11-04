using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// XFeat's own matcher: mutual nearest neighbors by descriptor cosine, in a Burst job
	/// on worker threads. Enough between nearby views of one stretch of tracking, which is
	/// all track building asks for; LighterGlue is kept for the wide-baseline matches of
	/// relocalization and loop closure. Bad matches that slip through are dropped by each
	/// landmark's triangulation.
	/// </summary>
	public static class MutualNearestMatcher
	{
		/// <summary>
		/// For every feature of <paramref name="a"/>, the index of its mutual nearest
		/// neighbor in <paramref name="b"/> with a cosine of at least
		/// <paramref name="minCosine"/>, or -1; and that cosine as the confidence.
		/// </summary>
		public static async Awaitable<(int[] matches, float[] confidence)> MatchAsync(FeatureSet a, FeatureSet b, float minCosine = 0.82f)
		{
			if (a == null || b == null || a.Count == 0 || b.Count == 0) return (null, null);

			// persistent: the job may outlive the four frames a TempJob allocation is allowed
			NativeArray<half> descA = new(a.descriptors, Allocator.Persistent);
			NativeArray<float4> rowsB = new(b.Count * Widths, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			NativeArray<int> bestForA = new(a.Count, Allocator.Persistent);
			NativeArray<float> bestSimForA = new(a.Count, Allocator.Persistent);
			NativeArray<int> bestForB = new(b.Count, Allocator.Persistent);
			NativeArray<float> bestSimForB = new(b.Count, Allocator.Persistent);
			try
			{
				NativeArray<half> descB = new(b.descriptors, Allocator.Persistent);
				JobHandle prepared = new PrepareJob { descriptors = descB, rows = rowsB, best = bestForB, bestSimilarity = bestSimForB }.Schedule();
				descB.Dispose(prepared);

				// a slice of rows a frame: one job holding a worker for tens of milliseconds
				// holds up the engine's own jobs behind it, and the frame with them
				JobHandle slice = prepared;
				for (int row = 0; row < a.Count; row += RowsPerFrame)
				{
					slice = new NearestSliceJob
					{
						descA = descA, rowsB = rowsB, countB = b.Count, first = row, end = math.min(row + RowsPerFrame, a.Count),
						bestForA = bestForA, bestSimForA = bestSimForA, bestForB = bestForB, bestSimForB = bestSimForB,
					}.Schedule(slice);
					JobHandle.ScheduleBatchedJobs();
					while (!slice.IsCompleted) await Awaitable.NextFrameAsync();
					slice.Complete();
				}

				int[] matches = new int[a.Count];
				float[] confidence = new float[a.Count];
				for (int i = 0; i < a.Count; i++)
				{
					int j = bestForA[i];
					bool mutual = j >= 0 && bestForB[j] == i && bestSimForA[i] >= minCosine;
					matches[i] = mutual ? j : -1;
					confidence[i] = mutual ? bestSimForA[i] : 0;
				}

				return (matches, confidence);
			}
			finally
			{
				descA.Dispose();
				rowsB.Dispose();
				bestForA.Dispose();
				bestSimForA.Dispose();
				bestForB.Dispose();
				bestSimForB.Dispose();
			}
		}

		// rows of a matched per frame: ~64 × 1000 × 64 multiply-adds, a millisecond or two
		private const int RowsPerFrame = 64;
		private const int Widths = FeatureSet.DescriptorSize / 4;

		// b's descriptors widened once, not once per multiply; and nothing yet the best of any row of it
		[BurstCompile]
		private struct PrepareJob : IJob
		{
			[ReadOnly] public NativeArray<half> descriptors;
			[WriteOnly] public NativeArray<float4> rows;
			[WriteOnly] public NativeArray<int> best;
			[WriteOnly] public NativeArray<float> bestSimilarity;

			public void Execute()
			{
				for (int i = 0; i < rows.Length; i++)
				{
					int at = i * 4;
					rows[i] = new float4(descriptors[at], descriptors[at + 1], descriptors[at + 2], descriptors[at + 3]);
				}

				for (int j = 0; j < best.Length; j++)
				{
					best[j] = -1;
					bestSimilarity[j] = -2;
				}
			}
		}

		// each row of a against all of b once, both directions' best kept in the one pass
		[BurstCompile(FloatMode = FloatMode.Fast)]
		private struct NearestSliceJob : IJob
		{
			[ReadOnly] public NativeArray<half> descA;
			[ReadOnly] public NativeArray<float4> rowsB;
			public int countB, first, end;
			[WriteOnly] public NativeArray<int> bestForA;
			[WriteOnly] public NativeArray<float> bestSimForA;
			[WriteOnly] public NativeArray<int> bestForB;
			public NativeArray<float> bestSimForB;

			public void Execute()
			{
				NativeArray<float4> rowA = new(Widths, Allocator.Temp);
				for (int i = first; i < end; i++)
				{
					int ai = i * FeatureSet.DescriptorSize;
					for (int c = 0; c < Widths; c++)
					{
						int at = ai + c * 4;
						rowA[c] = new float4(descA[at], descA[at + 1], descA[at + 2], descA[at + 3]);
					}

					float best = -2;
					int bestJ = -1;
					for (int j = 0; j < countB; j++)
					{
						int rj = j * Widths;
						float4 acc = 0;
						for (int c = 0; c < Widths; c++) acc += rowA[c] * rowsB[rj + c];
						float dot = math.csum(acc);
						if (dot > best)
						{
							best = dot;
							bestJ = j;
						}

						if (dot > bestSimForB[j])
						{
							bestSimForB[j] = dot;
							bestForB[j] = i;
						}
					}

					bestForA[i] = bestJ;
					bestSimForA[i] = best;
				}
			}
		}
	}
}
