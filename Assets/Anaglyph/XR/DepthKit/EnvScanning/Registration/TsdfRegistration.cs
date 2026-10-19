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
	/// <summary>One step of a search: the best pose after a pyramid level, or after refinement.</summary>
	public readonly struct SearchStep
	{
		public SearchStep(int level, Pose4 pose, float score, bool refined)
		{
			this.level = level;
			this.pose = pose;
			this.score = score;
			this.refined = refined;
		}

		/// <summary>Pyramid level just scored, coarsest highest; -1 once refined.</summary>
		public readonly int level;
		public readonly Pose4 pose;
		public readonly float score;
		public readonly bool refined;
	}

	/// <summary>
	/// How firmly a source scan's own geometry pins each of the four degrees of freedom:
	/// the eigen decomposition of the information matrix sum(J J^T) over its surface
	/// samples, J = (normal, yaw lever / StatsLever). An eigenvalue counts roughly how many
	/// samples' worth of surface face along that direction, so a hallway is near zero
	/// along its length and a lone floor near zero in x and z. An absolute count: scanning
	/// more never lowers it.
	/// </summary>
	public readonly struct SourceConstraint
	{
		public SourceConstraint(float4 strengths, float4x4 directions)
		{
			this.strengths = strengths;
			this.directions = directions;
		}

		/// <summary>Eigenvalues, weakest first.</summary>
		public readonly float4 strengths;
		/// <summary>Matching unit eigenvectors as columns, (x, y, z, yaw) each.</summary>
		public readonly float4x4 directions;

		public float Weakest => strengths.x;
		public float4 WeakestDirection => directions.c0;

		public override string ToString()
		{
			float4 d = math.abs(WeakestDirection);
			string axis = d.w > math.cmax(d.xyz) ? "yaw" : d.x >= d.y && d.x >= d.z ? "x" : d.y >= d.z ? "y" : "z";
			return $"weakest {strengths.x:F0} along {axis} (strengths {strengths.x:F0}/{strengths.y:F0}/{strengths.z:F0}/{strengths.w:F0})";
		}
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
		/// <summary>
		/// Share of the mutually observed surface weight that landed where the target saw open
		/// air, 0..1: surfaces the pose puts where the target saw none. Indifferent to how
		/// closely the surfaces that are there line up, so it holds steady across sensor noise
		/// and map warp that drag <see cref="agreement"/> down, while a wrong pose drives it up
		/// </summary>
		public float contradiction;
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
		/// <summary>Wall-normal yaw hypotheses a global search was restricted to; 0 when it searched every yaw (or was windowed).</summary>
		public int yawHypotheses;

		public override string ToString() => success
			? $"{pose} agreement={agreement:F3} contradiction={contradiction:F3} overlap={overlap:F2} ({overlapSamples} samples) score={score:F3} margin={margin:F3} rms={(refinedRms < 0 ? "refine failed: " + failure : refinedRms * 100 + "cm")} samples={surfaceSamples} scored={candidatesScored}{(yawHypotheses > 0 ? $" yaws={yawHypotheses}" : "")} levels[{levelTrace}]"
			: $"failed: {failure}";
	}

	/// <summary>
	/// Registers a chunk subset of the live scan (the source) against a
	/// <see cref="RegistrationVolume"/> (the target) on the GPU. Without a prior it is a
	/// global coarse-to-fine correlative search over yaw and translation bounded by the
	/// two maps' extents, at the yaws their wall directions allow; with a prior it
	/// searches a window around it starting at the level whose cell covers the
	/// uncertainty. Gauss-Newton finishes either way. Scoring, the per-level top-K and
	/// the refinement run on the GPU; ordering and expanding the survivors, which is
	/// small and branchy, runs on the CPU from a readback of just those survivors.
	/// </summary>
	public sealed class TsdfRegistration : IDisposable
	{
		/// <summary>One registration sample, as the GPU extracts them.</summary>
		[StructLayout(LayoutKind.Sequential)]
		public struct Sample
		{
			/// <summary>World position in the source frame.</summary>
			public float3 pos;
			/// <summary>Normalized signed distance: |sdf| ≤ 0.5 is a surface sample, > 0.5 free space.</summary>
			public float sdf;
			/// <summary>How much the sample should say about the pose.</summary>
			public float weight;
			public float3 pad;

			public Sample(float3 pos, float sdf, float weight)
			{
				this.pos = pos;
				this.sdf = sdf;
				this.weight = weight;
				pad = 0;
			}
		}

		private const int MaxFineSamples = 1 << 17;
		private const int MaxCoarseSamples = 1 << 14;
		private const int MaxCandidates = 1 << 19;
		// coarse cells upper-bound their children loosely, so many false peaks outscore
		// the true one there; keeping plenty of survivors is what makes the pruning safe.
		// indexed by pyramid level, fine first
		private static readonly int[] SurvivorsPerLevel = { 64, 256, 512, 1024 };
		private const int MaxSurvivors = 1024;
		// each level's GPU selection is this many times its survivor count; the CPU then keeps
		// the best, at most a PeakQuota share of them around any one peak, so the winner's
		// neighbors can't crowd rival peaks (another corner of the room) out of the beam
		private const int SelectOversample = 4;
		private const int MaxSelected = MaxSurvivors * SelectOversample;
		private const int PeakQuotaDivisor = 4;
		// distinct peaks refined and fine-scored side by side at the end; the margin is the
		// winner's lead over the best of the others
		private const int Peaks = 4;
		private const int RefineIterations = 8;
		private const float CoarseYawStepDegrees = 10f;

		/// <summary>
		/// Candidates closer than this are the same peak when measuring the margin. The
		/// agreement basin is wide (free-space samples decay slowly), so a rival has to sit
		/// well outside it to count as an alternative
		/// </summary>
		private const float DistinctTranslation = 1.5f;
		private const float DistinctYawDegrees = 20f;

		// wall-normal yaw hypotheses: peaks of the circular cross-correlation of the two
		// maps' wall azimuth histograms. A Manhattan room gives four, 90° apart
		private const int MinWallSamples = 200;
		private const int MaxYawHypotheses = 6;
		private const float YawPeakFraction = 0.5f;
		private const int YawPeakSeparationBins = 10;
		private const int YawBins = RegistrationVolume.YawBins;

		// mirrored in TsdfRegistration.compute
		private const int ScoreRow = 4096;
		private const int SelectBins = 4096;
		private const int SelectRank = 2, SelectExplained = 3;
		private const int StatsMin = 2, StatsMax = 5, StatsYaw = 8, StatsInfo = StatsYaw + YawBins, StatsLength = StatsInfo + 10;
		private const float InfoScale = 256f;
		/// <summary>Meters of lever arm that count as one unit of yaw constraint in <see cref="SourceConstraint"/>.</summary>
		public const float StatsLever = 2f;
		private const int CoordBias = 1 << 20;

		private readonly ComputeShader compute;
		private readonly EnvScanner scanner;

		private ComputeKernel extractKernel, scoreKernel, refineAccumulateKernel, refineSolveKernel;
		private ComputeKernel beginSelectKernel, selectHistogramKernel, selectThresholdKernel, selectCompactKernel;

		private readonly ComputeBuffer coarseSamples, fineSamples, sourceStats, chunkListBuffer;
		private readonly ComputeBuffer refinePartials, evalScores;
		private readonly ComputeBuffer[] peakPoses = new ComputeBuffer[Peaks], peakScores = new ComputeBuffer[Peaks];
		private readonly ComputeBuffer selectHistogram, selectState, survivors, survivorScores;
		private ComputeBuffer candidateBuffer, scoreBuffer;
		private string lastRefineNote;

		private int coarseCount, fineCount;
		private Bounds sourceBounds;
		private readonly float[] sourceYawHistogram = new float[YawBins];
		private int sourceWallSamples;
		private SourceConstraint sourceConstraint;
		private bool sourceReady;

		public RegistrationVolume Target { get; set; }
		public bool IsBusy { get; private set; }

		/// <summary>Raised when a search begins, before its first level is scored.</summary>
		public event Action SearchStarted = delegate { };

		/// <summary>Raised with the best pose after each pyramid level and after refinement.</summary>
		public event Action<SearchStep> StepCompleted = delegate { };

		private static readonly int samplesAppendID = Shader.PropertyToID("samplesAppend");
		private static readonly int sampleStrideID = Shader.PropertyToID("sampleStride");
		private static readonly int freeStrideID = Shader.PropertyToID("freeStride");
		private static readonly int sourceStatsID = Shader.PropertyToID("sourceStats");
		private static readonly int accumulateStatsID = Shader.PropertyToID("accumulateStats");
		private static readonly int statsCenterID = Shader.PropertyToID("statsCenter");
		private static readonly int statsLeverID = Shader.PropertyToID("statsLever");
		private static readonly int chunkListID = Shader.PropertyToID("chunkList");
		private static readonly int chunkCountID = Shader.PropertyToID("chunkCount");
		private static readonly int samplesID = Shader.PropertyToID("samples");
		private static readonly int sampleCountID = Shader.PropertyToID("sampleCount");
		private static readonly int candidatesID = Shader.PropertyToID("candidates");
		private static readonly int candidateCountID = Shader.PropertyToID("candidateCount");
		private static readonly int scoresID = Shader.PropertyToID("scores");
		private static readonly int minOverlapSamplesID = Shader.PropertyToID("minOverlapSamples");
		private static readonly int levelTexID = Shader.PropertyToID("levelTex");
		private static readonly int levelDimsID = Shader.PropertyToID("levelDims");
		private static readonly int levelCellID = Shader.PropertyToID("levelCell");
		private static readonly int levelIsFineID = Shader.PropertyToID("levelIsFine");
		private static readonly int refinePoseID = Shader.PropertyToID("refinePose");
		private static readonly int refinePartialsID = Shader.PropertyToID("refinePartials");
		private static readonly int refineGroupCountID = Shader.PropertyToID("refineGroupCount");
		private static readonly int selectHistogramID = Shader.PropertyToID("selectHistogram");
		private static readonly int selectStateID = Shader.PropertyToID("selectState");
		private static readonly int survivorsID = Shader.PropertyToID("survivors");
		private static readonly int survivorScoresID = Shader.PropertyToID("survivorScores");
		private static readonly int survivorLimitID = Shader.PropertyToID("survivorLimit");
		private static readonly int selectKeyID = Shader.PropertyToID("selectKey");
		private static readonly int scoreTrilinearID = Shader.PropertyToID("scoreTrilinear");

		public TsdfRegistration(ComputeShader compute, EnvScanner scanner)
		{
			this.compute = compute;
			this.scanner = scanner;

			extractKernel = new ComputeKernel(compute, "ExtractSamples");
			scoreKernel = new ComputeKernel(compute, "ScoreCandidates");
			refineAccumulateKernel = new ComputeKernel(compute, "RefineAccumulate");
			refineSolveKernel = new ComputeKernel(compute, "RefineSolve");
			beginSelectKernel = new ComputeKernel(compute, "BeginSelect");
			selectHistogramKernel = new ComputeKernel(compute, "SelectHistogram");
			selectThresholdKernel = new ComputeKernel(compute, "SelectThreshold");
			selectCompactKernel = new ComputeKernel(compute, "SelectCompact");

			int sampleStride = Marshal.SizeOf<Sample>();
			coarseSamples = new ComputeBuffer(MaxCoarseSamples, sampleStride, ComputeBufferType.Append);
			fineSamples = new ComputeBuffer(MaxFineSamples, sampleStride, ComputeBufferType.Append);
			sourceStats = new ComputeBuffer(StatsLength, sizeof(uint), ComputeBufferType.Raw);
			chunkListBuffer = new ComputeBuffer(4096, sizeof(int));
			for (int i = 0; i < Peaks; i++)
			{
				peakPoses[i] = new ComputeBuffer(2, 16);
				peakScores[i] = new ComputeBuffer(1, 16);
			}

			refinePartials = new ComputeBuffer(64 * 16, sizeof(float));
			evalScores = new ComputeBuffer(1, 16);

			selectHistogram = new ComputeBuffer(SelectBins, sizeof(uint));
			selectState = new ComputeBuffer(4, sizeof(uint));
			survivors = new ComputeBuffer(MaxSelected, 16);
			survivorScores = new ComputeBuffer(MaxSelected, 16);
		}

		public static ComputeShader LoadShader() => Resources.Load<ComputeShader>("TsdfRegistration");

		// ---- source ------------------------------------------------------------

		/// <summary>
		/// Extracts the registration samples from these chunks of a scan volume: the live
		/// map unless <paramref name="source"/> is given (a relocalizing headset's scratch
		/// scan). The subset is the caller's choice: everything for colocation, a region
		/// for loop closure. Completes once the sample counts, bounds and wall histogram
		/// are read back, all in one request.
		/// </summary>
		public async Awaitable<bool> SetSourceChunksAsync(IReadOnlyList<int> chunkIndices, EnvVolume source = null)
		{
			sourceReady = false;
			if (chunkIndices.Count == 0 || chunkIndices.Count > chunkListBuffer.count) return false;

			scanner.EnsureGpuResources();
			source ??= scanner.LiveVolume;
			if (source == null || !source.IsCreated) return false;
			scanner.ConfigureAtlasReader(compute, source);

			// yaw is measured about the middle of the chunks, known before extraction
			int[] list = new int[chunkIndices.Count];
			float3 center = 0;
			for (int i = 0; i < list.Length; i++)
			{
				list[i] = chunkIndices[i];
				center += scanner.ChunkCoordToCornerWorldPos(scanner.ChunkIndexToChunkCoord(list[i])) + scanner.ChunkWorldSizeDim * 0.5f;
			}

			center /= list.Length;
			chunkListBuffer.SetData(list);
			compute.SetVector(statsCenterID, new Vector4(center.x, center.y, center.z, 0));
			compute.SetFloat(statsLeverID, StatsLever);

			uint[] stats = new uint[StatsLength];
			for (int a = 0; a < 3; a++) stats[StatsMin + a] = uint.MaxValue;
			sourceStats.SetData(stats);

			int vpcd = scanner.VoxPerChunkDim;
			extractKernel.Bind("chunkTable", source.ChunkTable);
			extractKernel.Bind("chunkData", source.ChunkData);
			extractKernel.Bind(chunkListID, chunkListBuffer);
			extractKernel.Bind(sourceStatsID, sourceStats);
			compute.SetInt(chunkCountID, list.Length);

			// coarse: every 4th voxel per axis, free space every 8th; fine: every voxel,
			// free every 4th. The fine pass also gathers the bounds, wall histogram and
			// information matrix
			coarseSamples.SetCounterValue(0);
			extractKernel.Bind(samplesAppendID, coarseSamples);
			compute.SetInt(sampleStrideID, 4);
			compute.SetInt(freeStrideID, 2);
			compute.SetInt(accumulateStatsID, 0);
			extractKernel.DispatchFit(vpcd, vpcd, vpcd * list.Length);
			ComputeBuffer.CopyCount(coarseSamples, sourceStats, 0);

			fineSamples.SetCounterValue(0);
			extractKernel.Bind(samplesAppendID, fineSamples);
			compute.SetInt(sampleStrideID, 1);
			compute.SetInt(freeStrideID, 4);
			compute.SetInt(accumulateStatsID, 1);
			extractKernel.DispatchFit(vpcd, vpcd, vpcd * list.Length);
			ComputeBuffer.CopyCount(fineSamples, sourceStats, sizeof(uint));

			AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(sourceStats);
			if (req.hasError) return false;
			NativeArray<uint> data = req.GetData<uint>();
			coarseCount = math.min((int)data[0], MaxCoarseSamples);
			fineCount = math.min((int)data[1], MaxFineSamples);
			if (coarseCount == 0 || fineCount == 0) return false;

			// the bounds bound the translation search
			int3 qMin = new int3((int)data[StatsMin], (int)data[StatsMin + 1], (int)data[StatsMin + 2]) - CoordBias;
			int3 qMax = new int3((int)data[StatsMax], (int)data[StatsMax + 1], (int)data[StatsMax + 2]) - CoordBias;
			float3 min = (float3)qMin * scanner.VoxSize, max = (float3)(qMax + 1) * scanner.VoxSize;
			sourceBounds = new Bounds((Vector3)((min + max) * 0.5f), (Vector3)(max - min));

			sourceWallSamples = 0;
			for (int i = 0; i < YawBins; i++)
			{
				sourceYawHistogram[i] = data[StatsYaw + i];
				sourceWallSamples += (int)data[StatsYaw + i];
			}

			float[] info = new float[10];
			for (int k = 0; k < 10; k++) info[k] = (int)data[StatsInfo + k] / InfoScale;
			sourceConstraint = Decompose(info);

			sourceReady = true;
			return true;
		}

		public int FineSampleCount => fineCount;
		public int CoarseSampleCount => coarseCount;
		public Bounds SourceBounds => sourceBounds;

		/// <summary>How firmly the current source's geometry pins each degree of freedom; read it before registering.</summary>
		public SourceConstraint Constraint => sourceConstraint;

		// symmetric 4x4 from its upper triangle (00 01 02 03 11 12 13 22 23 33), cyclic Jacobi
		private static SourceConstraint Decompose(float[] t)
		{
			double[,] a =
			{
				{ t[0], t[1], t[2], t[3] },
				{ t[1], t[4], t[5], t[6] },
				{ t[2], t[5], t[7], t[8] },
				{ t[3], t[6], t[8], t[9] }
			};
			double[,] v = { { 1, 0, 0, 0 }, { 0, 1, 0, 0 }, { 0, 0, 1, 0 }, { 0, 0, 0, 1 } };

			for (int sweep = 0; sweep < 32; sweep++)
			{
				double off = 0;
				for (int p = 0; p < 4; p++)
				for (int q = p + 1; q < 4; q++)
					off += a[p, q] * a[p, q];
				if (off < 1e-18) break;

				for (int p = 0; p < 4; p++)
				for (int q = p + 1; q < 4; q++)
				{
					if (Math.Abs(a[p, q]) < 1e-30) continue;
					double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
					double tan = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
					if (theta == 0) tan = 1;
					double c = 1 / Math.Sqrt(tan * tan + 1), s = tan * c;
					for (int k = 0; k < 4; k++)
					{
						double akp = a[k, p], akq = a[k, q];
						a[k, p] = c * akp - s * akq;
						a[k, q] = s * akp + c * akq;
					}

					for (int k = 0; k < 4; k++)
					{
						double apk = a[p, k], aqk = a[q, k];
						a[p, k] = c * apk - s * aqk;
						a[q, k] = s * apk + c * aqk;
					}

					for (int k = 0; k < 4; k++)
					{
						double vkp = v[k, p], vkq = v[k, q];
						v[k, p] = c * vkp - s * vkq;
						v[k, q] = s * vkp + c * vkq;
					}
				}
			}

			int[] order = { 0, 1, 2, 3 };
			Array.Sort(order, (i, j) => a[i, i].CompareTo(a[j, j]));
			float4 strengths = 0;
			float4x4 directions = default;
			for (int i = 0; i < 4; i++)
			{
				int o = order[i];
				strengths[i] = (float)Math.Max(0, a[o, o]);
				directions[i] = new float4((float)v[0, o], (float)v[1, o], (float)v[2, o], (float)v[3, o]);
			}

			return new SourceConstraint(strengths, directions);
		}

		// ---- search ------------------------------------------------------------

		/// <summary>
		/// Global search: every translation that overlaps the target, at the yaws the
		/// two maps' wall directions allow, or at every yaw when <paramref name="yawHypotheses"/>
		/// is false or the walls are too few to say.
		/// </summary>
		public Awaitable<RegistrationResult> RegisterAsync(bool yawHypotheses = true) => RegisterAsync(null, 0, 0, null, yawHypotheses);

		/// <summary>
		/// Search around <paramref name="prior"/> within the given uncertainty, starting
		/// at the pyramid level whose cell covers it; null prior means global.
		/// <paramref name="pivot"/> is the source-frame point the yaw uncertainty turns
		/// about (the source's center if not given): a recentered headset has turned about
		/// itself, and a yaw about the world origin would put the translation window
		/// kilometers off for a rig far from it.
		/// </summary>
		public async Awaitable<RegistrationResult> RegisterAsync(Pose4? prior, float priorTranslationRange, float priorYawRangeDegrees, float3? pivot = null, bool yawHypotheses = true)
		{
			if (IsBusy) return Fail("busy");
			if (!sourceReady) return Fail("no source samples");
			if (Target == null || !Target.IsAllocated) return Fail("no target");

			IsBusy = true;
			try
			{
				SearchStarted.Invoke();
				if (Target.PyramidDirty) Target.BuildPyramid();

				int topLevel = RegistrationVolume.PyramidLevels - 1;
				int startLevel = topLevel;
				float3 center = pivot ?? (float3)sourceBounds.center;
				List<float4> candidates;
				int hypotheses = 0;

				if (prior.HasValue)
				{
					float vox = scanner.VoxSize;
					startLevel = math.clamp((int)math.ceil(math.log2(math.max(priorTranslationRange, vox) / vox)), 0, topLevel);
					candidates = WindowCandidates(prior.Value, startLevel, priorTranslationRange, priorYawRangeDegrees, center);
				}
				else
				{
					List<int> yawBins = null;
					if (yawHypotheses)
					{
						float[] targetHistogram = await Target.ReadYawHistogramAsync();
						yawBins = YawHypothesisBins(targetHistogram, out hypotheses);
					}

					candidates = GlobalCandidates(topLevel, center, yawBins);
				}

				if (candidates.Count == 0) return Fail("no candidates: maps do not overlap");

				RegistrationResult result = await SearchAsync(candidates, startLevel, center);
				result.yawHypotheses = hypotheses;
				return result;
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
		// |sdf| within this (normalized) is a surface sample; mirrored in TsdfRegistration.compute
		private const float SurfaceBand = 0.5f;

		/// <summary>
		/// Surface samples that this much scanned surface area (m²) yields. The surface band
		/// is a truncation band thick (±SurfaceBand of it), so a square meter holds
		/// band / voxel³ of them. Sample-count thresholds expressed as areas keep their
		/// meaning when the scanner's voxel size or truncation band changes.
		/// </summary>
		public static float SurfaceSamples(EnvScanner scanner, float area)
		{
			float vox = scanner.VoxSize;
			return area * 2f * SurfaceBand * scanner.DistanceTruncationBand / (vox * vox * vox);
		}

		/// <summary><see cref="SurfaceSamples"/> for a multiple of one chunk face (chunk world size²).</summary>
		public static float SurfaceSamplesPerChunkFaces(EnvScanner scanner, float chunkFaces)
		{
			float side = scanner.ChunkWorldSizeDim;
			return SurfaceSamples(scanner, chunkFaces * side * side);
		}

		// mirrors selectValue in TsdfRegistration.compute: explained (agreement x overlap) or a score component
		private static float SelectValue(float4 score, int key) => key == SelectExplained ? score.x * score.y : score[key];

		public static float RankOf(float agreement, float overlapSamples, int minSamples) =>
			agreement * math.saturate(overlapSamples / minSamples);

		/// <summary>
		/// Coarse to fine. Per level the GPU scores every candidate and keeps the top K;
		/// the CPU reads back only those survivors, orders them and expands them into the
		/// next level's candidates, where a hash set merges the children neighbors share.
		/// </summary>
		private async Awaitable<RegistrationResult> SearchAsync(List<float4> candidates, int startLevel, float3 center)
		{
			int scored = 0;
			System.Text.StringBuilder trace = new();
			List<(float4 pose, float4 score)> ranked = null;

			for (int level = startLevel; level >= 0; level--)
			{
				int n = candidates.Count;
				EnsureCandidateBuffers(n);
				candidateBuffer.SetData(candidates);
				scanner.ConfigureAtlasReader(compute);

				// every level scores with the sparse sample set; coarse levels keep what
				// explains the most of the source, the fine level what agrees best
				DispatchScore(candidateBuffer, scoreBuffer, n, level, coarseSamples, coarseCount, MinOverlapSamplesCoarse);
				int key = level > 0 ? SelectExplained : SelectRank;
				int keep = math.min(SurvivorsPerLevel[level], n);
				int k = math.min(keep * SelectOversample, n);
				DispatchSelect(candidateBuffer, scoreBuffer, n, k, key);
				scored += n;

				// the fine level re-scores its survivors with every sample for the final ranking
				if (level == 0)
				{
					DispatchScore(survivors, survivorScores, k, 0, fineSamples, fineCount, MinOverlapSamplesFine);
					scored += k;
				}

				Awaitable<AsyncGPUReadbackRequest> posesPending = AsyncGPUReadback.RequestAsync(survivors, k * 16, 0);
				Awaitable<AsyncGPUReadbackRequest> scoresPending = AsyncGPUReadback.RequestAsync(survivorScores, k * 16, 0);
				AsyncGPUReadbackRequest poseReq = await posesPending;
				AsyncGPUReadbackRequest scoreReq = await scoresPending;
				if (poseReq.hasError || scoreReq.hasError) return Fail("survivor readback failed");

				NativeArray<float4> poses = poseReq.GetData<float4>(), scores = scoreReq.GetData<float4>();
				ranked = new List<(float4, float4)>(k);
				for (int i = 0; i < k; i++) ranked.Add((poses[i], scores[i]));
				ranked.Sort((a, b) => SelectValue(b.score, key).CompareTo(SelectValue(a.score, key)));
				ranked = KeepDiverse(ranked, keep, math.max(1, keep / PeakQuotaDivisor));

				(float4 top, float4 topScore) = ranked[0];
				trace.Append($"L{level}:{n}c best={topScore.z:F3}/{topScore.y:F2}@({top.x:F1},{top.y:F1},{top.z:F1},{math.degrees(top.w):F0}°) ");
				StepCompleted.Invoke(new SearchStep(level, new Pose4(top.xyz, top.w), topScore.z, false));

				if (level > 0) candidates = Expand(ranked, level - 1, center);
			}

			if (ranked == null || ranked.Count == 0) return Fail("nothing scored");

			// the best of each distinct peak, refined and fine-scored side by side: a rival
			// peak (another corner of the room) is always measured against the winner on equal
			// terms, refined against refined, and read trilinearly as the refinement reads, so
			// neither gains or loses from where its samples happen to fall within a voxel
			List<(float4 pose, float4 score)> heads = PeakHeads(ranked, Peaks);
			for (int i = 0; i < heads.Count; i++)
			{
				DispatchRefine(new Pose4(heads[i].pose.xyz, heads[i].pose.w), peakPoses[i]);
				DispatchScore(peakPoses[i], peakScores[i], 1, 0, fineSamples, fineCount, MinOverlapSamplesFine, trilinear: true);
			}

			var posePending = new Awaitable<AsyncGPUReadbackRequest>[heads.Count];
			var scorePending = new Awaitable<AsyncGPUReadbackRequest>[heads.Count];
			for (int i = 0; i < heads.Count; i++)
			{
				posePending[i] = AsyncGPUReadback.RequestAsync(peakPoses[i]);
				scorePending[i] = AsyncGPUReadback.RequestAsync(peakScores[i]);
			}

			// a peak whose refinement fails keeps its grid pose and fine score
			var peaks = new List<(Pose4 pose, float4 score, float rms, string note)>(heads.Count);
			for (int i = 0; i < heads.Count; i++)
			{
				AsyncGPUReadbackRequest poseReq = await posePending[i];
				AsyncGPUReadbackRequest scoreReq = await scorePending[i];
				(float4 gridPose, float4 gridScore) = heads[i];
				Pose4 grid = new(gridPose.xyz, gridPose.w);
				if (poseReq.hasError || scoreReq.hasError)
				{
					peaks.Add((grid, gridScore, -1, "readback failed"));
					continue;
				}

				NativeArray<float4> data = poseReq.GetData<float4>();
				float4 pose = data[0];
				float peakRms = data[1].x;
				int support = (int)data[1].y;
				if (peakRms < 0 || math.any(!math.isfinite(pose)))
					peaks.Add((grid, gridScore, -1, peakRms == -1 ? $"only {support} samples in band" : peakRms == -2 ? "singular system" : "non-finite pose"));
				else
					peaks.Add((new Pose4(pose.xyz, pose.w), scoreReq.GetData<float4>()[0], peakRms, null));
			}

			int winner = 0;
			for (int i = 1; i < peaks.Count; i++)
				if (peaks[i].score.z > peaks[winner].score.z) winner = i;

			(Pose4 refined, float4 final, float rms, string note) = peaks[winner];
			float4 winnerPose = new(refined.translation, refined.yaw);
			float margin = final.z;
			int rival = -1;
			for (int i = 0; i < peaks.Count; i++)
			{
				if (i == winner || !IsDistinct(new float4(peaks[i].pose.translation, peaks[i].pose.yaw), winnerPose)) continue;
				if (rival < 0 || peaks[i].score.z > peaks[rival].score.z) rival = i;
			}

			if (rival >= 0) margin = final.z - peaks[rival].score.z;
			lastRefineNote = note;
			trace.Append(rival >= 0 ? $"rival {peaks[rival].score.z:F3}@{peaks[rival].pose} of {peaks.Count} peaks" : $"no rival among {peaks.Count} peaks");
			trace.Append(" [");
			for (int i = 0; i < peaks.Count; i++)
				trace.Append($"{(i > 0 ? "; " : "")}{peaks[i].score.z:F3}@{peaks[i].pose} from {heads[i].score.z:F3}@{new Pose4(heads[i].pose.xyz, heads[i].pose.w)}{(peaks[i].rms < 0 ? " " + peaks[i].note : "")}");
			trace.Append(']');
			StepCompleted.Invoke(new SearchStep(-1, refined, final.x, true));

			return new RegistrationResult
			{
				success = true,
				pose = refined,
				score = final.z,
				overlap = final.y,
				agreement = final.x,
				contradiction = final.w,
				overlapSamples = (int)(final.y * fineCount),
				margin = margin,
				failure = lastRefineNote,
				refinedRms = rms,
				surfaceSamples = fineCount,
				candidatesScored = scored,
				levelTrace = trace.ToString()
			};
		}

		/// <summary>Scores one pose at the given level (0 = fine): (agreement over the overlap, overlap fraction).</summary>
		public async Awaitable<(float agreement, float overlap)> EvaluateAsync(Pose4 pose, int level = 0)
		{
			if (IsBusy || !sourceReady || Target == null || !Target.IsAllocated) return (-1, 0);
			if (Target.PyramidDirty) Target.BuildPyramid();

			EnsureCandidateBuffers(1);
			candidateBuffer.SetData(new[] { new float4(pose.translation, pose.yaw) });
			scanner.ConfigureAtlasReader(compute);
			bool fine = level == 0;
			DispatchScore(candidateBuffer, evalScores, 1, level, fine ? fineSamples : coarseSamples, fine ? fineCount : coarseCount,
				fine ? MinOverlapSamplesFine : MinOverlapSamplesCoarse, trilinear: fine);

			AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(evalScores);
			if (req.hasError) return (-1, 0);
			float4 s = req.GetData<float4>()[0];
			return (s.x, s.y);
		}

		private void DispatchScore(ComputeBuffer candidates, ComputeBuffer scores, int count, int level, ComputeBuffer samples, int sampleCount, int minOverlapSamples, bool trilinear = false)
		{
			(Texture tex, int3 dims, float cell, bool isFine) = Target.Level(level);
			Target.BindForReading(scoreKernel);
			scoreKernel.Bind(samplesID, samples);
			compute.SetInt(sampleCountID, sampleCount);
			compute.SetInt(minOverlapSamplesID, minOverlapSamples);
			scoreKernel.Bind(candidatesID, candidates);
			compute.SetInt(candidateCountID, count);
			scoreKernel.Bind(scoresID, scores);
			scoreKernel.Bind(levelTexID, tex);
			compute.SetInts(levelDimsID, dims.x, dims.y, dims.z);
			compute.SetFloat(levelCellID, cell);
			compute.SetInt(levelIsFineID, isFine ? 1 : 0);
			compute.SetInt(scoreTrilinearID, trilinear && isFine ? 1 : 0);

			// one group per candidate, in rows: a dispatch dimension holds at most 65535 groups
			scoreKernel.DispatchGroups(math.min(count, ScoreRow), (count + ScoreRow - 1) / ScoreRow, 1);
		}

		// top-K of the scored candidates into survivors/survivorScores, unordered
		private void DispatchSelect(ComputeBuffer candidates, ComputeBuffer scores, int count, int k, int key)
		{
			compute.SetInt(candidateCountID, count);
			compute.SetInt(survivorLimitID, k);
			compute.SetInt(selectKeyID, key);

			beginSelectKernel.Bind(selectHistogramID, selectHistogram);
			beginSelectKernel.Bind(selectStateID, selectState);
			selectHistogramKernel.Bind(selectHistogramID, selectHistogram);
			selectHistogramKernel.Bind(scoresID, scores);
			selectThresholdKernel.Bind(selectHistogramID, selectHistogram);
			selectThresholdKernel.Bind(selectStateID, selectState);
			selectCompactKernel.Bind(selectStateID, selectState);
			selectCompactKernel.Bind(scoresID, scores);
			selectCompactKernel.Bind(candidatesID, candidates);
			selectCompactKernel.Bind(survivorsID, survivors);
			selectCompactKernel.Bind(survivorScoresID, survivorScores);

			int groups = (count + 255) / 256;
			beginSelectKernel.DispatchGroups(SelectBins / 256, 1, 1);
			selectHistogramKernel.DispatchGroups(groups, 1, 1);
			selectThresholdKernel.DispatchGroups(1, 1, 1);
			selectCompactKernel.DispatchGroups(groups, 1, 1);
		}

		// Gauss-Newton chained on the GPU from the given pose; no readback of its own
		private void DispatchRefine(Pose4 start, ComputeBuffer refinePose)
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
		}

		private void EnsureCandidateBuffers(int count)
		{
			if (candidateBuffer != null && candidateBuffer.count >= count) return;
			candidateBuffer?.Release();
			scoreBuffer?.Release();
			int size = math.max(count, 1024);
			candidateBuffer = new ComputeBuffer(size, 16);
			scoreBuffer = new ComputeBuffer(size, 16);
		}

		// the best `keep`, at most `quota` of them within the distinct radius of any one
		// peak's best, so rival peaks keep a share of the beam all the way down
		private static List<(float4 pose, float4 score)> KeepDiverse(List<(float4 pose, float4 score)> ranked, int keep, int quota)
		{
			List<float4> heads = new();
			List<int> counts = new();
			List<(float4 pose, float4 score)> result = new(keep);
			foreach ((float4 pose, float4 score) item in ranked)
			{
				int peak = -1;
				for (int h = 0; h < heads.Count && peak < 0; h++)
					if (!IsDistinct(item.pose, heads[h])) peak = h;
				if (peak < 0)
				{
					heads.Add(item.pose);
					counts.Add(0);
					peak = heads.Count - 1;
				}

				if (counts[peak] >= quota) continue;
				counts[peak]++;
				result.Add(item);
				if (result.Count == keep) break;
			}

			return result;
		}

		// the best candidate of each of the first `count` distinct peaks, best first
		private static List<(float4 pose, float4 score)> PeakHeads(List<(float4 pose, float4 score)> ranked, int count)
		{
			List<(float4 pose, float4 score)> heads = new(count);
			foreach ((float4 pose, float4 score) item in ranked)
			{
				bool distinct = true;
				foreach ((float4 pose, float4 _) head in heads)
					if (!IsDistinct(item.pose, head.pose)) { distinct = false; break; }
				if (!distinct) continue;
				heads.Add(item);
				if (heads.Count == count) break;
			}

			return heads;
		}

		private static bool IsDistinct(float4 a, float4 b)
		{
			float yawDiff = math.abs(math.degrees(math.atan2(math.sin(a.w - b.w), math.cos(a.w - b.w))));
			return math.distance(a.xyz, b.xyz) > DistinctTranslation || yawDiff > DistinctYawDegrees;
		}

		// ---- candidates --------------------------------------------------------
		//
		// Candidates are poses p' = rotateY(p, yaw) + t, but the lattice they are laid out
		// and expanded on is (where the search center lands, yaw), so a yaw step turns the
		// source about its center rather than the world origin, which for a scan meters
		// from the origin would swing it by meters per step.

		private float YawStep(int level) => math.radians(CoarseYawStepDegrees) * (1 << level) / (1 << (RegistrationVolume.PyramidLevels - 1));
		private float CellSize(int level) => scanner.VoxSize * (1 << level);

		/// <summary>
		/// Top-level yaw bins near the peaks of the wall-azimuth cross-correlation, each
		/// with its neighbors; null (search every yaw) when either map has too few walls.
		/// </summary>
		private List<int> YawHypothesisBins(float[] targetHistogram, out int hypotheses)
		{
			hypotheses = 0;
			if (targetHistogram == null || sourceWallSamples < MinWallSamples) return null;
			float targetWalls = 0;
			foreach (float bin in targetHistogram) targetWalls += bin;
			if (targetWalls < MinWallSamples) return null;

			float[] source = SmoothCircular(sourceYawHistogram);
			float[] target = SmoothCircular(targetHistogram);

			// target azimuth = source azimuth + yaw
			float[] correlation = new float[YawBins];
			float peak = 0;
			for (int k = 0; k < YawBins; k++)
			{
				float sum = 0;
				for (int b = 0; b < YawBins; b++) sum += source[b] * target[(b + k) % YawBins];
				correlation[k] = sum;
				peak = math.max(peak, sum);
			}

			if (peak <= 0) return null;

			List<int> peaks = new();
			for (int k = 0; k < YawBins; k++)
			{
				if (correlation[k] < YawPeakFraction * peak) continue;
				bool isMax = true;
				for (int o = -YawPeakSeparationBins; o <= YawPeakSeparationBins && isMax; o++)
				{
					if (o == 0) continue;
					float other = correlation[(k + o + YawBins) % YawBins];
					// ties go to the lower bin so a flat peak yields one hypothesis
					isMax = o < 0 ? correlation[k] > other : correlation[k] >= other;
				}

				if (isMax) peaks.Add(k);
			}

			peaks.Sort((a, b) => correlation[b].CompareTo(correlation[a]));
			if (peaks.Count > MaxYawHypotheses) peaks.RemoveRange(MaxYawHypotheses, peaks.Count - MaxYawHypotheses);
			hypotheses = peaks.Count;
			if (hypotheses == 0) return null;

			float topStep = YawStep(RegistrationVolume.PyramidLevels - 1);
			int topBins = (int)math.round(2 * math.PI / topStep);
			SortedSet<int> bins = new();
			foreach (int k in peaks)
			{
				int bin = (int)math.round(math.radians(k) / topStep);
				for (int o = -1; o <= 1; o++) bins.Add(((bin + o) % topBins + topBins) % topBins);
			}

			return new List<int>(bins);
		}

		private static float[] SmoothCircular(float[] histogram)
		{
			int n = histogram.Length;
			float[] result = new float[n];
			for (int i = 0; i < n; i++)
				result[i] = (histogram[(i - 2 + n) % n] + 2 * histogram[(i - 1 + n) % n] + 3 * histogram[i] +
					2 * histogram[(i + 1) % n] + histogram[(i + 2) % n]) / 9f;
			return result;
		}

		// every translation that keeps some of the rotated source inside the target; the
		// grid coarsens until the count fits
		private List<float4> GlobalCandidates(int level, float3 center, List<int> yawBins)
		{
			float cell = CellSize(level);
			float yawStep = YawStep(level);
			int topBins = (int)math.round(2 * math.PI / yawStep);

			Bounds target = Target.WorldBounds;
			float3 tMin = target.min, tMax = target.max;
			float3 sMin = (float3)sourceBounds.min - center, sMax = (float3)sourceBounds.max - center;

			// require some overlap: the source must land at least this far into the target
			float3 overlap = math.min(1f, (sMax - sMin) * 0.25f);

			if (yawBins == null)
			{
				yawBins = new List<int>(topBins);
				for (int i = 0; i < topBins; i++) yawBins.Add(i);
			}

			List<float4> result = new();
			float3[] corners = new float3[8];

			for (int step = 1; step <= 8; step++)
			{
				result.Clear();
				float stride = cell * step;

				foreach (int yi in yawBins)
				{
					float yaw = yi * yawStep;
					quaternion rot = quaternion.RotateY(yaw);

					// bounds of the source rotated about the center
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
					float3 rotatedCenter = math.mul(rot, center);

					for (float px = lo.x; px <= hi.x; px += stride)
					for (float py = lo.y; py <= hi.y; py += stride)
					for (float pz = lo.z; pz <= hi.z; pz += stride)
						result.Add(new float4(new float3(px, py, pz) - rotatedCenter, yaw));
				}

				if (result.Count <= MaxCandidates) break;
			}

			return result;
		}

		// the prior, turned about the center and shifted by whole cells
		private List<float4> WindowCandidates(Pose4 prior, int level, float translationRange, float yawRangeDegrees, float3 center)
		{
			float cell = CellSize(level);
			float yawStep = YawStep(level);
			int tCells = math.max(1, (int)math.ceil(translationRange / cell));
			int yawCells = math.max(1, (int)math.ceil(math.radians(yawRangeDegrees) / yawStep));
			float3 placed = prior.Apply(center);

			List<float4> result = new();
			for (int yi = -yawCells; yi <= yawCells; yi++)
			{
				float yaw = prior.yaw + yi * yawStep;
				float3 rotatedCenter = math.mul(quaternion.RotateY(yaw), center);
				for (int x = -tCells; x <= tCells; x++)
				for (int y = -tCells; y <= tCells; y++)
				for (int z = -tCells; z <= tCells; z++)
					result.Add(new float4(placed + new float3(x, y, z) * cell - rotatedCenter, yaw));
			}

			return result;
		}

		// each survivor's 3x3x3 translations x 3 yaws at the next level; a child two
		// survivors share is kept once
		private List<float4> Expand(List<(float4 pose, float4 score)> survivors, int level, float3 center)
		{
			float cell = CellSize(level);
			float yawStep = YawStep(level);
			HashSet<int4> seen = new();
			List<float4> result = new();

			foreach ((float4 pose, float4 _) in survivors)
			{
				float3 placed = pose.xyz + math.mul(quaternion.RotateY(pose.w), center);
				for (int yi = -1; yi <= 1; yi++)
				{
					float yaw = pose.w + yi * yawStep;
					float3 rotatedCenter = math.mul(quaternion.RotateY(yaw), center);
					for (int x = -1; x <= 1; x++)
					for (int y = -1; y <= 1; y++)
					for (int z = -1; z <= 1; z++)
					{
						float3 p = placed + new float3(x, y, z) * cell;
						int4 key = new((int3)math.round(p / cell), (int)math.round(yaw / yawStep));
						if (seen.Add(key)) result.Add(new float4(p - rotatedCenter, yaw));
					}
				}
			}

			return result;
		}

		public void Dispose()
		{
			coarseSamples?.Release();
			fineSamples?.Release();
			sourceStats?.Release();
			chunkListBuffer?.Release();
			candidateBuffer?.Release();
			scoreBuffer?.Release();
			foreach (ComputeBuffer buffer in peakPoses) buffer?.Release();
			foreach (ComputeBuffer buffer in peakScores) buffer?.Release();
			refinePartials?.Release();
			evalScores?.Release();
			selectHistogram?.Release();
			selectState?.Release();
			survivors?.Release();
			survivorScores?.Release();
		}
	}
}
