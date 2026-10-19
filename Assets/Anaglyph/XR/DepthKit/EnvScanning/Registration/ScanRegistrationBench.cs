using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Anaglyph.XR.DepthKit.EnvScanning.Registration
{
	/// <summary>
	/// Registers one saved <see cref="EnvScan"/> against another outside the colocation
	/// provider, for repeatable tests of the search: the source goes into a scratch
	/// <see cref="EnvVolume"/> (the way relocalization feeds it), the target into a
	/// <see cref="RegistrationVolume"/>, and the result is compared with the ground truth
	/// the two files carry. Needs a running <see cref="EnvScanner"/> (play mode) for its
	/// kernels and grid; the live map is never touched
	/// </summary>
	public static class ScanRegistrationBench
	{
		public enum Search
		{
			/// <summary>Wall yaw hypotheses, then every yaw when nothing fits — what colocation does.</summary>
			Global,
			/// <summary>Every yaw from the start.</summary>
			GlobalEveryYaw,
			/// <summary>A window around <see cref="Options.prior"/> — what relocalization tries first.</summary>
			Window
		}

		public sealed class Options
		{
			public Search search = Search.Global;
			public Pose4 prior = Pose4.Identity;
			public float windowMeters = 5f;
			public float windowDegrees = 90f;
			/// <summary>Yaw pivot of a window search; the source scan's head position when null.</summary>
			public float3? pivot;
			/// <summary>A global search whose result doesn't fit these retries at every yaw, as the provider's does; defaults when null.</summary>
			public RegistrationGates gates;
			/// <summary>Moves the source by whole chunks before registering: an exact, known translation.</summary>
			public int3 sourceChunkShift;
			/// <summary>Overrides the ground truth the files imply.</summary>
			public Pose4? truth;
		}

		public struct Report
		{
			public RegistrationResult result;
			public SourceConstraint constraint;
			public Pose4 truth;
			/// <summary>Distance between where the found and the true transform put the source's center, meters.</summary>
			public float translationError;
			public float yawErrorDegrees;
			public float seconds;
			public int sourceChunks;
			public int targetChunks;
			public int sourceSamples;
			/// <summary>The global search fell back to every yaw.</summary>
			public bool everyYawFallback;
			public string failure;

			public bool Ran => failure == null;

			public override string ToString() => !Ran
				? $"bench failed: {failure}"
				: $"error {translationError * 100:F1}cm / {yawErrorDegrees:F2}° (truth {truth}) in {seconds * 1000:F0}ms{(everyYawFallback ? " after every-yaw fallback" : "")}; " +
				  $"source {sourceChunks} chunks {sourceSamples} samples, {constraint}; target {targetChunks} chunks; result {result}";
		}

		/// <summary>Loads both files (bare names resolve into <see cref="EnvScan.DefaultDirectory"/>) and runs.</summary>
		public static async Awaitable<Report> RunAsync(string sourcePath, string targetPath, Options options = null)
		{
			EnvScan source = null, target = null;
			try
			{
				source = EnvScan.Load(sourcePath);
				target = EnvScan.Load(targetPath);
				return await RunAsync(source, target, options);
			}
			catch (Exception e)
			{
				return new Report { failure = e.Message };
			}
			finally
			{
				source?.Dispose();
				target?.Dispose();
			}
		}

		public static async Awaitable<Report> RunAsync(EnvScan source, EnvScan target, Options options = null)
		{
			options ??= new Options();
			Report report = new();
			EnvScanner scanner = EnvScanner.Instance;
			if (!scanner) return Fail("no EnvScanner (enter play mode)");

			string why = source.Incompatibility(scanner) ?? target.Incompatibility(scanner);
			if (why != null) return Fail(why);
			if (source.ChunkCount == 0 || target.ChunkCount == 0) return Fail("empty scan");

			float3 shift = (float3)options.sourceChunkShift * scanner.ChunkWorldSizeDim;
			report.truth = options.truth ?? EnvScan.GroundTruth(source, target) * new Pose4(-shift, 0);

			ComputeShader shader = TsdfRegistration.LoadShader();
			EnvVolume scratch = null;
			RegistrationVolume volume = null;
			TsdfRegistration registration = null;
			try
			{
				int side = (int)math.ceil(math.pow(source.ChunkCount, 1f / 3f) + 1e-4f);
				scratch = scanner.CreateVolume(new int3(side), "ScanRegistrationBench Source");
				report.sourceChunks = source.UploadInto(scanner, scratch, options.sourceChunkShift);

				volume = new RegistrationVolume(shader, scanner);
				if (!target.FillRegistrationVolume(volume))
					return Fail($"target spans {target.ChunkBounds.max - target.ChunkBounds.min + 1} chunks, more than a registration volume holds");
				report.targetChunks = target.ChunkCount;

				registration = new TsdfRegistration(shader, scanner) { Target = volume };
				Stopwatch watch = Stopwatch.StartNew();

				List<int> chunks = new(scratch.Chunks);
				if (!await registration.SetSourceChunksAsync(chunks, scratch))
					return Fail($"no usable samples in the source ({chunks.Count} chunks)");
				report.sourceSamples = registration.FineSampleCount;
				report.constraint = registration.Constraint;

				RegistrationResult result;
				switch (options.search)
				{
					case Search.Window:
						float3 pivot = options.pivot ?? (float3)source.Head.position + shift;
						result = await registration.RegisterAsync(options.prior, options.windowMeters, options.windowDegrees, pivot);
						break;
					case Search.GlobalEveryYaw:
						result = await registration.RegisterAsync(yawHypotheses: false);
						break;
					default:
						result = await registration.RegisterAsync(yawHypotheses: true);
						if (result.yawHypotheses > 0 && !(options.gates ?? new RegistrationGates()).Fits(result, scanner))
						{
							result = await registration.RegisterAsync(yawHypotheses: false);
							report.everyYawFallback = true;
						}

						break;
				}

				report.seconds = (float)watch.Elapsed.TotalSeconds;
				report.result = result;

				float3 center = registration.SourceBounds.center;
				report.translationError = math.distance(result.pose.Apply(center), report.truth.Apply(center));
				report.yawErrorDegrees = math.degrees(math.abs(Pose4.WrapAngle(result.pose.yaw - report.truth.yaw)));
				return report;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				return Fail(e.Message);
			}
			finally
			{
				registration?.Dispose();
				volume?.Dispose();
				scratch?.Dispose();
			}

			Report Fail(string failure)
			{
				report.failure = failure;
				return report;
			}
		}

		// ---- sweep ---------------------------------------------------------------

		public struct SweepReport
		{
			public int cases, rightAccepted, wrongAccepted;
			public Dictionary<string, int> rightRejected, wrongRejected;
			public List<float> rightAgreement, wrongAgreement, rightContradiction, wrongContradiction;
			/// <summary>One line per wrong acceptance and per correct result rejected by anything but coverage.</summary>
			public List<string> notable;
			/// <summary>One CSV row per case (<see cref="RowHeader"/>), with the <see cref="Probe"/> metrics at its pose.</summary>
			public List<string> rows;

			public override string ToString()
			{
				StringBuilder b = new();
				b.Append($"{cases} cases: right accepted {rightAccepted}, WRONG ACCEPTED {wrongAccepted}, right rejected [{Join(rightRejected)}], wrong rejected [{Join(wrongRejected)}]; ");
				b.Append($"agreement right {Stats(rightAgreement)}, wrong {Stats(wrongAgreement)}; ");
				b.Append($"contradiction right {Stats(rightContradiction)}, wrong {Stats(wrongContradiction)}");
				foreach (string line in notable) b.Append("\n  ").Append(line);
				return b.ToString();
			}

			private static string Join(Dictionary<string, int> d)
			{
				List<string> parts = new();
				foreach ((string k, int v) in d) parts.Add($"{k} {v}");
				return string.Join(", ", parts);
			}

			private static string Stats(List<float> v)
			{
				if (v.Count == 0) return "none";
				v.Sort();
				return $"{v[0]:F2}..{v[v.Count - 1]:F2} (median {v[v.Count / 2]:F2}, n={v.Count})";
			}
		}

		/// <summary>
		/// Registers partial views of <paramref name="source"/> (cones from its head at eight
		/// headings and two ranges, and discs of three radii) against the full
		/// <paramref name="target"/> and against each half of it, the way a joiner's young scan
		/// meets a host map that may not cover what the joiner sees. Counts how often the
		/// provider's gates accept a wrong pose and reject a right one. <paramref name="truth"/>
		/// overrides the ground truth the files imply: device scans, where a recenter moves the
		/// head inside an unmoved tracking space, carry none
		/// </summary>
		public static async Awaitable<SweepReport> SweepAsync(EnvScan source, EnvScan target, RegistrationGates gates = null, Pose4? truth = null)
		{
			gates ??= new RegistrationGates();
			EnvScanner scanner = EnvScanner.Instance;
			SweepReport report = new()
			{
				rightRejected = new(), wrongRejected = new(),
				rightAgreement = new(), wrongAgreement = new(), rightContradiction = new(), wrongContradiction = new(), notable = new(), rows = new()
			};
			if (!scanner) return report;

			float face = TsdfRegistration.SurfaceSamplesPerChunkFaces(scanner, 1);
			float half = scanner.ChunkWorldSizeDim * 0.5f;
			float3 Center(int chunk) => scanner.ChunkCoordToCornerWorldPos(scanner.ChunkIndexToChunkCoord(chunk)) + half;

			List<float> xs = new();
			foreach (int chunk in target.Chunks.Keys) xs.Add(Center(chunk).x);
			xs.Sort();
			float mid = xs[xs.Count / 2];
			List<(string name, EnvScan scan)> targets = new() { ("full", target), ("half-", Subset(scanner, target, c => c.x < mid)), ("half+", Subset(scanner, target, c => c.x >= mid)) };

			float3 head = source.Head.position;
			List<(string name, Func<float3, bool> keep)> views = new();
			for (int h = 0; h < 8; h++)
			{
				float a = math.radians(h * 45f);
				float3 dir = new(math.sin(a), 0, math.cos(a));
				foreach (float range in new[] { 3.5f, 5.5f })
					views.Add(($"view {h * 45}° {range}m", c =>
					{
						float3 d = c - head;
						float l = math.length(d.xz);
						return l <= range && (l < 1.6f || math.dot(new float3(d.x, 0, d.z) / l, dir) > 0.64f);
					}));
			}

			foreach (float radius in new[] { 2.5f, 4f, 8f })
				views.Add(($"disc {radius}m", c => math.distance(c.xz, head.xz) <= radius));

			try
			{
				foreach ((string tName, EnvScan tScan) in targets)
				foreach ((string vName, Func<float3, bool> keep) in views)
				{
					using EnvScan view = Subset(scanner, source, keep);
					if (view.ChunkCount < 2) continue;
					Report r = await RunAsync(view, tScan, new Options { search = Search.Global, truth = truth, gates = gates });
					if (!r.Ran) continue;

					RegistrationResult res = r.result;
					report.cases++;
					bool right = r.translationError < 0.3f && r.yawErrorDegrees < 3f;
					(right ? report.rightAgreement : report.wrongAgreement).Add(res.agreement);
					(right ? report.rightContradiction : report.wrongContradiction).Add(res.contradiction);

					float minOverlap = gates.MinOverlapSamples(scanner), maxRms = gates.MaxRefinedRms(scanner);
					bool covered = gates.HasCoverage(r.constraint, scanner);
					string gate = !covered ? "coverage" :
						!res.success ? "failed" :
						res.contradiction > gates.maxContradiction ? "contradiction" :
						res.agreement < gates.minAgreement ? "agreement" :
						res.overlapSamples < minOverlap ? "overlap" :
						res.margin < gates.minMarginFraction * res.score ? "margin" :
						res.refinedRms < 0 || res.refinedRms > maxRms ? "rms" : null;

					if (gate == null)
					{
						if (right) report.rightAccepted++;
						else report.wrongAccepted++;
					}
					else
					{
						Dictionary<string, int> d = right ? report.rightRejected : report.wrongRejected;
						d[gate] = (d.TryGetValue(gate, out int n) ? n : 0) + 1;
					}

					string otherGate = !covered ? "coverage" :
						!res.success ? "failed" :
						res.overlapSamples < minOverlap ? "overlap" :
						res.margin < gates.minMarginFraction * res.score ? "margin" :
						res.refinedRms < 0 || res.refinedRms > maxRms ? "rms" : "pass";
					float[] probe = Probe(scanner, view, tScan, res.pose);
					report.rows.Add($"{tName},{vName},{(right ? 1 : 0)},{r.translationError:F3},{r.yawErrorDegrees:F2},{otherGate},{gate ?? "pass"},{res.agreement:F4},{res.contradiction:F4},{res.overlapSamples / face:F2}," +
						$"{(res.score > 0 ? res.margin / res.score : 0):F4},{res.refinedRms:F4},{string.Join(",", Array.ConvertAll(probe, v => v.ToString("F4")))}");

					if ((gate == null && !right) || (gate != null && right && gate != "coverage"))
						report.notable.Add($"{(gate == null ? "WRONG ACCEPTED" : $"right rejected by {gate}")}: target {tName}, {vName} ({r.sourceChunks} chunks): " +
							$"error {r.translationError * 100:F0}cm/{r.yawErrorDegrees:F0}°, agreement {res.agreement:F2}, contradiction {res.contradiction:F2}, overlap {res.overlapSamples / face:F1} faces, " +
							$"margin {res.margin:F3} of {res.score:F3}, rms {res.refinedRms * 100:F1}cm");
				}
			}
			finally
			{
				foreach ((string _, EnvScan scan) in targets)
					if (scan != target) scan.Dispose();
			}

			return report;
		}

		public const string RowHeader = "target,view,right,terr,yerr,gate,verdict,agreement,contradiction,overlap,marginfrac,rms," +
			"agree_nn,agree_tri,in25_nn,in50_nn,in100_nn,in25_tri,in50_tri,in100_tri,saturated,median_tri,observed,free,free_c4,free_c8,free_c16";

		/// <summary>
		/// CPU replay of how <paramref name="source"/> fits <paramref name="target"/> at
		/// <paramref name="pose"/>, to compare candidate fit metrics on sim and device scans:
		/// the GPU's soft agreement (nearest voxel, tolerance half a band, saturated = 0), the
		/// same from a trilinear lookup, box inlier fractions (|Δsdf| under a quarter, half
		/// and a whole band) both ways, the share landing in saturated voxels, the median
		/// trilinear |Δsdf|, and the observed share. Surface samples only, weighted as the GPU weights them
		/// </summary>
		public static float[] Probe(EnvScanner scanner, EnvScan source, EnvScan target, Pose4 pose)
		{
			int dim = scanner.VoxPerChunkDim, inner = dim - 2;
			float vox = scanner.VoxSize;
			int3 bias = scanner.ChunkTableDims / 2 * inner;
			float3 offset = scanner.OriginOffset;

			bool Lookup(EnvScan scan, int3 g, out float sdf) => LookupCount(scan, g, out sdf, out _);

			bool LookupCount(EnvScan scan, int3 g, out float sdf, out int count)
			{
				sdf = 0;
				count = 0;
				int3 chunk = (int3)math.floor((float3)g / inner);
				int index = scanner.ChunkCoordToChunkIndex(chunk);
				if (index < 0 || !scan.Chunks.TryGetValue(index, out NativeArray<EnvScanner.Voxel> voxels)) return false;
				int3 v = g - chunk * inner + 1;
				EnvScanner.Voxel voxel = voxels[v.x + v.y * dim + v.z * dim * dim];
				if (voxel.IsEmpty) return false;
				sdf = voxel.CalcDistNorm();
				count = voxel.count;
				return true;
			}

			bool Trilinear(float3 p, out float sdf)
			{
				sdf = 0;
				float3 g = (p - offset) / vox - 0.5f;
				int3 g0 = (int3)math.floor(g) + bias;
				float3 f = g - math.floor(g);
				float[] c = new float[8];
				for (int i = 0; i < 8; i++)
					if (!Lookup(target, g0 + new int3(i & 1, (i >> 1) & 1, (i >> 2) & 1), out c[i]))
						return false;
				float c00 = math.lerp(c[0], c[1], f.x), c10 = math.lerp(c[2], c[3], f.x);
				float c01 = math.lerp(c[4], c[5], f.x), c11 = math.lerp(c[6], c[7], f.x);
				sdf = math.lerp(math.lerp(c00, c10, f.y), math.lerp(c01, c11, f.y), f.z);
				return true;
			}

			const float surfaceBand = 0.5f, tolerance = 0.5f, saturated = 0.9f;
			double all = 0, obsNn = 0, obsTri = 0, agreeNn = 0, agreeTri = 0, sat = 0;
			double[] inNn = new double[3], inTri = new double[3], free = new double[4];
			int[] minCounts = { 1, 4, 8, 16 };
			float[] boxes = { 0.25f, 0.5f, 1f };
			List<float> deltas = new();

			foreach ((int index, NativeArray<EnvScanner.Voxel> voxels) in source.Chunks)
			{
				float3 corner = scanner.ChunkCoordToCornerWorldPos(scanner.ChunkIndexToChunkCoord(index));
				for (int z = 1; z < dim - 1; z++)
				for (int y = 1; y < dim - 1; y++)
				for (int x = 1; x < dim - 1; x++)
				{
					EnvScanner.Voxel voxel = voxels[x + y * dim + z * dim * dim];
					if (voxel.IsEmpty) continue;
					float s = voxel.CalcDistNorm();
					if (math.abs(s) > surfaceBand) continue;

					float Sd(int dx, int dy, int dz) => voxels[x + dx + (y + dy) * dim + (z + dz) * dim * dim].CalcDistNorm();
					float3 grad = new(Sd(1, 0, 0) - Sd(-1, 0, 0), Sd(0, 1, 0) - Sd(0, -1, 0), Sd(0, 0, 1) - Sd(0, 0, -1));
					float gLen = math.length(grad);
					float vertical = gLen > 1e-4f ? math.abs(grad.y) / gLen : 0;
					float w = (1 - math.abs(s) / surfaceBand * 0.75f) * (1 - 0.75f * vertical * vertical);
					all += w;

					float3 p = pose.Apply(corner + (new float3(x, y, z) + 0.5f) * vox);
					if (LookupCount(target, (int3)math.floor((p - offset) / vox) + bias, out float t, out int count))
					{
						obsNn += w;
						for (int k = 0; k < 4; k++)
							if (t >= saturated && count >= minCounts[k]) free[k] += w;
						bool open = math.abs(t) >= saturated;
						if (open) sat += w;
						float d = math.abs(t - s);
						if (!open) agreeNn += w * math.saturate(1 - d / tolerance);
						for (int b = 0; b < 3; b++)
							if (!open && d < boxes[b]) inNn[b] += w;
					}

					if (Trilinear(p, out float tt))
					{
						obsTri += w;
						bool open = math.abs(tt) >= saturated;
						float d = math.abs(tt - s);
						deltas.Add(open ? 2 : d);
						if (!open) agreeTri += w * math.saturate(1 - d / tolerance);
						for (int b = 0; b < 3; b++)
							if (!open && d < boxes[b]) inTri[b] += w;
					}
				}
			}

			deltas.Sort();
			float Frac(double num, double den) => den > 0 ? (float)(num / den) : 0;
			return new[]
			{
				Frac(agreeNn, obsNn), Frac(agreeTri, obsTri),
				Frac(inNn[0], obsNn), Frac(inNn[1], obsNn), Frac(inNn[2], obsNn),
				Frac(inTri[0], obsTri), Frac(inTri[1], obsTri), Frac(inTri[2], obsTri),
				Frac(sat, obsNn), deltas.Count > 0 ? deltas[deltas.Count / 2] : 0, Frac(obsNn, all),
				Frac(free[0], obsNn), Frac(free[1], obsNn), Frac(free[2], obsNn), Frac(free[3], obsNn)
			};
		}

		/// <summary>A copy of the chunks of <paramref name="scan"/> whose centers pass <paramref name="keep"/>, with its poses.</summary>
		public static EnvScan Subset(EnvScanner scanner, EnvScan scan, Func<float3, bool> keep)
		{
			EnvScan subset = EnvScan.NewFor(scanner);
			subset.TrackingSpace = scan.TrackingSpace;
			subset.Head = scan.Head;
			subset.SensorCorrection = scan.SensorCorrection;
			float half = scanner.ChunkWorldSizeDim * 0.5f;
			foreach ((int chunk, NativeArray<EnvScanner.Voxel> voxels) in scan.Chunks)
				if (keep(scanner.ChunkCoordToCornerWorldPos(scanner.ChunkIndexToChunkCoord(chunk)) + half))
					subset.Chunks[chunk] = new NativeArray<EnvScanner.Voxel>(voxels, Allocator.Persistent);
			return subset;
		}
	}
}
