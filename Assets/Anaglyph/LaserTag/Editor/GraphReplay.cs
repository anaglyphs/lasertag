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
		public const float CoveredMeters = 250f, TiedSeconds = 120f, FarFrom = 15f, FarTo = 25f;

		public readonly struct Band
		{
			public readonly float far, near;
			public readonly int farCount;
			public Band(float far, int farCount, float near)
			{
				this.far = far;
				this.farCount = farCount;
				this.near = near;
			}

			public override string ToString() => FormattableString.Invariant($"{far * 100:F1} cm (n={farCount}), near {near * 100:F1}");
		}

		public sealed class Result
		{
			public string run, label;
			public int views, passesTried, edges;
			public int liveEdges;
			public double seconds;
			public Band liveRig, liveTip, tip, final;

			public override string ToString() => FormattableString.Invariant(
				$"{run} {label}: replay tip {tip}; final map {final}; live rig {liveRig}; live tip {liveTip}; {views} views, {passesTried} tries, {edges} edges (live {liveEdges}), {seconds:F0} s");
		}

		private sealed class View
		{
			public RecordedView recorded;
			public int knot;
			public int walk;
			public float3 position, forward;
			// the rows that offer a ranged landmark
			public int[] rows;
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
		public static Result Run(string folder, string label = "defaults", Action<KeyframeGraph.Settings> tweak = null)
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
			List<(TwoPlayerTest.Sample host, TwoPlayerTest.Sample player)> moments = Moments(hostSamples, playerSamples);
			result.liveRig = Score(moments, s => (s.rigPosition, s.rigYaw));
			result.liveTip = Score(moments, s => (s.tipPosition, s.tipYaw));

			Matcher matcher = new(settings);
			List<KeyframeEdge> edges = new(graph.links);
			Dictionary<(double time, bool host), (float3 t, float y)> tips = new();
			double start = views[0].recorded.capturedAt, end = views[^1].recorded.capturedAt;
			float step = math.max(settings.solveInterval, 0.5f);
			int nextView = 0;
			int solvedCount = -1;
			bool[] active = new bool[graph.knots.Count];
			for (double t = start; t < end + step; t += step)
			{
				Activate(graph, active, t + step);
				for (; nextView < views.Count && views[nextView].recorded.capturedAt <= t; nextView++)
					matcher.Measure(views[nextView], views, nextView, graph, edges);

				if (edges.Count - solvedCount >= settings.minNewEdges)
				{
					Solve(graph, active, edges, settings, settings.iterations);
					solvedCount = edges.Count;
				}

				// what each player's tracking would be placed by, until the next solve
				foreach ((TwoPlayerTest.Sample host, TwoPlayerTest.Sample player) in moments)
				{
					if (host.time < t || host.time >= t + step) continue;
					tips[(host.time, true)] = TransformAt(graph, active, hostOwner, host.walked, true);
					tips[(player.time, false)] = TransformAt(graph, active, hostOwner, player.walked, false);
				}
			}

			result.passesTried = matcher.Tried;
			result.edges = edges.Count - graph.links.Count;
			result.tip = Score(moments, (m, isHost) => tips.TryGetValue((isHost ? m.host.time : m.player.time, isHost), out (float3 t, float y) x) ? x : (float3.zero, 0));

			Activate(graph, active, double.PositiveInfinity);
			Solve(graph, active, edges, settings, 12);
			result.final = Score(moments, (m, isHost) => TransformAt(graph, active, hostOwner, isHost ? m.host.walked : m.player.walked, isHost));
			matcher.Dispose();
			result.seconds = (DateTime.UtcNow - began).TotalSeconds;
			return result;
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
			for (int j = 0; j < r.Count; j++)
				if (r.landmarks[j] >= 0 && KeyframeGraph.IsRanged(settings, r.inverseDepths[j], r.inverseDepthSigmas[j])) rows.Add(j);

			int rayCount = math.min(r.Count, KeyframeGraph.NewerFeatures);
			float3[] rays = new float3[rayCount];
			quaternion rotation = r.raw.rotation;
			for (int j = 0; j < rayCount; j++) rays[j] = math.mul(rotation, r.intrinsics.Ray(r.keypoints[j]));
			return new View { recorded = r, knot = knot, walk = graph.walks[knot], rows = rows.ToArray(), rays = rays };
		}

		// ---- matching: what KeyframeGraph.SelectOlder and SchedulePass do ---------------------------

		private sealed class Matcher : IDisposable
		{
			private const int D = FeatureSet.DescriptorSize, Rows = XFeatExtractor.MaxKeypoints;
			private readonly KeyframeGraph.Settings settings;
			private NativeArray<half> descriptorsA = new(Rows * D, Allocator.Persistent), descriptorsB = new(Rows * D, Allocator.Persistent);
			private NativeArray<float3> pointsA = new(Rows, Allocator.Persistent), raysB = new(Rows, Allocator.Persistent);
			private NativeArray<int> inliersOfA = new(Rows, Allocator.Persistent);
			private NativeArray<KeyframeEdge> result = new(1, Allocator.Persistent);
			private readonly int[] knotOfRow = new int[Rows];
			private readonly HashSet<long> pooled = new();
			private readonly Dictionary<int, int> inliersOfKnot = new();
			private uint seed = 1;
			public int Tried { get; private set; }

			public Matcher(KeyframeGraph.Settings settings) => this.settings = settings;

			public void Dispose()
			{
				descriptorsA.Dispose();
				descriptorsB.Dispose();
				pointsA.Dispose();
				raysB.Dispose();
				inliersOfA.Dispose();
				result.Dispose();
			}

			public void Measure(View newer, List<View> views, int upTo, Graph graph, List<KeyframeEdge> edges)
			{
				double at = newer.recorded.capturedAt;
				Place(newer, graph);
				// on its own headset: against all it holds of that headset's and the others' shared views
				MeasureOn(newer, views, upTo, graph, edges, older => older.recorded.owner == newer.recorded.owner
					? older.recorded.evictedAt > at && older.recorded.capturedAt < at && (older.walk != newer.walk || math.abs(graph.knots[older.knot].path - graph.knots[newer.knot].path) >= settings.pairMinWalk)
					: older.recorded.shared);

				// once shared: on each other headset, against what that one hasn't shared
				if (newer.recorded.shared)
					MeasureOn(newer, views, upTo, graph, edges, older => older.recorded.owner != newer.recorded.owner && !older.recorded.shared && older.recorded.evictedAt > at);
			}

			private void MeasureOn(View newer, List<View> views, int upTo, Graph graph, List<KeyframeEdge> edges, Func<View, bool> holds)
			{
				float maxCosine = math.cos(math.radians(settings.pairAngle));
				List<(float likeness, View view)> candidates = new();
				for (int i = 0; i < upTo; i++)
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
					int inFrame = Overlap(newer, k);
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
					MeasurePass(newer, pooledViews, graph, edges);
				}
			}

			private static void Place(View view, Graph graph)
			{
				double4 x = graph.state[view.knot];
				view.position = Pose4.RotateY(view.recorded.raw.position, (float)x.w) + (float3)x.xyz;
				view.forward = Pose4.RotateY(math.mul(view.recorded.raw.rotation, math.forward()), (float)x.w);
			}

			private static int Overlap(View newer, View older)
			{
				PinholeCamera camera = newer.recorded.intrinsics;
				float width = 2 * camera.cx, height = 2 * camera.cy;
				quaternion toCamera = math.conjugate(newer.recorded.raw.rotation);
				float3 center = newer.recorded.raw.position;
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
					foreach (int j in view.rows)
					{
						if (rows >= pointsA.Length) break;
						if (!pooled.Add(((long)r.owner << 32) | (uint)r.landmarks[j])) continue;
						NativeArray<half>.Copy(r.descriptors, j * D, descriptorsA, rows * D, D);
						pointsA[rows] = r.points[j];
						knotOfRow[rows] = view.knot;
						rows++;
					}
				}

				if (rows < settings.minInliers) return;
				RecordedView b = newer.recorded;
				NativeArray<half>.Copy(b.descriptors, 0, descriptorsB, 0, newer.rays.Length * D);
				raysB.Slice(0, newer.rays.Length).CopyFrom(newer.rays);
				Tried++;
				new KeyframeEdgeJob
				{
					descriptorsA = descriptorsA, descriptorsB = descriptorsB,
					pointsA = pointsA, raysB = raysB,
					countA = rows, countB = newer.rays.Length,
					centerB = b.raw.position, angularSigma = settings.pixelSigma / b.intrinsics.fx,
					minInliers = settings.minInliers, maxYawSigma = math.radians(settings.maxYawSigmaDegrees),
					seed = seed++, result = result, inliersOfA = inliersOfA,
				}.Schedule().Complete();
				KeyframeEdge edge = result[0];
				if (edge.inliers <= 0) return;

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
					yawRateSquared = (double)DriftModel.Default.yawRate * DriftModel.Default.yawRate,
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

		/// <summary>The moments the goal counts: both aligned, both have walked the area, tied for long enough.</summary>
		public static List<(TwoPlayerTest.Sample host, TwoPlayerTest.Sample player)> Moments(List<TwoPlayerTest.Sample> hostSamples, List<TwoPlayerTest.Sample> playerSamples)
		{
			List<(TwoPlayerTest.Sample, TwoPlayerTest.Sample)> moments = new();
			double joined = double.NaN;
			int h = 0;
			foreach (TwoPlayerTest.Sample p in playerSamples)
			{
				if (p.phase != nameof(CanonAlignment.Phase.Aligned) || p.otherViews == 0) continue;
				while (h + 1 < hostSamples.Count && Math.Abs(hostSamples[h + 1].time - p.time) <= Math.Abs(hostSamples[h].time - p.time)) h++;
				TwoPlayerTest.Sample o = hostSamples[h];
				if (Math.Abs(o.time - p.time) > 1.2 || o.phase != nameof(CanonAlignment.Phase.Aligned)) continue;
				if (double.IsNaN(joined)) joined = p.time;
				if (math.min(p.walked, o.walked) >= CoveredMeters && p.time >= joined + TiedSeconds) moments.Add((o, p));
			}

			return moments;
		}

		public static Band Score(List<(TwoPlayerTest.Sample host, TwoPlayerTest.Sample player)> moments, Func<TwoPlayerTest.Sample, (Vector3 t, float y)> transform) =>
			Score(moments, (m, isHost) => { (Vector3 t, float y) = transform(isHost ? m.host : m.player); return ((float3)t, y); });

		private static Band Score(List<(TwoPlayerTest.Sample host, TwoPlayerTest.Sample player)> moments, Func<(TwoPlayerTest.Sample host, TwoPlayerTest.Sample player), bool, (float3 t, float y)> transform)
		{
			double far = 0, near = 0;
			int farCount = 0, nearCount = 0;
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

			return new Band((float)Math.Sqrt(far / Math.Max(farCount, 1)), farCount / 2, (float)Math.Sqrt(near / Math.Max(nearCount, 1)));
		}
	}
}
