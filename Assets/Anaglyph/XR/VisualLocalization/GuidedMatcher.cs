using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// Matching for a query that comes with a camera prior (a revisit of this headset's own
	/// map, a cross-link against the authority's): the landmarks in view are projected
	/// through the prior into the query image, and each takes the query feature nearest in
	/// descriptor among those inside the window its true position may fall in, if that
	/// feature prefers it too. A few million cheap distance tests and a few tens of
	/// thousands of dot products in one Burst job, on a worker thread: no attention
	/// network, nothing on the GPU. LighterGlue stays for queries with no prior, which have
	/// to match the whole view against the whole view.
	/// </summary>
	public static class GuidedMatcher
	{
		/// <summary>
		/// For every target (a landmark projected to <paramref name="projected"/>[t], whose
		/// true position lies within <paramref name="windows"/>[t] pixels of it, with row t of
		/// <paramref name="descriptors"/>), the index of its mutual nearest query feature
		/// within that window with a cosine of at least <paramref name="minCosine"/>, beating
		/// the runner-up in its window by <paramref name="minMargin"/> (repeated structure gives
		/// near ties), or -1; and that cosine as the confidence.
		/// </summary>
		public static async Awaitable<(int[] matches, float[] confidence)> MatchAsync(FeatureSet query, float2[] projected, float[] windows,
			half[] descriptors, float minCosine, float minMargin = 0)
		{
			int targets = projected.Length;
			if (targets == 0) return (Array.Empty<int>(), Array.Empty<float>());
			if (query == null || query.Count == 0)
			{
				int[] none = new int[targets];
				Array.Fill(none, -1);
				return (none, new float[targets]);
			}

			// persistent: the job may outlive the four frames a TempJob allocation is allowed
			NativeArray<float2> queryPoints = new(query.keypoints, Allocator.Persistent);
			NativeArray<half> queryDesc = new(query.descriptors, Allocator.Persistent);
			NativeArray<float2> targetPoints = new(projected, Allocator.Persistent);
			NativeArray<float> targetWindows = new(windows, Allocator.Persistent);
			NativeArray<half> targetDesc = new(descriptors, Allocator.Persistent);
			NativeArray<int> matches = new(targets, Allocator.Persistent);
			NativeArray<float> confidence = new(targets, Allocator.Persistent);
			try
			{
				JobHandle handle = new GuidedMatchJob
				{
					queryPoints = queryPoints,
					queryDesc = queryDesc,
					queryCount = query.Count,
					targetPoints = targetPoints,
					targetWindows = targetWindows,
					targetDesc = targetDesc,
					minCosine = minCosine,
					minMargin = minMargin,
					matches = matches,
					confidence = confidence,
				}.Schedule();
				JobHandle.ScheduleBatchedJobs();
				while (!handle.IsCompleted) await Awaitable.NextFrameAsync();
				handle.Complete();
				return (matches.ToArray(), confidence.ToArray());
			}
			finally
			{
				queryPoints.Dispose();
				queryDesc.Dispose();
				targetPoints.Dispose();
				targetWindows.Dispose();
				targetDesc.Dispose();
				matches.Dispose();
				confidence.Dispose();
			}
		}

		// every target against every query feature inside its window, both directions' best
		// kept in the one pass; then the mutual pairs
		[BurstCompile(FloatMode = FloatMode.Fast)]
		private struct GuidedMatchJob : IJob
		{
			[ReadOnly] public NativeArray<float2> queryPoints;
			[ReadOnly] public NativeArray<half> queryDesc;
			public int queryCount;
			[ReadOnly] public NativeArray<float2> targetPoints;
			[ReadOnly] public NativeArray<float> targetWindows;
			[ReadOnly] public NativeArray<half> targetDesc;
			public float minCosine;
			public float minMargin;
			[WriteOnly] public NativeArray<int> matches;
			[WriteOnly] public NativeArray<float> confidence;

			public void Execute()
			{
				const int d = FeatureSet.DescriptorSize;
				int targets = targetPoints.Length;
				NativeArray<int> bestForTarget = new(targets, Allocator.Temp);
				NativeArray<float> bestSimTarget = new(targets, Allocator.Temp);
				NativeArray<float> secondSimTarget = new(targets, Allocator.Temp);
				NativeArray<int> bestForQuery = new(queryCount, Allocator.Temp);
				NativeArray<float> bestSimQuery = new(queryCount, Allocator.Temp);
				for (int i = 0; i < queryCount; i++)
				{
					bestForQuery[i] = -1;
					bestSimQuery[i] = -2;
				}

				NativeArray<float4> row = new(d / 4, Allocator.Temp);
				for (int t = 0; t < targets; t++)
				{
					float2 at = targetPoints[t];
					float window2 = targetWindows[t] * targetWindows[t];
					int td = t * d;
					int best = -1;
					float bestSim = -2, secondSim = -2;
					bool widened = false;
					for (int i = 0; i < queryCount; i++)
					{
						if (math.distancesq(at, queryPoints[i]) > window2) continue;
						if (!widened)
						{
							// the target's row widened once, only if anything lies in its window
							for (int c = 0; c < d; c += 4)
								row[c / 4] = new float4(targetDesc[td + c], targetDesc[td + c + 1], targetDesc[td + c + 2], targetDesc[td + c + 3]);
							widened = true;
						}

						int qd = i * d;
						float4 acc = 0;
						for (int c = 0; c < d; c += 4)
							acc += row[c / 4] * new float4(queryDesc[qd + c], queryDesc[qd + c + 1], queryDesc[qd + c + 2], queryDesc[qd + c + 3]);
						float dot = math.csum(acc);
						if (dot > bestSim)
						{
							secondSim = bestSim;
							bestSim = dot;
							best = i;
						}
						else if (dot > secondSim) secondSim = dot;

						if (dot > bestSimQuery[i])
						{
							bestSimQuery[i] = dot;
							bestForQuery[i] = t;
						}
					}

					bestForTarget[t] = best;
					bestSimTarget[t] = bestSim;
					secondSimTarget[t] = secondSim;
				}

				for (int t = 0; t < targets; t++)
				{
					int i = bestForTarget[t];
					bool mutual = i >= 0 && bestForQuery[i] == t && bestSimTarget[t] >= minCosine &&
					              bestSimTarget[t] - secondSimTarget[t] >= minMargin;
					matches[t] = mutual ? i : -1;
					confidence[t] = mutual ? bestSimTarget[t] : 0;
				}
			}
		}
	}
}
