#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.VisualLocalization;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Re-runs a recorded two-player run's graph with no rendering, network or walking: the views,
	/// in the order they were captured, through the real pair matching and solve, scored against the
	/// simulation's truth in the same samples the live run wrote. Minutes of walk in seconds, on the
	/// same views every time, so that two settings compare directly.
	///
	/// What it holds fixed is what was recorded: each lap's landmarks as the run left them, which
	/// views were shared, how tracking moved. It can't speak for capture, tracks or joining, and it
	/// scores the graph's tip, not the rig fitted through it.
	/// </summary>
	public static class GraphReplay
	{
		/// <summary>
		/// What the goal counts: both have walked this far and been tied this long since initial
		/// alignment was complete (the joiner's watch confirming its join, else the join itself), the
		/// run is no older than this (seconds from the host's first sample) and initial alignment no
		/// older than this: a 5-minute round scores a 10-minute recording's first 5 minutes after it.
		/// </summary>
		public static float CoveredMeters = 250f, TiedSeconds = 120f, EndSeconds = float.PositiveInfinity, AfterAlignedSeconds = float.PositiveInfinity;
		public const float FarFrom = 15f, FarTo = 25f;

		/// <summary>Above 0, for tests: landmarks within this many meters of a view stand at the room's true depth along its ray.</summary>
		public static float TrueDepthRange;
		/// <summary>The noise to give those depths, as a share of the depth.</summary>
		public static float TrueDepthNoise;
		/// <summary>Whether a landmark past that range is left out rather than kept at its estimate.</summary>
		public static bool OnlyTrueDepths;
		/// <summary>For tests: every edge says exactly what the simulation's truth says, showing what's left when matching is flawless.</summary>
		public static bool PerfectEdges;
		/// <summary>For tests: with <see cref="PerfectEdges"/>, every pass tried gives an edge, showing what's left when matching never fails.</summary>
		public static bool EveryPassMatches;

		/// <summary>How long a sweep holds each editor tick: longer finishes sooner, shorter keeps the editor usable.</summary>
		public static float SecondsPerTick = 0.03f;

		public readonly struct Band
		{
			public readonly float far, near, all;
			public readonly int farCount, allCount;
			public Band(float far, int farCount, float near, float all, int allCount)
			{
				this.far = far;
				this.farCount = farCount;
				this.near = near;
				this.all = all;
				this.allCount = allCount;
			}

			public override string ToString() => FormattableString.Invariant($"{far * 100:F1} cm (n={farCount}), near {near * 100:F1}, all {all * 100:F1} (n={allCount})");
		}

		public sealed class Result
		{
			public string run, label;
			public int views, passesTried, edges;
			public int liveEdges;
			public double seconds, matchSeconds, solveSeconds, largestSolveSeconds;
			public int solves;
			public Band liveRig, liveTip, tip, final, tipWithFinalYaw, finalWithTipYaw;
			public string later = "";

			public override string ToString() => FormattableString.Invariant(
				$"{run} {label}: replay tip {tip}; final map {final}; tip with final yaw {tipWithFinalYaw.far * 100:F1}; final with tip yaw {finalWithTipYaw.far * 100:F1}; {later}live rig {liveRig}; live tip {liveTip}; {views} views, {passesTried} tries, {edges} edges (live {liveEdges}), {seconds:F0} s; matching {matchSeconds:F1} s, {solves} solves {solveSeconds:F1} s, largest {largestSolveSeconds * 1000:F0} ms");
		}

		private sealed class View
		{
			public RecordedView recorded;
			public int knot;
			public int walk;
			public float3 position, forward;
			// the rows that offer a ranged landmark, and each one's range doubt along its ray (KeyframeEdgeJob.depthAxisA)
			public int[] rows;
			public float3[] doubts;
			public float3[] rays;
		}

		private sealed class Graph
		{
			public KeyframeGraph.Settings settings;
			public readonly List<KeyframeGraphKnot> knots = new();
			public readonly List<double> times = new();
			public readonly List<uint> owners = new();
			public readonly Dictionary<ulong, int> byKey = new();
			public readonly List<int> walks = new();
			public readonly List<KeyframeEdge> links = new();
			public int[] next;
			public double4[] saved, state;
			public int liveEdges;
		}

		/// <summary>The folders of the runs under the log root that were recorded, oldest first.</summary>
		public static string[] Recordings(string root) => Directory.GetDirectories(root)
			.Where(d => File.Exists(Path.Combine(d, "host.views")) && File.Exists(Path.Combine(d, "player2.views")))
			.OrderBy(d => d).ToArray();

		/// <summary>One recorded run under these settings; <paramref name="tweak"/> changes them from the code's defaults.</summary>
		/// <summary>
		/// Each setting over each recording, a run per editor tick so the editor stays alive, each
		/// line appended to <paramref name="report"/> as it comes and a mean per setting after.
		/// </summary>
		public static void Sweep(IReadOnlyList<string> folders, string report, string reuseEdges, params (string label, Action<KeyframeGraph.Settings> tweak)[] settings)
		{
			Queue<(string folder, string label, Action<KeyframeGraph.Settings> tweak)> left = new();
			foreach ((string label, Action<KeyframeGraph.Settings> tweak) in settings)
				foreach (string folder in folders) left.Enqueue((folder, label, tweak));
			Dictionary<string, List<Result>> results = new();
			IEnumerator<bool> running = null;

			void Tick()
			{
				double until = UnityEditor.EditorApplication.timeSinceStartup + SecondsPerTick;
				try
				{
					while (running != null && UnityEditor.EditorApplication.timeSinceStartup < until)
						if (!running.MoveNext()) running = null;
				}
				catch (Exception e)
				{
					UnityEditor.EditorApplication.update -= Tick;
					File.AppendAllText(report, $"SWEEP FAILED {e}\n");
					return;
				}

				if (running != null) return;
				if (left.Count == 0)
				{
					UnityEditor.EditorApplication.update -= Tick;
					foreach ((string setting, List<Result> r) in results)
						File.AppendAllText(report, FormattableString.Invariant($"== {setting}: tip {Mean(r, x => x.tip.far):F2}, final {Mean(r, x => x.final.far):F2}, tip near {Mean(r, x => x.tip.near):F2}, final near {Mean(r, x => x.final.near):F2}\n"));
					File.AppendAllText(report, "SWEEP DONE\n");
					return;
				}

				(string nextFolder, string nextLabel, Action<KeyframeGraph.Settings> nextTweak) = left.Dequeue();
				running = Steps(nextFolder, nextLabel, nextTweak, reuseEdges, result =>
				{
					if (!results.TryGetValue(nextLabel, out List<Result> list)) results[nextLabel] = list = new();
					list.Add(result);
					File.AppendAllText(report, result + "\n");
				});
			}

			UnityEditor.EditorApplication.update += Tick;
		}

		private static double Mean(List<Result> results, Func<Result, float> of) => results.Average(r => (double)of(r)) * 100;

		/// <param name="reuseEdges">When a set of edges has been measured under this name, solve those instead of measuring again: for settings matching doesn't read.</param>
		public static Result Run(string folder, string label = "defaults", Action<KeyframeGraph.Settings> tweak = null, string reuseEdges = null)
		{
			Result result = null;
			IEnumerator<bool> steps = Steps(folder, label, tweak, reuseEdges, r => result = r);
			while (steps.MoveNext()) { }
			return result;
		}

		// the replay, a solve at a time: the editor can get on with things between them
		private static IEnumerator<bool> Steps(string folder, string label, Action<KeyframeGraph.Settings> tweak, string reuseEdges, Action<Result> finished)
		{
			DateTime began = DateTime.UtcNow;
			KeyframeGraph.Settings settings = new();
			tweak?.Invoke(settings);

			List<TwoPlayerTest.Sample> hostSamples = TwoPlayerTest.ReadSamples(Path.Combine(folder, "host.jsonl"));
			List<TwoPlayerTest.Sample> playerSamples = TwoPlayerTest.ReadSamples(Path.Combine(folder, "player2.jsonl"));
			List<RecordedView> recorded = ViewRecorder.Read(Path.Combine(folder, "host.views"));
			recorded.AddRange(ViewRecorder.Read(Path.Combine(folder, "player2.views")));
			uint hostOwner = recorded[0].owner;

			Graph graph = LoadGraph(Path.Combine(folder, "host.graph.json"), settings);
			List<TwoPlayerTest.Sample> Samples(uint owner) => owner == hostOwner ? hostSamples : playerSamples;
			for (int k = 0; k < graph.knots.Count; k++) graph.times.Add(TimeAtPath(Samples(graph.owners[k]), graph.knots[k].path));

			List<View> views = new();
			foreach (RecordedView r in recorded)
				if (graph.byKey.TryGetValue(r.knotKey, out int knot)) views.Add(Prepare(r, knot, graph, settings));
			views.Sort((a, b) => a.recorded.capturedAt.CompareTo(b.recorded.capturedAt));

			Result result = new() { run = Path.GetFileName(folder), label = label, views = views.Count, liveEdges = graph.liveEdges };
			List<(TwoPlayerTest.Sample host, TwoPlayerTest.Sample player)> moments = Moments(hostSamples, playerSamples, ConfirmedAt(folder, playerSamples));
			result.liveRig = Score(moments, s => (s.rigPosition, s.rigYaw));
			result.liveTip = Score(moments, s => (s.tipPosition, s.tipYaw));

			Matcher matcher = new(settings);
			List<KeyframeEdge> edges = new(graph.links);
			Dictionary<(double time, bool host), (float3 t, float y)> tips = new();
			double start = views[0].recorded.capturedAt, end = views[^1].recorded.capturedAt;
			float step = math.max(settings.solveInterval, 0.5f);
			int nextView = 0;
			int solvedCount = -1;
			List<(double at, double4[] state)> snapshots = new();
			bool[] active = new bool[graph.knots.Count];
			string edgesPath = reuseEdges != null ? Path.Combine(folder, $"edges-{reuseEdges}.bin") : null;
			KeyframeEdge[][] measured = edgesPath != null && File.Exists(edgesPath) ? ReadEdges(edgesPath) : null;
			KeyframeEdge[][] measuring = new KeyframeEdge[views.Count][];
			for (double t = start; t < end + step; t += step)
			{
				Activate(graph, active, t + step);
				for (; nextView < views.Count && views[nextView].recorded.capturedAt <= t; nextView++)
				{
					if (measured != null)
					{
						foreach (KeyframeEdge e in measured[nextView])
							edges.Add(PerfectEdges ? MadePerfect(e, graph, hostOwner, hostSamples, playerSamples) : e);
						continue;
					}

					int before = edges.Count;
					matcher.RunUntil(views[nextView].recorded.capturedAt, views, nextView, graph, edges);
					if (PerfectEdges)
						for (int e = before; e < edges.Count; e++)
							edges[e] = MadePerfect(edges[e], graph, hostOwner, hostSamples, playerSamples);
					measuring[nextView] = edges.GetRange(before, edges.Count - before).ToArray();
					matcher.Arrive(views[nextView], hostOwner);
				}

				if (edges.Count - solvedCount >= settings.minNewEdges)
				{
					System.Diagnostics.Stopwatch solveClock = System.Diagnostics.Stopwatch.StartNew();
					Solve(graph, active, edges, settings, settings.iterations);
					result.solveSeconds += solveClock.Elapsed.TotalSeconds;
					result.largestSolveSeconds = Math.Max(result.largestSolveSeconds, solveClock.Elapsed.TotalSeconds);
					result.solves++;
					solvedCount = edges.Count;
					snapshots.Add((t, (double4[])graph.state.Clone()));
					yield return true;
				}

				// what each player's tracking would be placed by, until the next solve
				foreach ((TwoPlayerTest.Sample host, TwoPlayerTest.Sample player) in moments)
				{
					if (host.time < t || host.time >= t + step) continue;
					tips[(host.time, true)] = TransformAt(graph, active, hostOwner, host.walked, true);
					tips[(player.time, false)] = TransformAt(graph, active, hostOwner, player.walked, false);
				}
			}

			if (edgesPath != null && measured == null) WriteEdges(edgesPath, measuring);
			result.passesTried = matcher.Tried;
			result.matchSeconds = matcher.Seconds;
			result.edges = edges.Count - graph.links.Count;
			result.tip = Score(moments, (m, isHost) => tips.TryGetValue((isHost ? m.host.time : m.player.time, isHost), out (float3 t, float y) x) ? x : (float3.zero, 0));

			Activate(graph, active, double.PositiveInfinity);
			Solve(graph, active, edges, settings, 12);
			result.final = Score(moments, (m, isHost) => TransformAt(graph, active, hostOwner, isHost ? m.host.walked : m.player.walked, isHost));
			// each moment's head where one had it, turned as the other had it: which half of the tip's error is which
			(float3 t, float y) TipOf((TwoPlayerTest.Sample host, TwoPlayerTest.Sample player) m, bool isHost) =>
				tips.TryGetValue((isHost ? m.host.time : m.player.time, isHost), out (float3 t, float y) tip) ? tip : TransformAt(graph, active, hostOwner, isHost ? m.host.walked : m.player.walked, isHost);
			(float3 t, float y) FinalOf((TwoPlayerTest.Sample host, TwoPlayerTest.Sample player) m, bool isHost) => TransformAt(graph, active, hostOwner, isHost ? m.host.walked : m.player.walked, isHost);
			(float3 t, float y) Turned((float3 t, float y) at, float yaw, float3 rawHead) => (at.t + Pose4.RotateY(rawHead, at.y) - Pose4.RotateY(rawHead, yaw), yaw);
			result.later += EdgeTruth(edges, graph, hostOwner, hostSamples, playerSamples);
			double4[] finalState = graph.state;

			foreach (double wait in new[] { 5.0, 15.0, 30.0, 60.0 })
			{
				Band band = Score(moments, (m, isHost) =>
				{
					double due = (isHost ? m.host.time : m.player.time) + wait;
					int found = snapshots.FindIndex(x => x.at >= due);
					graph.state = found >= 0 ? snapshots[found].state : finalState;
					return FinalOf(m, isHost);
				});
				result.later += FormattableString.Invariant($"after {wait:F0} s {band}; ");
			}

			graph.state = finalState;
			result.tipWithFinalYaw = Score(moments, (m, isHost) => Turned(TipOf(m, isHost), FinalOf(m, isHost).y, isHost ? m.host.rawHead : m.player.rawHead));
			result.finalWithTipYaw = Score(moments, (m, isHost) => Turned(FinalOf(m, isHost), TipOf(m, isHost).y, isHost ? m.host.rawHead : m.player.rawHead));
			matcher.Dispose();
			result.seconds = (DateTime.UtcNow - began).TotalSeconds;
			finished(result);
		}

		private static void WriteEdges(string path, KeyframeEdge[][] perView)
		{
			using BinaryWriter w = new(File.Create(path));
			w.Write(perView.Length);
			foreach (KeyframeEdge[] edges in perView)
			{
				w.Write(edges?.Length ?? 0);
				if (edges != null) w.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(edges.AsSpan()));
			}
		}

		private static KeyframeEdge[][] ReadEdges(string path)
		{
			using BinaryReader r = new(File.OpenRead(path));
			KeyframeEdge[][] perView = new KeyframeEdge[r.ReadInt32()][];
			for (int v = 0; v < perView.Length; v++)
			{
				perView[v] = new KeyframeEdge[r.ReadInt32()];
				r.Read(System.Runtime.InteropServices.MemoryMarshal.AsBytes(perView[v].AsSpan()));
			}

			return perView;
		}

		// ---- the graph as the run ended it ----------------------------------------------------------

		private static Graph LoadGraph(string path, KeyframeGraph.Settings settings)
		{
			Graph graph = new() { settings = settings };
			string json = File.ReadAllText(path);
			int edgesAt = json.IndexOf("\"edges\":[", StringComparison.Ordinal);
			int knotsAt = json.IndexOf("\"knots\":[", StringComparison.Ordinal);
			List<double4> saved = new();
			foreach (double[] k in Rows(json.Substring(knotsAt, edgesAt - knotsAt)))
			{
				uint owner = (uint)k[0];
				graph.owners.Add(owner);
				int previous = (int)k[10];
				graph.knots.Add(new KeyframeGraphKnot { path = (float)k[1], origin = new float3((float)k[2], (float)k[3], (float)k[4]), isFixed = k[9] != 0, previous = previous, next = -1 });
				saved.Add(new double4(k[5], k[6], k[7], k[8]));
				graph.byKey[KnotKeys.Make(owner, (uint)k[11])] = graph.knots.Count - 1;
				graph.walks.Add(previous >= 0 ? graph.walks[previous] : graph.walks.Count);
			}

			graph.saved = saved.ToArray();
			graph.state = (double4[])graph.saved.Clone();
			graph.next = new int[graph.knots.Count];
			Array.Fill(graph.next, -1);
			for (int k = 0; k < graph.knots.Count; k++)
				if (graph.knots[k].previous >= 0) graph.next[graph.knots[k].previous] = k;

			foreach (double[] e in Rows(json.Substring(edgesAt)))
			{
				if (e[9] > 0)
				{
					graph.liveEdges++;
					continue;
				}

				graph.links.Add(new KeyframeEdge
				{
					knotA = (int)e[0], knotB = (int)e[1],
					pointA = new float3((float)e[2], (float)e[3], (float)e[4]), pointB = new float3((float)e[5], (float)e[6], (float)e[7]),
					yaw = (float)e[8], inliers = (int)e[9],
					positionInformation = 1f / (settings.edgeSigma * settings.edgeSigma),
					yawInformation = 1f / math.square(math.radians(settings.edgeYawSigmaDegrees)),
				});
			}

			return graph;
		}

		private static IEnumerable<double[]> Rows(string section)
		{
			int at = section.IndexOf('[') + 1;
			while (true)
			{
				int open = section.IndexOf('[', at);
				if (open < 0) yield break;
				int close = section.IndexOf(']', open);
				string[] cells = section.Substring(open + 1, close - open - 1).Split(',');
				yield return Array.ConvertAll(cells, c => double.Parse(c, CultureInfo.InvariantCulture));
				at = close + 1;
			}
		}

		private static double TimeAtPath(List<TwoPlayerTest.Sample> samples, float path)
		{
			for (int i = 0; i < samples.Count; i++)
				if (samples[i].walked >= path) return samples[i].time;
			return samples.Count > 0 ? samples[^1].time : 0;
		}

		private static View Prepare(RecordedView r, int knot, Graph graph, KeyframeGraph.Settings settings)
		{
			List<int> rows = new();
			List<float3> doubts = new();
			quaternion rotation = r.raw.rotation;
			Unity.Mathematics.Random noise = new((uint)(r.timestampNs & 0x7fffffff) | 1);
			for (int j = 0; j < r.Count; j++)
			{
				if (r.landmarks[j] < 0) continue;
				bool known = TrueDepthRange > 0 && r.trueDepths != null && float.IsFinite(r.trueDepths[j]) && r.trueDepths[j] <= TrueDepthRange;
				if (known)
				{
					float3 ray = math.mul(rotation, r.intrinsics.Ray(r.keypoints[j]));
					float depth = r.trueDepths[j] * (1 + TrueDepthNoise * (noise.NextFloat() + noise.NextFloat() + noise.NextFloat() - 1.5f) * 2f);
					r.points[j] = (float3)r.raw.position + ray * depth;
					rows.Add(j);
					doubts.Add(ray * (TrueDepthNoise * r.trueDepths[j]));
				}
				else if (!OnlyTrueDepths && KeyframeGraph.IsRanged(settings, r.inverseDepths[j], r.inverseDepthSigmas[j]))
				{
					rows.Add(j);
					doubts.Add(r.directions[j] * KeyframeGraph.DepthDoubt(r.inverseDepths[j], r.inverseDepthSigmas[j]));
				}
			}

			int rayCount = math.min(r.Count, KeyframeGraph.NewerFeatures);
			float3[] rays = new float3[rayCount];
			for (int j = 0; j < rayCount; j++) rays[j] = math.mul(rotation, r.intrinsics.Ray(r.keypoints[j]));
			return new View { recorded = r, knot = knot, walk = graph.walks[knot], rows = rows.ToArray(), doubts = doubts.ToArray(), rays = rays };
		}

		// ---- matching: what KeyframeGraph.SelectOlder and SchedulePass do ---------------------------

		private sealed class Matcher : IDisposable
		{
			private const int D = FeatureSet.DescriptorSize, Rows = XFeatExtractor.MaxKeypoints;
			private readonly KeyframeGraph.Settings settings;
			private NativeArray<half> descriptorsA = new(Rows * D, Allocator.Persistent), descriptorsB = new(Rows * D, Allocator.Persistent);
			private NativeArray<float3> pointsA = new(Rows, Allocator.Persistent), raysB = new(Rows, Allocator.Persistent), depthAxisA = new(Rows, Allocator.Persistent);
			private NativeArray<int> inliersOfA = new(Rows, Allocator.Persistent);
			private NativeArray<KeyframeEdge> result = new(1, Allocator.Persistent);
			private readonly int[] knotOfRow = new int[Rows];
			private readonly HashSet<long> pooled = new();
			private readonly Dictionary<int, int> inliersOfKnot = new();
			private uint seed = 1;
			public int Tried { get; private set; }
			public double Seconds { get; private set; }

			public Matcher(KeyframeGraph.Settings settings) => this.settings = settings;

			public void Dispose()
			{
				descriptorsA.Dispose();
				descriptorsB.Dispose();
				pointsA.Dispose();
				raysB.Dispose();
				depthAxisA.Dispose();
				inliersOfA.Dispose();
				result.Dispose();
			}

			/// <summary>A headset's queue, as KeyframeGraph.Enqueue and UpdatePairs keep it: the newest view first, the oldest dropped when full, one pass per tick of its budget.</summary>
			private sealed class Headset
			{
				public readonly List<View> waiting = new();
				public readonly List<List<View>> passes = new();
				public View newer;
				public double nextPair;
			}

			private readonly Headset hostHeadset = new(), playerHeadset = new();
			private uint hostOwner;

			public void Arrive(View view, uint host)
			{
				hostOwner = host;
				Headset own = view.recorded.owner == hostOwner ? hostHeadset : playerHeadset;
				Enqueue(own, view);
				if (view.recorded.shared) Enqueue(own == hostHeadset ? playerHeadset : hostHeadset, view);
			}

			private void Enqueue(Headset headset, View view)
			{
				if (headset.waiting.Count >= math.max(settings.maxWaiting, 1)) headset.waiting.RemoveAt(0);
				headset.waiting.Add(view);
			}

			/// <summary>What each headset's budget gets through by this time.</summary>
			public void RunUntil(double until, List<View> views, int known, Graph graph, List<KeyframeEdge> edges)
			{
				System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
				RunUntil(hostHeadset, until, views, known, graph, edges);
				RunUntil(playerHeadset, until, views, known, graph, edges);
				Seconds += clock.Elapsed.TotalSeconds;
			}

			private void RunUntil(Headset headset, double until, List<View> views, int known, Graph graph, List<KeyframeEdge> edges)
			{
				double interval = 1.0 / math.max(settings.pairsPerSecond, 0.1f);
				while (true)
				{
					while (headset.passes.Count == 0)
					{
						if (headset.waiting.Count == 0) return;
						if (math.max(headset.nextPair, headset.waiting[^1].recorded.capturedAt) > until) return;
						headset.newer = headset.waiting[^1];
						headset.waiting.RemoveAt(headset.waiting.Count - 1);
						Select(headset, until, views, known, graph);
					}

					double at = math.max(headset.nextPair, headset.newer.recorded.capturedAt);
					if (at > until) return;
					List<View> pass = headset.passes[0];
					headset.passes.RemoveAt(0);
					MeasurePass(headset.newer, pass, graph, edges);
					headset.nextPair = at + interval;
				}
			}

			private void Select(Headset headset, double at, List<View> views, int known, Graph graph)
			{
				View newer = headset.newer;
				bool isOwnView = newer.recorded.owner == hostOwner == (headset == hostHeadset);
				Place(newer, graph);
				Func<View, bool> holds = isOwnView
					? older => older.recorded.owner == newer.recorded.owner
						? older.recorded.evictedAt > at && older.recorded.capturedAt < newer.recorded.capturedAt && (older.walk != newer.walk || math.abs(graph.knots[older.knot].path - graph.knots[newer.knot].path) >= settings.pairMinWalk)
						: older.recorded.shared
					: older => older.recorded.owner != newer.recorded.owner && !older.recorded.shared && older.recorded.evictedAt > at;

				float maxCosine = math.cos(math.radians(settings.pairAngle));
				List<(float likeness, View view)> candidates = new();
				for (int i = 0; i < known; i++)
				{
					View k = views[i];
					if (k == newer || !holds(k)) continue;
					Place(k, graph);
					float3 to = k.position - newer.position;
					to.y = 0;
					if (math.lengthsq(to) > settings.pairDistance * settings.pairDistance) continue;
					float cosine = math.dot(k.forward, newer.forward);
					if (cosine < maxCosine) continue;
					candidates.Add((math.length(to) + math.degrees(math.acos(math.saturate(cosine))) / 10f, k));
				}

				candidates.Sort((x, y) => x.likeness.CompareTo(y.likeness));
				long passNs = (long)(math.max(settings.passSeconds, 0.1f) * 1e9);
				List<(List<View> views, int overlap)> passes = new();
				Dictionary<long, int> byKey = new();
				foreach ((float _, View k) in candidates)
				{
					long key = ((long)k.walk << 32) ^ (k.recorded.timestampNs / passNs);
					if (!byKey.TryGetValue(key, out int index))
					{
						index = passes.Count;
						byKey[key] = index;
						passes.Add((new List<View>(), 0));
					}

					(List<View> pooledViews, int overlap) = passes[index];
					if (pooledViews.Count >= settings.viewsPerPass) continue;
					int inFrame = Overlap(newer, k, graph);
					if (inFrame < settings.minInliers) continue;
					pooledViews.Add(k);
					passes[index] = (pooledViews, overlap + inFrame);
				}

				int kept = 0;
				foreach ((List<View> pooledViews, int overlap) in passes)
				{
					if (kept >= settings.passesPerKeyframe) break;
					if (overlap < settings.minOverlap) continue;
					kept++;
					headset.passes.Add(pooledViews);
				}
			}

			private static void Place(View view, Graph graph)
			{
				double4 x = graph.state[view.knot];
				view.position = Pose4.RotateY(view.recorded.raw.position, (float)x.w) + (float3)x.xyz;
				view.forward = Pose4.RotateY(math.mul(view.recorded.raw.rotation, math.forward()), (float)x.w);
			}

			// where the graph puts the newer view's raw frame in an older view's: KeyframeGraph.PriorToOlder
			private static Pose4 PriorToOlder(View newer, View older, Graph graph) =>
				Pose4.Between(Pose4.FromPacked((float4)graph.state[older.knot]), Pose4.FromPacked((float4)graph.state[newer.knot]));

			private static int Overlap(View newer, View older, Graph graph)
			{
				PinholeCamera camera = newer.recorded.intrinsics;
				float width = 2 * camera.cx, height = 2 * camera.cy;
				Pose4 toOlder = PriorToOlder(newer, older, graph);
				quaternion toCamera = math.conjugate(math.mul(toOlder.Rotation, newer.recorded.raw.rotation));
				float3 center = toOlder.Apply(newer.recorded.raw.position);
				int count = 0;
				foreach (int row in older.rows)
				{
					float3 p = math.mul(toCamera, older.recorded.points[row] - center);
					if (p.z < 0.1f) continue;
					float x = camera.fx * p.x / p.z + camera.cx, y = camera.fy * p.y / p.z + camera.cy;
					if (x >= 0 && y >= 0 && x < width && y < height) count++;
				}

				return count;
			}

			private void MeasurePass(View newer, List<View> pass, Graph graph, List<KeyframeEdge> edges)
			{
				pooled.Clear();
				int rows = 0;
				foreach (View view in pass)
				{
					if (view.knot == newer.knot) continue;
					RecordedView r = view.recorded;
					for (int n = 0; n < view.rows.Length; n++)
					{
						int j = view.rows[n];
						if (rows >= pointsA.Length) break;
						if (!pooled.Add(((long)r.owner << 32) | (uint)r.landmarks[j])) continue;
						NativeArray<half>.Copy(r.descriptors, j * D, descriptorsA, rows * D, D);
						pointsA[rows] = r.points[j];
						depthAxisA[rows] = view.doubts[n];
						knotOfRow[rows] = view.knot;
						rows++;
					}
				}

				if (EveryPassMatches)
				{
					Tried++;
					View firstOlder = pass.Find(v => v.knot != newer.knot);
					if (firstOlder == null) return;
					edges.Add(new KeyframeEdge { knotA = firstOlder.knot, knotB = newer.knot, pointB = (float3)newer.recorded.raw.position + math.mul(newer.recorded.raw.rotation, math.forward()) * 5f, inliers = 60 });
					return;
				}

				if (rows < settings.minInliers) return;
				RecordedView b = newer.recorded;
				NativeArray<half>.Copy(b.descriptors, 0, descriptorsB, 0, newer.rays.Length * D);
				Pose4 toOlder = PriorToOlder(newer, pass.Find(v => v.knot != newer.knot), graph);
				for (int j = 0; j < newer.rays.Length; j++) raysB[j] = Pose4.RotateY(newer.rays[j], toOlder.yaw);
				Tried++;
				new KeyframeEdgeJob
				{
					descriptorsA = descriptorsA, descriptorsB = descriptorsB,
					pointsA = pointsA, raysB = raysB, depthAxisA = depthAxisA,
					countA = rows, countB = newer.rays.Length,
					centerB = toOlder.Apply(b.raw.position), angularSigma = settings.pixelSigma / b.intrinsics.fx,
					minInliers = settings.minInliers, maxYawSigma = math.radians(settings.maxYawSigmaDegrees), robustSigmas = settings.edgeRobustSigmas, gncSigmas = settings.edgeGncSigmas,
					seed = seed++, result = result, inliersOfA = inliersOfA,
				}.Schedule().Complete();
				KeyframeEdge edge = result[0];
				if (edge.inliers <= 0) return;
				edge.pointB = toOlder.ApplyInverse(edge.pointB);
				edge.yaw = Pose4.WrapAngle(edge.yaw + toOlder.yaw);

				inliersOfKnot.Clear();
				int best = -1, bestCount = 0;
				for (int i = 0; i < edge.inliers; i++)
				{
					int knot = knotOfRow[inliersOfA[i]];
					inliersOfKnot.TryGetValue(knot, out int count);
					inliersOfKnot[knot] = ++count;
					if (count <= bestCount) continue;
					bestCount = count;
					best = knot;
				}

				edge.knotA = best;
				edge.knotB = newer.knot;
				edges.Add(edge);
			}
		}

		// ---- solving --------------------------------------------------------------------------------

		// a knot starts where the one before it is, until a solve says otherwise: as a new segment does
		private static void Activate(Graph graph, bool[] active, double until)
		{
			for (int k = 0; k < active.Length; k++)
			{
				if (active[k] || graph.times[k] > until) continue;
				active[k] = true;
				int previous = graph.knots[k].previous;
				graph.state[k] = previous >= 0 ? graph.state[previous] : graph.saved[k];
			}
		}

		private static void Solve(Graph graph, bool[] active, List<KeyframeEdge> edges, KeyframeGraph.Settings settings, int iterations)
		{
			int[] position = new int[active.Length];
			int count = 0;
			for (int k = 0; k < active.Length; k++) position[k] = active[k] ? count++ : -1;

			NativeArray<KeyframeGraphKnot> knots = new(count, Allocator.TempJob);
			NativeArray<double4> state = new(count, Allocator.TempJob);
			for (int k = 0; k < active.Length; k++)
			{
				if (!active[k]) continue;
				KeyframeGraphKnot knot = graph.knots[k];
				knot.previous = knot.previous >= 0 ? position[knot.previous] : -1;
				knot.next = -1;
				knots[position[k]] = knot;
				state[position[k]] = graph.state[k];
			}

			List<KeyframeEdge> usable = new(edges.Count);
			List<KeyframeEdgeWeight> weights = new(edges.Count);
			foreach (KeyframeEdge e in edges)
			{
				if (!active[e.knotA] || !active[e.knotB]) continue;
				KeyframeEdge inSolve = e;
				inSolve.knotA = position[e.knotA];
				inSolve.knotB = position[e.knotB];
				usable.Add(inSolve);
				weights.Add(e.inliers <= 1
					? new KeyframeEdgeWeight { sight = new float3(1, 0, 0), along = e.positionInformation, across = e.positionInformation, height = e.positionInformation, yaw = e.yawInformation }
					: KeyframeGraph.WeightOfPair(settings, e.inliers, Pose4.RotateY(e.pointB - graph.knots[e.knotB].origin, (float)graph.state[e.knotB].w)));
			}

			NativeArray<KeyframeEdge> edgeArray = new(usable.ToArray(), Allocator.TempJob);
			NativeArray<KeyframeEdgeWeight> weightArray = new(weights.ToArray(), Allocator.TempJob);
			NativeArray<double> largest = new(1, Allocator.TempJob);
			for (int i = 0; i < iterations; i++)
			{
				new KeyframeGraphSolveJob
				{
					knots = knots, edges = edgeArray, weights = weightArray, state = state, largestStep = largest,
					positionRateSquared = (double)settings.driftRate * settings.driftRate,
					yawRateSquared = math.square((double)math.radians(settings.driftYawRateDegrees)),
					positionFloorSquared = 0.005 * 0.005,
					yawFloorSquared = math.square(math.radians(0.02)),
					huber = settings.huber,
				}.Schedule().Complete();
				if (largest[0] <= 1e-4) break;
			}

			for (int k = 0; k < active.Length; k++)
				if (active[k]) graph.state[k] = state[position[k]];

			knots.Dispose();
			state.Dispose();
			edgeArray.Dispose();
			weightArray.Dispose();
			largest.Dispose();
		}

		// raw tracking to the map where this player has walked: its knots', as KeyframeGraph.Transform reads them
		// each edge against the simulation's truth: what it says two knots' placements of its point differ by, against what they really do
		private static (float3 room, float yaw) TrueRoomAt(List<TwoPlayerTest.Sample> samples, float walked)
		{
			int lo = 0, hi = samples.Count - 1;
			while (lo < hi)
			{
				int mid = (lo + hi) / 2;
				if (samples[mid].walked < walked) lo = mid + 1;
				else hi = mid;
			}

			return (samples[lo].rawRoom, samples[lo].rawRoomYaw);
		}

		private static KeyframeEdge MadePerfect(KeyframeEdge e, Graph graph, uint hostOwner, List<TwoPlayerTest.Sample> host, List<TwoPlayerTest.Sample> player)
		{
			(float3 roomA, float yawA) = TrueRoomAt(graph.owners[e.knotA] == hostOwner ? host : player, graph.knots[e.knotA].path);
			(float3 roomB, float yawB) = TrueRoomAt(graph.owners[e.knotB] == hostOwner ? host : player, graph.knots[e.knotB].path);
			e.pointA = Pose4.RotateY(Pose4.RotateY(e.pointB - roomB, -yawB), yawA) + roomA;
			e.yaw = yawA - yawB;
			return e;
		}

		private static string EdgeTruth(List<KeyframeEdge> edges, Graph graph, uint hostOwner, List<TwoPlayerTest.Sample> host, List<TwoPlayerTest.Sample> player)
		{
			(int lo, int hi)[] bins = { (20, 60), (60, 120), (120, int.MaxValue) };
			double[] lateral = new double[bins.Length], height = new double[bins.Length], yaw = new double[bins.Length];
			int[] counts = new int[bins.Length];
			foreach (KeyframeEdge e in edges)
			{
				if (e.inliers <= 1) continue;
				List<TwoPlayerTest.Sample> a = graph.owners[e.knotA] == hostOwner ? host : player, b = graph.owners[e.knotB] == hostOwner ? host : player;
				(float3 roomA, float yawA) = TrueRoomAt(a, graph.knots[e.knotA].path);
				(float3 roomB, float yawB) = TrueRoomAt(b, graph.knots[e.knotB].path);
				float3 difference = Pose4.RotateY(e.pointA - roomA, -yawA) - Pose4.RotateY(e.pointB - roomB, -yawB);
				float turn = Pose4.WrapAngle(-yawA + e.yaw + yawB);
				int bin = Array.FindIndex(bins, r => e.inliers >= r.lo && e.inliers < r.hi);
				if (bin < 0) continue;
				lateral[bin] += difference.x * difference.x + difference.z * difference.z;
				height[bin] += difference.y * difference.y;
				yaw[bin] += turn * turn;
				counts[bin]++;
			}

			StringBuilder text = new("edges against truth: ");
			for (int i = 0; i < bins.Length; i++)
				text.Append(FormattableString.Invariant($"[{bins[i].lo}+ inliers: {counts[i]}, lateral {Math.Sqrt(lateral[i] / Math.Max(counts[i], 1)) * 100:F1} cm, height {Math.Sqrt(height[i] / Math.Max(counts[i], 1)) * 100:F1} cm, yaw {Math.Sqrt(yaw[i] / Math.Max(counts[i], 1)) * 57.3:F2} deg] "));
			return text + "; ";
		}

		private static (float3 t, float y) TransformAt(Graph graph, bool[] active, uint hostOwner, float path, bool host)
		{
			int knot = -1;
			for (int k = 0; k < active.Length; k++)
				if (active[k] && graph.owners[k] == hostOwner == host && graph.knots[k].path <= path && (knot < 0 || graph.knots[k].path > graph.knots[knot].path))
					knot = k;
			if (knot < 0) return (float3.zero, 0);

			double4 x = graph.state[knot];
			int next = graph.next[knot];
			if (next >= 0 && active[next])
			{
				float span = graph.knots[next].path - graph.knots[knot].path;
				if (span > 1e-3f) x = math.lerp(x, graph.state[next], math.saturate((path - graph.knots[knot].path) / span));
			}

			return ((float3)x.xyz, (float)x.w);
		}

		// ---- scoring --------------------------------------------------------------------------------

		/// <summary>When the joiner's watch first confirmed its join, on the samples' clock; NaN when it never did or the run has no outcomes file.</summary>
		public static double ConfirmedAt(string folder, List<TwoPlayerTest.Sample> playerSamples)
		{
			string path = Path.Combine(folder, "player2.outcomes.txt");
			if (playerSamples.Count == 0 || !File.Exists(path)) return double.NaN;
			foreach (string line in File.ReadLines(path))
			{
				if (!line.Contains("Check: confirmed")) continue;
				int space = line.IndexOf(' ');
				if (space > 0 && double.TryParse(line.Substring(0, space), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)) return playerSamples[0].time + seconds;
			}

			return double.NaN;
		}

		/// <summary>The moments the goal counts: both aligned, initial alignment complete, both have walked the area, tied for long enough.</summary>
		public static List<(TwoPlayerTest.Sample host, TwoPlayerTest.Sample player)> Moments(List<TwoPlayerTest.Sample> hostSamples, List<TwoPlayerTest.Sample> playerSamples, double confirmedAt = double.NaN)
		{
			List<(TwoPlayerTest.Sample, TwoPlayerTest.Sample)> moments = new();
			double aligned = double.NaN, end = hostSamples.Count > 0 ? hostSamples[0].time + EndSeconds : double.PositiveInfinity;
			int h = 0;
			foreach (TwoPlayerTest.Sample p in playerSamples)
			{
				if (p.phase != nameof(CanonAlignment.Phase.Aligned) || p.otherViews == 0 || p.time > end) continue;
				while (h + 1 < hostSamples.Count && Math.Abs(hostSamples[h + 1].time - p.time) <= Math.Abs(hostSamples[h].time - p.time)) h++;
				TwoPlayerTest.Sample o = hostSamples[h];
				if (Math.Abs(o.time - p.time) > 1.2 || o.phase != nameof(CanonAlignment.Phase.Aligned)) continue;
				if (double.IsNaN(aligned)) aligned = double.IsNaN(confirmedAt) ? p.time : Math.Max(p.time, confirmedAt);
				if (math.min(p.walked, o.walked) >= CoveredMeters && p.time >= aligned + TiedSeconds && p.time <= aligned + AfterAlignedSeconds) moments.Add((o, p));
			}

			return moments;
		}

		public static Band Score(List<(TwoPlayerTest.Sample host, TwoPlayerTest.Sample player)> moments, Func<TwoPlayerTest.Sample, (Vector3 t, float y)> transform) =>
			Score(moments, (m, isHost) => { (Vector3 t, float y) = transform(isHost ? m.host : m.player); return ((float3)t, y); });

		private static Band Score(List<(TwoPlayerTest.Sample host, TwoPlayerTest.Sample player)> moments, Func<(TwoPlayerTest.Sample host, TwoPlayerTest.Sample player), bool, (float3 t, float y)> transform)
		{
			double far = 0, near = 0, all = 0;
			int farCount = 0, nearCount = 0, allCount = 0;
			void Add(TwoPlayerTest.Sample a, (float3 t, float y) ta, TwoPlayerTest.Sample b, (float3 t, float y) tb)
			{
				// a's head, in the room as a's tracking had it, then where b's tracking and transform put that
				float3 room = Pose4.RotateY((float3)a.rawHead - (float3)a.rawRoom, -a.rawRoomYaw);
				float3 worldA = Pose4.RotateY(a.rawHead, ta.y) + ta.t;
				float3 rawB = Pose4.RotateY(room, b.rawRoomYaw) + (float3)b.rawRoom;
				float3 worldB = Pose4.RotateY(rawB, tb.y) + tb.t;
				float3 headB = Pose4.RotateY(b.rawHead, tb.y) + tb.t;
				float apart = math.length((worldA - headB).xz);
				double offset = math.lengthsq(worldA - worldB);
				all += offset;
				allCount++;
				if (apart >= FarFrom && apart <= FarTo)
				{
					far += offset;
					farCount++;
				}
				else if (apart < FarFrom)
				{
					near += offset;
					nearCount++;
				}
			}

			foreach ((TwoPlayerTest.Sample host, TwoPlayerTest.Sample player) m in moments)
			{
				(float3 t, float y) ofHost = transform(m, true), ofPlayer = transform(m, false);
				Add(m.host, ofHost, m.player, ofPlayer);
				Add(m.player, ofPlayer, m.host, ofHost);
			}

			return new Band((float)Math.Sqrt(far / Math.Max(farCount, 1)), farCount / 2, (float)Math.Sqrt(near / Math.Max(nearCount, 1)), (float)Math.Sqrt(all / Math.Max(allCount, 1)), allCount / 2);
		}
	}
}
#endif
