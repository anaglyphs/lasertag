using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Anaglyph.GPU;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XR.DepthKit.EnvScanning.Registration
{
	/// <summary>A gravity-aligned rigid transform: p' = rotateY(p, yaw) + translation.</summary>
	[Serializable]
	public struct Pose4
	{
		public float3 translation;
		public float yaw; // radians about +Y, Unity's sense

		public Pose4(float3 translation, float yaw)
		{
			this.translation = translation;
			this.yaw = yaw;
		}

		public static Pose4 Identity => new(float3.zero, 0);

		public quaternion Rotation => quaternion.RotateY(yaw);
		public Pose AsPose => new(translation, Rotation);
		public float4x4 Matrix => float4x4.TRS(translation, Rotation, 1);

		public float3 Apply(float3 p) => math.mul(Rotation, p) + translation;

		public override string ToString() => $"t=({translation.x:F2}, {translation.y:F2}, {translation.z:F2}) yaw={math.degrees(yaw):F1}°";
	}

	public struct RegistrationResult
	{
		public bool success;
		public Pose4 pose;
		/// <summary>Ranking score of the best fine candidate: agreement scaled down only when the overlap is thin.</summary>
		public float score;
		/// <summary>Fraction of the source's surface sample weight that landed in observed target cells.</summary>
		public float overlap;
		/// <summary>Surface agreement over the mutually observed samples, minus the free-space veto, 0..1.</summary>
		public float agreement;
		/// <summary>Roughly how many surface samples the agreement was measured on.</summary>
		public int overlapSamples;
		/// <summary>Score lead over the best candidate that is NOT a neighbor of the winner.</summary>
		public float margin;
		/// <summary>Residual distance to the target surface after refinement, meters; negative when refinement failed.</summary>
		public float refinedRms;
		public int surfaceSamples;
		public int candidatesScored;
		public string failure;
		/// <summary>Best score and its pose at each pyramid level, coarsest first, for diagnostics.</summary>
		public string levelTrace;

		public override string ToString() => success
			? $"{pose} agreement={agreement:F3} overlap={overlap:F2} ({overlapSamples} samples) score={score:F3} margin={margin:F3} rms={(refinedRms < 0 ? "refine failed: " + failure : refinedRms * 100 + "cm")} samples={surfaceSamples} scored={candidatesScored} levels[{levelTrace}]"
			: $"failed: {failure}";
	}

	/// <summary>
	/// Registers a chunk subset of the live scan (the source) against a
	/// <see cref="RegistrationVolume"/> (the target) on the GPU. Without a prior it is a
	/// global coarse-to-fine correlative search over yaw and translation bounded by the
	/// two maps' extents; with a prior it searches a window around it starting at the
	/// level whose cell covers the uncertainty. Gauss-Newton finishes either way.
	/// </summary>
	public sealed class TsdfRegistration : IDisposable
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct Sample
		{
			public float3 pos;
			public float sdf;
			public float weight;
			public float3 pad;
		}

		private const int MaxFineSamples = 1 << 17;
		private const int MaxCoarseSamples = 1 << 14;
		private const int MaxCandidatesPerDispatch = 65535;
		private const int MaxCandidates = 1 << 19;
		// coarse cells upper-bound their children loosely, so many false peaks outscore
		// the true one there; keeping plenty of survivors is what makes the pruning safe.
		// indexed by pyramid level, fine first
		private static readonly int[] SurvivorsPerLevel = { 64, 256, 512, 1024 };
		private const int RefineIterations = 8;
		private const float CoarseYawStepDegrees = 10f;

		/// <summary>
		/// Candidates closer than this are the same peak when measuring the margin. The
		/// agreement basin is wide (free-space samples decay slowly), so a rival has to sit
		/// well outside it to count as an alternative
		/// </summary>
		private const float DistinctTranslation = 1.5f;
		private const float DistinctYawDegrees = 20f;

		private readonly ComputeShader compute;
		private readonly EnvScanner scanner;

		private ComputeKernel extractKernel, scoreKernel, refineAccumulateKernel, refineSolveKernel;

		private readonly ComputeBuffer coarseSamples, fineSamples, sampleCountBuffer, chunkListBuffer;
		private ComputeBuffer candidateBuffer, scoreBuffer;
		private readonly ComputeBuffer refinePose, refinePartials;
		private string lastRefineNote;

		private int coarseCount, fineCount;
		private Bounds sourceBounds;
		private bool sourceReady;

		public RegistrationVolume Target { get; set; }
		public bool IsBusy { get; private set; }

		private static readonly int samplesAppendID = Shader.PropertyToID("samplesAppend");
		private static readonly int sampleStrideID = Shader.PropertyToID("sampleStride");
		private static readonly int freeStrideID = Shader.PropertyToID("freeStride");
		private static readonly int chunkListID = Shader.PropertyToID("chunkList");
		private static readonly int chunkCountID = Shader.PropertyToID("chunkCount");
		private static readonly int samplesID = Shader.PropertyToID("samples");
		private static readonly int sampleCountID = Shader.PropertyToID("sampleCount");
		private static readonly int candidatesID = Shader.PropertyToID("candidates");
		private static readonly int scoresID = Shader.PropertyToID("scores");
		private static readonly int candidateCountID = Shader.PropertyToID("candidateCount");
		private static readonly int candidateOffsetID = Shader.PropertyToID("candidateOffset");
		private static readonly int levelTexID = Shader.PropertyToID("levelTex");
		private static readonly int levelDimsID = Shader.PropertyToID("levelDims");
		private static readonly int levelCellID = Shader.PropertyToID("levelCell");
		private static readonly int levelIsFineID = Shader.PropertyToID("levelIsFine");
		private static readonly int refinePoseID = Shader.PropertyToID("refinePose");
		private static readonly int refinePartialsID = Shader.PropertyToID("refinePartials");
		private static readonly int refineGroupCountID = Shader.PropertyToID("refineGroupCount");

		public TsdfRegistration(ComputeShader compute, EnvScanner scanner)
		{
			this.compute = compute;
			this.scanner = scanner;

			extractKernel = new ComputeKernel(compute, "ExtractSamples");
			scoreKernel = new ComputeKernel(compute, "ScoreCandidates");
			refineAccumulateKernel = new ComputeKernel(compute, "RefineAccumulate");
			refineSolveKernel = new ComputeKernel(compute, "RefineSolve");

			int sampleStride = Marshal.SizeOf<Sample>();
			coarseSamples = new ComputeBuffer(MaxCoarseSamples, sampleStride, ComputeBufferType.Append);
			fineSamples = new ComputeBuffer(MaxFineSamples, sampleStride, ComputeBufferType.Append);
			sampleCountBuffer = new ComputeBuffer(2, sizeof(int), ComputeBufferType.Raw);
			chunkListBuffer = new ComputeBuffer(4096, sizeof(int));
			refinePose = new ComputeBuffer(2, 16);
			refinePartials = new ComputeBuffer(64 * 16, sizeof(float));
		}

		public static ComputeShader LoadShader() => Resources.Load<ComputeShader>("TsdfRegistration");

		// ---- source ------------------------------------------------------------

		/// <summary>
		/// Extracts the registration samples from these chunks of the live scan. The
		/// subset is the caller's choice: everything for colocation, a region for loop
		/// closure. Completes once the sample counts and source bounds are read back.
		/// </summary>
		public async Awaitable<bool> SetSourceChunksAsync(IReadOnlyList<int> chunkIndices)
		{
			sourceReady = false;
			if (chunkIndices.Count == 0 || chunkIndices.Count > chunkListBuffer.count) return false;

			scanner.EnsureGpuResources();
			scanner.ConfigureAtlasReader(compute);

			int[] list = new int[chunkIndices.Count];
			for (int i = 0; i < list.Length; i++) list[i] = chunkIndices[i];
			chunkListBuffer.SetData(list);

			int vpcd = scanner.VoxPerChunkDim;
			extractKernel.Bind("chunkTable", scanner.ChunkTable);
			extractKernel.Bind("chunkData", scanner.ChunkData);
			extractKernel.Bind(chunkListID, chunkListBuffer);
			compute.SetInt(chunkCountID, list.Length);

			// coarse: every 4th voxel per axis, free space every 8th; fine: every voxel, free every 4th
			coarseSamples.SetCounterValue(0);
			extractKernel.Bind(samplesAppendID, coarseSamples);
			compute.SetInt(sampleStrideID, 4);
			compute.SetInt(freeStrideID, 2);
			extractKernel.DispatchFit(vpcd, vpcd, vpcd * list.Length);
			ComputeBuffer.CopyCount(coarseSamples, sampleCountBuffer, 0);

			fineSamples.SetCounterValue(0);
			extractKernel.Bind(samplesAppendID, fineSamples);
			compute.SetInt(sampleStrideID, 1);
			compute.SetInt(freeStrideID, 4);
			extractKernel.DispatchFit(vpcd, vpcd, vpcd * list.Length);
			ComputeBuffer.CopyCount(fineSamples, sampleCountBuffer, sizeof(int));

			AsyncGPUReadbackRequest countReq = await AsyncGPUReadback.RequestAsync(sampleCountBuffer);
			if (countReq.hasError) return false;
			NativeArray<int> counts = countReq.GetData<int>();
			coarseCount = math.min(counts[0], MaxCoarseSamples);
			fineCount = math.min(counts[1], MaxFineSamples);
			if (coarseCount == 0 || fineCount == 0) return false;

			// the coarse set is small; its bounds bound the translation search
			AsyncGPUReadbackRequest coarseReq = await AsyncGPUReadback.RequestAsync(coarseSamples, coarseCount * Marshal.SizeOf<Sample>(), 0);
			if (coarseReq.hasError) return false;
			NativeArray<Sample> coarse = coarseReq.GetData<Sample>();
			float3 min = float.MaxValue, max = float.MinValue;
			for (int i = 0; i < coarseCount; i++)
			{
				min = math.min(min, coarse[i].pos);
				max = math.max(max, coarse[i].pos);
			}

			sourceBounds = new Bounds((Vector3)((min + max) * 0.5f), (Vector3)(max - min));
			sourceReady = true;
			return true;
		}

		public int FineSampleCount => fineCount;
		public int CoarseSampleCount => coarseCount;
		public Bounds SourceBounds => sourceBounds;

		// ---- search ------------------------------------------------------------

		/// <summary>
		/// Global search: every yaw, every translation that overlaps the target.
		/// </summary>
		public Awaitable<RegistrationResult> RegisterAsync() => RegisterAsync(null, 0, 0);

		/// <summary>
		/// Search around <paramref name="prior"/> within the given uncertainty, starting
		/// at the pyramid level whose cell covers it; null prior means global.
		/// </summary>
		public async Awaitable<RegistrationResult> RegisterAsync(Pose4? prior, float priorTranslationRange, float priorYawRangeDegrees)
		{
			if (IsBusy) return Fail("busy");
			if (!sourceReady) return Fail("no source samples");
			if (Target == null || !Target.IsAllocated) return Fail("no target");

			IsBusy = true;
			try
			{
				if (Target.PyramidDirty) Target.BuildPyramid();

				int topLevel = RegistrationVolume.PyramidLevels - 1;
				int startLevel = topLevel;
				List<float4> candidates;

				if (prior.HasValue)
				{
					float vox = scanner.VoxSize;
					startLevel = math.clamp((int)math.ceil(math.log2(math.max(priorTranslationRange, vox) / vox)), 0, topLevel);
					candidates = WindowCandidates(prior.Value, startLevel, priorTranslationRange, priorYawRangeDegrees);
				}
				else
				{
					candidates = GlobalCandidates(topLevel);
				}

				if (candidates.Count == 0) return Fail("no candidates: maps do not overlap");

				int scored = 0;
				List<(float4 pose, float score, float overlap)> ranked = null;
				System.Text.StringBuilder trace = new();

				for (int level = startLevel; level >= 0; level--)
				{
					// every level ranks with the sparse sample set; the fine level then
					// re-scores its survivors with every sample for the final ranking
					NativeArray<float> scores = await ScoreAsync(candidates, level, coarseSamples, coarseCount);
					if (!scores.IsCreated) return Fail("score readback failed");
					scored += candidates.Count;

					ranked = Rank(candidates, scores, SurvivorsPerLevel[level], coarseCount, MinOverlapSamplesCoarse);

					if (level == 0)
					{
						List<float4> finalists = new(ranked.Count);
						foreach ((float4 pose, float _, float __) in ranked) finalists.Add(pose);
						NativeArray<float> fineScores = await ScoreAsync(finalists, 0, fineSamples, fineCount);
						if (!fineScores.IsCreated) return Fail("fine score readback failed");
						scored += finalists.Count;
						ranked = Rank(finalists, fineScores, SurvivorsPerLevel[0], fineCount, MinOverlapSamplesFine);
					}

					float4 top = ranked[0].pose;
					trace.Append($"L{level}:{candidates.Count}c best={ranked[0].score:F3}/{ranked[0].overlap:F2}@({top.x:F1},{top.y:F1},{top.z:F1},{math.degrees(top.w):F0}°) ");
					if (level > 0) candidates = Expand(ranked, level - 1);
				}

				if (ranked == null || ranked.Count == 0) return Fail("nothing scored");

				(float4 best, float bestScore, float bestOverlap) = ranked[0];
				float margin = bestScore;
				foreach ((float4 pose, float score, float _) in ranked)
				{
					if (IsDistinct(pose, best)) { margin = bestScore - score; break; }
				}

				(Pose4 refined, float rms) = await RefineAsync(new Pose4(best.xyz, best.w));

				// the grid candidate sits up to half a cell off; the refined pose's own
				// agreement is what the caller should judge
				(float finalAgreement, float finalOverlap) = await EvaluateAsync(refined, 0);
				if (finalAgreement < 0) { finalAgreement = bestScore; finalOverlap = bestOverlap; }

				return new RegistrationResult
				{
					success = true,
					pose = refined,
					score = RankOf(finalAgreement, finalOverlap * fineCount, MinOverlapSamplesFine),
					overlap = finalOverlap,
					agreement = finalAgreement,
					overlapSamples = (int)(finalOverlap * fineCount),
					margin = margin,
					failure = lastRefineNote,
					refinedRms = rms,
					surfaceSamples = fineCount,
					candidatesScored = scored,
					levelTrace = trace.ToString()
				};
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return Fail(e.Message);
			}
			finally
			{
				IsBusy = false;
			}
		}

		private static RegistrationResult Fail(string why) => new() { success = false, failure = why };

		// a pose supported by fewer overlapping samples than this is discounted for
		// ranking: a sliver of perfectly matching floor must not outrank a broadly
		// matching room. An absolute count, never a fraction of the source: scanning
		// more than the peer must not lower the score of the part they share
		private const int MinOverlapSamplesCoarse = 100;
		private const int MinOverlapSamplesFine = 500;

		/// <summary>Ranking value: agreement over the overlap, discounted only when few samples overlap.</summary>
		public static float RankOf(float agreement, float overlapSamples, int minSamples) =>
			agreement * math.saturate(overlapSamples / minSamples);

		/// <summary>Scores one pose at the given level (0 = fine): (agreement over the overlap, overlap fraction).</summary>
		public async Awaitable<(float agreement, float overlap)> EvaluateAsync(Pose4 pose, int level = 0)
		{
			if (!sourceReady || Target == null || !Target.IsAllocated) return (-1, 0);
			if (Target.PyramidDirty) Target.BuildPyramid();
			List<float4> one = new() { new float4(pose.translation, pose.yaw) };
			NativeArray<float> scores = await ScoreAsync(one, level, level == 0 ? fineSamples : coarseSamples, level == 0 ? fineCount : coarseCount);
			return scores.IsCreated ? (scores[0], scores[1]) : (-1, 0);
		}

		private float YawStepDegrees(int level) => CoarseYawStepDegrees * (1 << level) / (1 << (RegistrationVolume.PyramidLevels - 1));
		private float CellSize(int level) => scanner.VoxSize * (1 << level);

		private List<float4> GlobalCandidates(int level)
		{
			float cell = CellSize(level);
			float yawStep = math.radians(YawStepDegrees(level));
			int yawBins = (int)math.round(2 * math.PI / yawStep);

			Bounds target = Target.WorldBounds;
			float3 tMin = target.min, tMax = target.max;
			float3 sMin = sourceBounds.min, sMax = sourceBounds.max;

			// require some overlap: the source must land at least this far into the target
			float3 sourceSize = sMax - sMin;
			float3 overlap = math.min(1f, sourceSize * 0.25f);

			List<float4> result = new();
			float3[] corners = new float3[8];

			for (int step = 1; step <= 8; step++)
			{
				result.Clear();
				float stride = cell * step;

				for (int yi = 0; yi < yawBins; yi++)
				{
					float yaw = yi * yawStep;
					quaternion rot = quaternion.RotateY(yaw);

					// bounds of the rotated source
					int ci = 0;
					for (int x = 0; x < 2; x++)
					for (int y = 0; y < 2; y++)
					for (int z = 0; z < 2; z++)
						corners[ci++] = math.mul(rot, new float3(x == 0 ? sMin.x : sMax.x, y == 0 ? sMin.y : sMax.y, z == 0 ? sMin.z : sMax.z));
					float3 rMin = float.MaxValue, rMax = float.MinValue;
					foreach (float3 c in corners) { rMin = math.min(rMin, c); rMax = math.max(rMax, c); }

					float3 lo = tMin - rMax + overlap;
					float3 hi = tMax - rMin - overlap;
					if (math.any(hi < lo)) continue;

					for (float tx = lo.x; tx <= hi.x; tx += stride)
					for (float ty = lo.y; ty <= hi.y; ty += stride)
					for (float tz = lo.z; tz <= hi.z; tz += stride)
						result.Add(new float4(tx, ty, tz, yaw));
				}

				if (result.Count <= MaxCandidates) break;
			}

			return result;
		}

		private List<float4> WindowCandidates(Pose4 prior, int level, float translationRange, float yawRangeDegrees)
		{
			float cell = CellSize(level);
			float yawStep = math.radians(YawStepDegrees(level));
			int tCells = math.max(1, (int)math.ceil(translationRange / cell));
			int yawCells = math.max(1, (int)math.ceil(math.radians(yawRangeDegrees) / yawStep));

			List<float4> result = new();
			for (int yi = -yawCells; yi <= yawCells; yi++)
			for (int x = -tCells; x <= tCells; x++)
			for (int y = -tCells; y <= tCells; y++)
			for (int z = -tCells; z <= tCells; z++)
				result.Add(new float4(prior.translation + new float3(x, y, z) * cell, prior.yaw + yi * yawStep));
			return result;
		}

		private List<float4> Expand(List<(float4 pose, float score, float overlap)> survivors, int level)
		{
			float cell = CellSize(level);
			float yawStep = math.radians(YawStepDegrees(level));
			HashSet<int4> seen = new();
			List<float4> result = new();

			foreach ((float4 pose, float _, float __) in survivors)
			{
				for (int yi = -1; yi <= 1; yi++)
				for (int x = -1; x <= 1; x++)
				for (int y = -1; y <= 1; y++)
				for (int z = -1; z <= 1; z++)
				{
					float4 c = new(pose.xyz + new float3(x, y, z) * cell, pose.w + yi * yawStep);
					int4 key = new((int3)math.round(c.xyz / cell), (int)math.round(c.w / yawStep));
					if (seen.Add(key)) result.Add(c);
				}
			}

			return result;
		}

		private static List<(float4 pose, float score, float overlap)> Rank(List<float4> candidates, NativeArray<float> scores, int keep, int sampleCount, int minOverlapSamples)
		{
			List<(float4 pose, float score, float overlap)> all = new(candidates.Count);
			for (int i = 0; i < candidates.Count; i++)
				all.Add((candidates[i], RankOf(scores[i * 2], scores[i * 2 + 1] * sampleCount, minOverlapSamples), scores[i * 2 + 1]));
			all.Sort((a, b) => b.score.CompareTo(a.score));
			if (all.Count > keep) all.RemoveRange(keep, all.Count - keep);
			return all;
		}

		private static bool IsDistinct(float4 a, float4 b)
		{
			float yawDiff = math.abs(math.degrees(math.atan2(math.sin(a.w - b.w), math.cos(a.w - b.w))));
			return math.distance(a.xyz, b.xyz) > DistinctTranslation || yawDiff > DistinctYawDegrees;
		}

		private async Awaitable<NativeArray<float>> ScoreAsync(List<float4> candidates, int level, ComputeBuffer sampleBuffer, int sampleCount)
		{
			EnsureCandidateBuffers(candidates.Count);
			candidateBuffer.SetData(candidates);

			(Texture tex, int3 dims, float cell, bool isFine) = Target.Level(level);
			scanner.ConfigureAtlasReader(compute);
			Target.BindForReading(scoreKernel);
			scoreKernel.Bind(samplesID, sampleBuffer);
			compute.SetInt(sampleCountID, sampleCount);
			scoreKernel.Bind(candidatesID, candidateBuffer);
			scoreKernel.Bind(scoresID, scoreBuffer);
			compute.SetInt(candidateCountID, candidates.Count);
			scoreKernel.Bind(levelTexID, tex);
			compute.SetInts(levelDimsID, dims.x, dims.y, dims.z);
			compute.SetFloat(levelCellID, cell);
			compute.SetInt(levelIsFineID, isFine ? 1 : 0);

			for (int offset = 0; offset < candidates.Count; offset += MaxCandidatesPerDispatch)
			{
				compute.SetInt(candidateOffsetID, offset);
				scoreKernel.DispatchGroups(math.min(MaxCandidatesPerDispatch, candidates.Count - offset), 1, 1);
			}

			AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(scoreBuffer, candidates.Count * 2 * sizeof(float), 0);
			return req.hasError ? default : req.GetData<float>();
		}

		private void EnsureCandidateBuffers(int count)
		{
			if (candidateBuffer != null && candidateBuffer.count >= count) return;
			candidateBuffer?.Release();
			scoreBuffer?.Release();
			int size = math.max(count, 1024);
			candidateBuffer = new ComputeBuffer(size, 16);
			scoreBuffer = new ComputeBuffer(size * 2, sizeof(float));
		}

		private async Awaitable<(Pose4 pose, float rms)> RefineAsync(Pose4 start)
		{
			int groups = math.clamp((fineCount + 255) / 256, 1, 64);

			refinePose.SetData(new[] { new float4(start.translation, start.yaw), new float4(0, 0, 0, 0.01f) });

			scanner.ConfigureAtlasReader(compute);
			Target.BindForReading(refineAccumulateKernel);
			refineAccumulateKernel.Bind(samplesID, fineSamples);
			compute.SetInt(sampleCountID, fineCount);
			refineAccumulateKernel.Bind(refinePoseID, refinePose);
			refineAccumulateKernel.Bind(refinePartialsID, refinePartials);
			refineSolveKernel.Bind(refinePoseID, refinePose);
			refineSolveKernel.Bind(refinePartialsID, refinePartials);
			compute.SetInt(refineGroupCountID, groups);

			for (int i = 0; i < RefineIterations; i++)
			{
				refineAccumulateKernel.DispatchGroups(groups, 1, 1);
				refineSolveKernel.DispatchGroups(1, 1, 1);
			}

			AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(refinePose);
			if (req.hasError) { lastRefineNote = "readback failed"; return (start, -1); }
			NativeArray<float4> data = req.GetData<float4>();
			float4 pose = data[0];
			float rms = data[1].x;
			int support = (int)data[1].y;
			if (rms < 0 || math.any(!math.isfinite(pose)))
			{
				lastRefineNote = rms == -1 ? $"only {support} samples in band" : rms == -2 ? "singular system" : "non-finite pose";
				return (start, -1);
			}

			lastRefineNote = null;
			return (new Pose4(pose.xyz, pose.w), rms);
		}

		public void Dispose()
		{
			coarseSamples?.Release();
			fineSamples?.Release();
			sampleCountBuffer?.Release();
			chunkListBuffer?.Release();
			candidateBuffer?.Release();
			scoreBuffer?.Release();
			refinePose?.Release();
			refinePartials?.Release();
		}
	}
}
