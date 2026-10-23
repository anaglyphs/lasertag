#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.DepthKit.EnvScanning.Slam;
using Anaglyph.XR.VisualLocalization;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Unattended loop-closure test in XR Simulation. <see cref="SimWalker"/> runs like a
	/// player through random reachable stops on the environment's nav-mesh, back to base
	/// every few stops as players respawn, while <see cref="SimTrackingDrift"/> drifts the
	/// tracking. Nothing about the route seeks out loops. <see cref="Launch"/> runs it from
	/// edit mode, in and out of play mode.
	///
	/// Ground truth: the simulated room follows the drifted tracking frame, and colocation
	/// moves the rig to cancel that drift, so with perfect correction the room never moves
	/// in the world. Misalignment is how far the room has moved since the test began, measured
	/// at the head: where the room point the head is at now sat at the start. It is logged
	/// once a second alongside the uncorrected drift, the loop edges proposed, the visual
	/// map's size and the worst frame time, to Logs/SimLoopTest/&lt;time&gt;.jsonl, with a
	/// summary in the console when the tour ends.
	/// </summary>
	public class SimLoopTest : MonoBehaviour
	{
		private const string Menu = "Lasertag/Sim Loop Test/";

		public static SimLoopTest Current { get; private set; }

		/// <summary>The summary of the last finished run, for whoever drives the test. Kept across play mode's domain reloads.</summary>
		public static string LastSummary
		{
			get => SessionState.GetString(SummaryKey, null);
			private set => SessionState.SetString(SummaryKey, value ?? "");
		}

		public static string LastReportPath
		{
			get => SessionState.GetString(ReportKey, null);
			private set => SessionState.SetString(ReportKey, value ?? "");
		}

		private const string SummaryKey = "Anaglyph.SimLoopTest.Summary";
		private const string ReportKey = "Anaglyph.SimLoopTest.Report";
		private const string PendingKey = "Anaglyph.SimLoopTest.Pending";

		// set when Launch entered play mode for the run: it leaves play mode again when done
		private static bool exitPlayModeWhenDone;

		private SimWalker walker;
		private SimCaptureTiming timing;
		private Transform room;
		private Pose roomStart;
		private float started, nextSample, worstFrame;
		private int loops, yawOnlyLoops, solves, stops;
		private float maxMisalignment, sumMisalignment;
		private int samples;
		private StreamWriter writer;
		private readonly List<string> events = new();

		// ground truth: each of this headset's nodes, where the head really was in the room
		// when the node was made. Relative poses between them are what the graph should hold
		private readonly Dictionary<ulong, Pose4> truth = new();
		private ulong gauge;
		private readonly List<(float translation, float yaw)> loopErrors = new();
		// every loop solve against the truth, accepted or not
		private readonly List<(float translation, float yaw, bool accepted)> solveErrors = new();
		private float lastGraphRms, lastGraphMax;

		[MenuItem(Menu + "Run Around Like a Player")]
		private static void RunMenu() => Launch(10, 6f);

		[MenuItem(Menu + "Run Around Like a Player", true)]
		private static bool CanRun() => Current == null && !EditorApplication.isCompiling;

		/// <summary>
		/// Runs a test from edit mode or play mode. From edit mode it enters play mode, starts
		/// the run once the visual map has its first keyframe, and leaves play mode when the run
		/// ends, so play mode (and its battery cost) lasts only as long as the test. Read
		/// <see cref="LastSummary"/> and <see cref="LastReportPath"/> afterwards. Returns why
		/// not, or null.
		/// </summary>
		public static string Launch(int stops, float minSpacing)
		{
			if (Application.isPlaying) return Run(stops, minSpacing);
			if (EditorApplication.isCompiling) return "compiling";
			LastSummary = "";
			SessionState.SetString(PendingKey, $"{stops};{minSpacing.ToString(CultureInfo.InvariantCulture)}");
			EditorApplication.isPlaying = true;
			return null;
		}

		[InitializeOnLoadMethod]
		private static void ListenForPlayMode() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

		private static void OnPlayModeChanged(PlayModeStateChange change)
		{
			if (change != PlayModeStateChange.EnteredPlayMode) return;
			string pending = SessionState.GetString(PendingKey, "");
			if (pending.Length == 0) return;
			SessionState.EraseString(PendingKey);
			string[] parts = pending.Split(';');
			StartWhenReady(int.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture));
		}

		private static async void StartWhenReady(int stops, float minSpacing)
		{
			exitPlayModeWhenDone = true;
			float give = Time.realtimeSinceStartup + 90f;
			while (Application.isPlaying && Time.realtimeSinceStartup < give &&
			       (VisualLocalizer.Instance == null || VisualLocalizer.Instance.Map.Count == 0 || SimWalker.FindRoom() == null || MainXRRig.Instance == null))
				await Awaitable.WaitForSecondsAsync(0.5f);
			if (!Application.isPlaying) return;

			string problem = Time.realtimeSinceStartup >= give ? "the visual map never started" : Run(stops, minSpacing);
			if (problem == null) return;
			LastSummary = $"not run: {problem}";
			Debug.LogWarning($"[{nameof(SimLoopTest)}] {LastSummary}");
			exitPlayModeWhenDone = false;
			EditorApplication.isPlaying = false;
		}

		[MenuItem(Menu + "Stop")]
		private static void StopMenu()
		{
			if (Current) Current.Finish("stopped");
		}

		[MenuItem(Menu + "Stop", true)]
		private static bool CanStop() => Current != null;

		/// <summary>
		/// Starts a run through <paramref name="stops"/> random reachable stops at least
		/// <paramref name="minSpacing"/> apart, moving as a player does (running, head darting,
		/// never pausing to look around) unless <paramref name="player"/> is false; with
		/// <paramref name="returnTrip"/>, back through them in reverse. Nothing about the route
		/// seeks out loops: whatever revisits there are, a random run makes. Returns why not,
		/// or null when it started.
		/// </summary>
		public static string Run(int stops, float minSpacing, bool returnTrip = false, bool player = true)
		{
			if (!Application.isPlaying) return "not in play mode";
			if (Current) return "a test is already running";
			if (MainXRRig.Instance == null) return "no XR rig";

			GameObject host = new("Sim Loop Test");
			SimWalker walker = SimWalker.Instance ? SimWalker.Instance : host.AddComponent<SimWalker>();
			SimLoopTest test = host.AddComponent<SimLoopTest>();
			test.timing = host.AddComponent<SimCaptureTiming>();
			if (player) walker.PlayLikeAPlayer();
			string problem = test.Begin(walker, stops, minSpacing, returnTrip);
			if (problem != null) Destroy(host);
			return problem;
		}

		private string Begin(SimWalker w, int stopCount, float minSpacing, bool returnTrip)
		{
			walker = w;
			room = SimWalker.FindRoom();
			if (!room) return "no XR Simulation environment with a NavMeshSurface";

			// stops spread out: each at least minSpacing from the head and the previous stops
			List<Vector3> route = new();
			Vector3 home = MainXRRig.Camera.transform.position;
			for (int attempt = 0; attempt < 200 && route.Count < stopCount; attempt++)
			{
				if (!walker.RandomReachable(minSpacing, out Vector3 p)) continue;
				bool spaced = Vector3.Distance(Flat(p), Flat(home)) >= minSpacing;
				foreach (Vector3 q in route) spaced &= Vector3.Distance(Flat(p), Flat(q)) >= minSpacing;
				if (spaced) route.Add(p);
			}

			if (route.Count < 2) return "couldn't find spread-out reachable stops on the nav-mesh";

			// the start is the player's base: killed, or between rounds, they run back to it
			// every few stops, as players do (the only revisits a real game makes on purpose)
			List<Vector3> tour = new();
			System.Random respawns = new(route.Count * 7919 + (int)(minSpacing * 100));
			int untilRespawn = respawns.Next(2, 5);
			int respawnCount = 0;
			foreach (Vector3 stop in route)
			{
				tour.Add(stop);
				if (--untilRespawn > 0) continue;
				tour.Add(home);
				respawnCount++;
				untilRespawn = respawns.Next(2, 5);
			}

			if (returnTrip)
			{
				for (int i = route.Count - 2; i >= 0; i--) tour.Add(route[i]);
				tour.Add(home);
			}
			if (!walker.Route(tour)) return walker.Status;

			Current = this;
			if (VisualLocalizer.Instance != null) VisualLocalizer.Instance.Mapping = default;
			stops = tour.Count;
			roomStart = new Pose(room.position, room.rotation);
			started = Time.realtimeSinceStartup;
			nextSample = started;

			string folder = Path.Combine(Application.dataPath, "..", "Logs", "SimLoopTest");
			Directory.CreateDirectory(folder);
			LastReportPath = Path.GetFullPath(Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl"));
			writer = new StreamWriter(LastReportPath);

			if (SharedSlam.Instance != null) SharedSlam.Instance.EdgeProposed += OnEdge;
			if (VisualColocationConstraintProvider.Instance != null) VisualColocationConstraintProvider.Instance.LoopSolved += OnLoopSolved;
			PoseGraph.Shared.Solved += OnSolved;
			PoseGraph.Shared.NodeAdded += OnNode;
			walker.Arrived += OnArrived;

			Note($"run through {route.Count} stops with {respawnCount} runs back to base{(returnTrip ? " and back" : "")} ({tour.Count} legs), spacing ≥ {minSpacing} m, " +
			     $"{walker.walkSpeed:F1} m/s, head shake {walker.shakeDegrees:F0}°, glances ±{walker.glanceAngle:F0}° every ~{walker.glanceInterval:F1} s");
			return null;
		}

		private void OnNode(PoseNode node)
		{
			if (SharedSlam.Instance == null || PoseGraph.PlayerOf(node.key) != SharedSlam.Instance.PlayerTag) return;
			Transform head = MainXRRig.Camera.transform;
			truth[node.key] = Pose4.FromPose(room.InverseTransformPoint(head.position), Quaternion.Inverse(room.rotation) * head.rotation);
			if (gauge == 0) gauge = node.key;
		}

		// how far apart two relative poses are: translation and yaw of the one undone by the other
		private static (float translation, float yaw) Error(Pose4 measured, Pose4 actual)
		{
			Pose4 e = Pose4.Between(actual, measured);
			return (Vector3.Magnitude(e.translation), Mathf.Abs(Pose4.WrapAngle(e.yaw)) * Mathf.Rad2Deg);
		}

		private void OnEdge(PoseEdge edge)
		{
			if (edge.kind != EdgeKind.Loop) return;
			loops++;
			if (edge.sigmaTranslation > 0.5f) yawOnlyLoops++;

			string verdict = "no ground truth for these nodes";
			if (truth.TryGetValue(edge.from, out Pose4 from) && truth.TryGetValue(edge.to, out Pose4 to) &&
			    PoseGraph.Shared.TryGetNode(edge.from, out PoseNode a) && PoseGraph.Shared.TryGetNode(edge.to, out PoseNode b))
			{
				Pose4 actual = Pose4.Between(from, to);
				(float t, float y) = Error(edge.measurement, actual);
				(float dt, float dy) = Error(Pose4.Between(a.odometry, b.odometry), actual);
				loopErrors.Add((t, y));
				verdict = $"measurement off by {t * 100:F1} cm / {y:F2}° (odometry between them off by {dt * 100:F1} cm / {dy:F2}°: what the edge should fix)";
				writer?.WriteLine($"{{\"loop\":{{\"from\":\"{edge.from:X16}\",\"to\":\"{edge.to:X16}\",\"error\":{F(t)},\"errorYaw\":{F(y)},\"odometryError\":{F(dt)},\"odometryErrorYaw\":{F(dy)},\"sigma\":{F(edge.sigmaTranslation)},\"sigmaYaw\":{F(edge.sigmaYaw * Mathf.Rad2Deg)}}}}}");
			}

			Note($"loop edge {edge.from:X16} → {edge.to:X16} σ {edge.sigmaTranslation * 100:F1} cm / {edge.sigmaYaw * Mathf.Rad2Deg:F2}°: {verdict}");
		}

		// the relative pose a loop solve implies, exactly as SharedSlam.ProposeVisualLoop makes its edge
		private void OnLoopSolved(ulong old, ulong current, Pose4 correction, GravityPnPResult pose, string rejection)
		{
			if (!truth.TryGetValue(old, out Pose4 from) || !truth.TryGetValue(current, out Pose4 to) ||
			    !PoseGraph.Shared.TryGetNode(old, out PoseNode m) || !PoseGraph.Shared.TryGetNode(current, out PoseNode n)) return;

			Pose4 actual = Pose4.Between(from, to);
			(float t, float y) = Error(Pose4.Between(m.world, correction * n.world), actual);
			(float dt, float dy) = Error(Pose4.Between(m.world, n.world), actual);
			solveErrors.Add((t, y, rejection == null));
			float expected = SharedSlam.Instance != null ? SharedSlam.Instance.Drift(old, current) : float.NaN;
			writer?.WriteLine($"{{\"solve\":{{\"error\":{F(t)},\"errorYaw\":{F(y)},\"uncorrectedError\":{F(dt)},\"uncorrectedErrorYaw\":{F(dy)}," +
			                  $"\"sigma\":{F(pose.translationSigma)},\"sigmaYaw\":{F(pose.yawSigma * Mathf.Rad2Deg)},\"inliers\":{pose.inliers},\"correspondences\":{pose.correspondences}," +
			                  $"\"rmsPx\":{F(pose.rmsPixels)},\"expectedDrift\":{F(float.IsInfinity(expected) ? -1 : expected)},\"rejection\":{Quote(rejection)}}}}}");
		}

		private void OnSolved(IReadOnlyDictionary<ulong, Pose4> _)
		{
			solves++;
			(lastGraphRms, lastGraphMax) = GraphError(world: true);
			(float odoRms, float odoMax) = GraphError(world: false);
			Note($"solve {solves}: misalignment now {Misalignment(out float yaw) * 100:F1} cm / {yaw:F2}°; " +
			     $"graph error {lastGraphRms * 100:F1} cm rms / {lastGraphMax * 100:F1} cm max (odometry alone {odoRms * 100:F1} / {odoMax * 100:F1})");
		}

		/// <summary>
		/// Every own node against the truth, relative to the first: the solved world poses, or
		/// the raw odometry for comparison. Translation error, rms and max
		/// </summary>
		private (float rms, float max) GraphError(bool world)
		{
			if (gauge == 0 || !truth.TryGetValue(gauge, out Pose4 gaugeTruth) || !PoseGraph.Shared.TryGetNode(gauge, out PoseNode gaugeNode)) return (0, 0);
			double sum = 0;
			float max = 0;
			int n = 0;
			foreach ((ulong key, Pose4 actual) in truth)
			{
				if (!PoseGraph.Shared.TryGetNode(key, out PoseNode node)) continue;
				Pose4 estimated = world ? Pose4.Between(gaugeNode.world, node.world) : Pose4.Between(gaugeNode.odometry, node.odometry);
				(float t, float _) = Error(estimated, Pose4.Between(gaugeTruth, actual));
				sum += t * t;
				max = Mathf.Max(max, t);
				n++;
			}

			return n == 0 ? (0, 0) : ((float)System.Math.Sqrt(sum / n), max);
		}

		// the graph with the truth beside it, for offline analysis
		private void DumpGraph(string path)
		{
			static string G(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);
			static string P(Pose4 p) => $"[{G(p.translation.x)},{G(p.translation.y)},{G(p.translation.z)},{G(p.yaw)}]";
			StringBuilder b = new("{\"gauge\":\"" + gauge.ToString("X16") + "\",\"nodes\":[");
			bool first = true;
			foreach (PoseNode n in PoseGraph.Shared.Nodes)
			{
				if (!first) b.Append(',');
				first = false;
				b.Append($"{{\"key\":\"{n.key:X16}\",\"previous\":\"{n.previous:X16}\",\"segmentStart\":{(n.segmentStart ? "true" : "false")},\"prior\":{(n.hasPrior ? "true" : "false")},");
				b.Append($"\"odometry\":{P(n.odometry)},\"world\":{P(n.world)}");
				if (truth.TryGetValue(n.key, out Pose4 t)) b.Append($",\"truth\":{P(t)}");
				b.Append('}');
			}

			b.Append("],\"edges\":[");
			first = true;
			foreach (PoseEdge e in PoseGraph.Shared.Edges)
			{
				if (!first) b.Append(',');
				first = false;
				b.Append($"{{\"from\":\"{e.from:X16}\",\"to\":\"{e.to:X16}\",\"kind\":\"{e.kind}\",\"rejected\":{(e.rejected ? "true" : "false")},");
				b.Append($"\"measurement\":{P(e.measurement)},\"sigma\":{G(e.sigmaTranslation)},\"sigmaYaw\":{G(e.sigmaYaw)}}}");
			}

			b.Append("]}");
			File.WriteAllText(path, b.ToString());
		}

		private void OnArrived(int stop) => Note($"arrived at stop {stop + 1}/{stops}, walked {walker.DistanceWalked:F1} m; {(timing ? timing.Summary : "")}");

		private void Note(string message)
		{
			string line = $"{Time.realtimeSinceStartup - started:F1}s {message}";
			events.Add(line);
			Debug.Log($"[{nameof(SimLoopTest)}] {line}");
			writer?.WriteLine("{\"event\":" + Quote(line) + "}");
		}

		/// <summary>How far the room has moved in the world since the start, at the head, and its turn.</summary>
		private float Misalignment(out float yawDegrees)
		{
			Vector3 head = MainXRRig.Camera.transform.position;
			// the room point at the head now, and where that point was at the start
			Vector3 local = room.InverseTransformPoint(head);
			Vector3 then = roomStart.position + roomStart.rotation * local;
			yawDegrees = Mathf.DeltaAngle(roomStart.rotation.eulerAngles.y, room.rotation.eulerAngles.y);
			return Vector3.Distance(head, then);
		}

		private void Update()
		{
			if (!room || !walker)
			{
				Finish("lost the room or the walker");
				return;
			}

			worstFrame = Mathf.Max(worstFrame, Time.unscaledDeltaTime);
			float now = Time.realtimeSinceStartup;
			if (now >= nextSample)
			{
				nextSample = now + 1f;
				float misalignment = Misalignment(out float yaw);
				maxMisalignment = Mathf.Max(maxMisalignment, misalignment);
				sumMisalignment += misalignment;
				samples++;

				Transform drift = SimTrackingDrift.Frame;
				float driftMeters = drift ? drift.localPosition.magnitude : 0;
				float driftYaw = drift ? Mathf.DeltaAngle(0, drift.localEulerAngles.y) : 0;
				VisualLocalizer localizer = VisualLocalizer.Instance;

				StringBuilder sb = new("{");
				sb.Append($"\"t\":{F(now - started)},\"walked\":{F(walker.DistanceWalked)},\"misalignment\":{F(misalignment)},\"misalignmentYaw\":{F(yaw)}");
				sb.Append($",\"drift\":{F(driftMeters)},\"driftYaw\":{F(driftYaw)},\"loops\":{loops},\"solves\":{solves}");
				sb.Append($",\"keyframes\":{(localizer != null ? localizer.Map.Count : 0)},\"coverageLevel\":{(localizer != null ? localizer.Map.CoverageLevel : 0)}");
				sb.Append($",\"worstFrameMs\":{F(worstFrame * 1000)},\"status\":{Quote(walker.Status)}}}");
				writer?.WriteLine(sb.ToString());
				writer?.Flush();
				worstFrame = 0;
			}

			if (!walker.IsMoving && walker.StopsReached >= stops) Finish("tour complete");
			else if (walker.Status.StartsWith("stopped")) Finish(walker.Status);
		}

		private void Finish(string why)
		{
			if (Current != this) return;
			float misalignment = Misalignment(out float yaw);
			Transform drift = SimTrackingDrift.Frame;
			float driftMeters = drift ? drift.localPosition.magnitude : 0;
			LastSummary = $"{why}: walked {walker.DistanceWalked:F0} m in {Time.realtimeSinceStartup - started:F0} s; " +
			              $"{loops} loop edges ({yawOnlyLoops} yaw-only), {solves} solves; final misalignment {misalignment * 100:F1} cm / {yaw:F2}° " +
			              $"(mean {(samples > 0 ? sumMisalignment / samples * 100 : 0):F1} cm, max {maxMisalignment * 100:F1} cm) against {driftMeters * 100:F0} cm of raw drift";
			if (loopErrors.Count > 0)
			{
				float sumT = 0, sumY = 0, maxT = 0;
				foreach ((float t, float y) in loopErrors) { sumT += t; sumY += y; maxT = Mathf.Max(maxT, t); }
				LastSummary += $"; loop edges off by {sumT / loopErrors.Count * 100:F1} cm / {sumY / loopErrors.Count:F2}° on average (max {maxT * 100:F1} cm)";
			}

			if (solveErrors.Count > 0)
			{
				int good = 0, goodRejected = 0, acceptedCount = 0, badAccepted = 0;
				foreach ((float t, float y, bool accepted) in solveErrors)
				{
					bool right = t < 0.15f && y < 1f;
					if (right) good++;
					if (right && !accepted) goodRejected++;
					if (accepted) acceptedCount++;
					if (accepted && !right) badAccepted++;
				}

				LastSummary += $"; {solveErrors.Count} loop solves, {good} within 15 cm / 1° ({goodRejected} of those turned down), {acceptedCount} passed the gates ({badAccepted} of those wrong)";
			}

			if (VisualLocalizer.Instance != null) LastSummary += $"; mapping: {VisualLocalizer.Instance.Mapping}";
			if (timing) LastSummary += $"; {timing.Summary}";

			(float rms, float gmax) = GraphError(world: true);
			(float odoRms, float odoMax) = GraphError(world: false);
			LastSummary += $"; graph error {rms * 100:F1} cm rms / {gmax * 100:F1} cm max against odometry's {odoRms * 100:F1} / {odoMax * 100:F1}";
			Note(LastSummary);
			DumpGraph(Path.ChangeExtension(LastReportPath, ".graph.json"));

			if (SharedSlam.Instance != null) SharedSlam.Instance.EdgeProposed -= OnEdge;
			if (VisualColocationConstraintProvider.Instance != null) VisualColocationConstraintProvider.Instance.LoopSolved -= OnLoopSolved;
			PoseGraph.Shared.Solved -= OnSolved;
			PoseGraph.Shared.NodeAdded -= OnNode;
			walker.Arrived -= OnArrived;
			walker.Stop();
			writer?.Dispose();
			writer = null;
			Current = null;
			Destroy(gameObject);

			if (exitPlayModeWhenDone)
			{
				exitPlayModeWhenDone = false;
				EditorApplication.isPlaying = false;
			}
		}

		private void OnDestroy()
		{
			if (Current == this) Finish("destroyed");
		}

		private static Vector3 Flat(Vector3 v)
		{
			v.y = 0;
			return v;
		}

		private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

		private static string Quote(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
	}
}
#endif
