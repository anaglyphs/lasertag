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
			NativeArray<half> descB = new(b.descriptors, Allocator.Persistent);
			NativeArray<int> bestForA = new(a.Count, Allocator.Persistent);
			NativeArray<float> bestSimForA = new(a.Count, Allocator.Persistent);
			NativeArray<int> bestForB = new(b.Count, Allocator.Persistent);
			try
			{
				JobHandle forA = new NearestJob { from = descA, to = descB, toCount = b.Count, best = bestForA, bestSimilarity = bestSimForA }.Schedule(a.Count, 32);
				// the reverse direction only needs the index
				NativeArray<float> bestSimForB = new(b.Count, Allocator.Persistent);
				JobHandle forB = new NearestJob { from = descB, to = descA, toCount = a.Count, best = bestForB, bestSimilarity = bestSimForB }.Schedule(b.Count, 32);
				JobHandle both = JobHandle.CombineDependencies(forA, forB);
				JobHandle.ScheduleBatchedJobs();
				while (!both.IsCompleted) await Awaitable.NextFrameAsync();
				both.Complete();
				bestSimForB.Dispose();

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
				descB.Dispose();
				bestForA.Dispose();
				bestSimForA.Dispose();
				bestForB.Dispose();
			}
		}

		[BurstCompile(FloatMode = FloatMode.Fast)]
		private struct NearestJob : IJobParallelFor
		{
			[ReadOnly] public NativeArray<half> from;
			[ReadOnly] public NativeArray<half> to;
			public int toCount;
			[WriteOnly] public NativeArray<int> best;
			[WriteOnly] public NativeArray<float> bestSimilarity;

			public void Execute(int i)
			{
				const int d = FeatureSet.DescriptorSize;
				int fi = i * d;
				// the row being matched, widened once
				NativeArray<float4> row = new(d / 4, Allocator.Temp);
				for (int c = 0; c < d; c += 4) row[c / 4] = new float4(from[fi + c], from[fi + c + 1], from[fi + c + 2], from[fi + c + 3]);

				float bestDot = -2;
				int bestJ = -1;
				for (int j = 0; j < toCount; j++)
				{
					int tj = j * d;
					float4 acc = 0;
					for (int c = 0; c < d; c += 4)
						acc += row[c / 4] * new float4(to[tj + c], to[tj + c + 1], to[tj + c + 2], to[tj + c + 3]);
					float dot = math.csum(acc);
					if (dot <= bestDot) continue;
					bestDot = dot;
					bestJ = j;
				}

				best[i] = bestJ;
				bestSimilarity[i] = bestDot;
			}
		}
	}
}
