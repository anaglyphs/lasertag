#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.VisualLocalization;
using ParrelSync;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Two players in XR Simulation: the main editor hosts, a ParrelSync clone of the project joins as
	/// Player 2, and both walk tours of the room (<see cref="SimLoopTest"/>) with their tracking
	/// drifting, for as long as asked. <see cref="Launch"/> does all of it from edit mode, in and
	/// out of play mode, and closes Player 2 again. Being a full editor Player 2 has Burst, which
	/// Multiplayer Play Mode's virtual players never get.
	///
	/// Ground truth: each player's simulated room stands where its tracking really is in its world.
	/// So one player's head, taken into its own room and back out through the other player's, is
	/// where the other player should see it, and the distance from there to where it does see it is
	/// the head offset the shared alignment exists to remove. Each player logs once a second to
	/// Logs/TwoPlayerTest/&lt;time&gt;/, and the host adds summary.txt when the run ends.
	///
	/// <see cref="SimEnvironmentRigFollower"/> must be enabled on the simulator prefab, or the room
	/// stands still and every offset reads 0.
	///
	/// unity-mcp can attach to the clone while it is open, and from there <see cref="Launch"/>
	/// refuses. Closing Player 2 at the end of a run gives it back. <see cref="LastSummary"/> reads
	/// from either editor.
	/// </summary>
	[InitializeOnLoad]
	public static class TwoPlayerTest
	{
		private const string Menu = "Lasertag/Two Player Test/";
		private const string RunningKey = "Anaglyph.TwoPlayerTest.Running";
		private const string SettingsKey = "Anaglyph.TwoPlayerTest.Settings";
		// a run left behind by a crash stops counting this long after it should have ended
		private const double RunGrace = 600;
		// opening the clone can take most of that, and the clone must still see the run when it's up
		private const float LaunchTimeout = 480f;
		private const double MaxSampleGap = 1.2;
		public const int MapNone = 0, MapBuild = 1, MapUse = 2;

		[Serializable]
		private class Run
		{
			public string folder;
			public double createdAt;
			public float minutes;
			public bool graph;
			public int seed;
			public int stops;
			public float spacing;
			public float speed;
			public int map;
			public float recenterAt;
		}

		[Serializable]
		private struct Sample
		{
			public double time;
			public string phase;
			public Vector3 room;
			public Vector2 roomForward;
			public Vector3 head;
			// the drift itself: where the head really is, and the error between that and tracking
			public Vector3 trueHead;
			public Vector3 driftPosition;
			public float driftYaw;
			public float walked;
			public int sites, crossLinks, ownViews, otherViews, sharedViews, lockedViews;
			public int knots, edges, pairs, solves;
			public float lastSolveSeconds, lastSolveMoved;
			// raw tracking, the frame the graph measures in: the head and the room in it, and the rig's place for it and the graph's
			public Vector3 rawHead, rawRoom;
			public float rawRoomYaw;
			public Vector3 rigPosition;
			public float rigYaw;
			public Vector3 tipPosition;
			public float tipYaw;
			public bool hasTip;
			public int frame;
			public string mapping, graph;
			public bool burst;
			public int workers;
		}

		private static Run run;
		private static string logPath;
		private static bool configured;
		private static double nextSample, nextWalk, nextGraphDump;
		private static float endsAt;
		private static bool waitingForPlayer;
		private static double launchedAt;
		private static float recenterDue;
		private static bool cloneRefreshed, cloneEntered;

		static TwoPlayerTest()
		{
			// CloneSaveBlock does this job without ParrelSync's dialog, which hangs a clone nobody is watching
			if (ClonesManager.IsClone()) EditorPrefs.SetBool("ParrelSync_DisableClonesAssetSaving", false);
			EditorApplication.playModeStateChanged += OnPlayModeChanged;
			EditorApplication.update += Tick;
		}

		// ---- driving it --------------------------------------------------------

		[MenuItem(Menu + "Run for 5 Minutes")]
		private static void RunMenu() => Report(Launch(5f));

		[MenuItem(Menu + "Run for 5 Minutes Without the Graph")]
		private static void RunWithoutGraphMenu() => Report(Launch(5f, false));

		[MenuItem(Menu + "Stop")]
		private static void StopMenu()
		{
			if (Application.isPlaying) EditorApplication.isPlaying = false;
			else Finish();
		}

		[MenuItem(Menu + "Stop", true)]
		private static bool CanStop() => IsRunning;

		private static void Report(string problem)
		{
			if (problem != null) Debug.LogWarning($"[{nameof(TwoPlayerTest)}] {problem}");
		}

		private static bool IsRunning => SessionState.GetBool(RunningKey, false);

		/// <summary>
		/// Runs two players for <paramref name="minutes"/>, from edit mode in the main editor: the clone
		/// opened, play mode in and out, the clone closed. Read <see cref="LastSummary"/> afterwards.
		/// Returns why not, or null when it started.
		/// </summary>
		/// <param name="graph">Off for a baseline: no pairs matched, nothing solved.</param>
		/// <param name="speed">Meters a second both players keep up, 0 for a sprint of 3. The headset captures walked 1 to 1.3 on average.</param>
		/// <param name="seed">Non-zero: the drift, the start poses and the walkers follow it, so runs compare.</param>
		/// <param name="map">What becomes of the saved map, each of them the space's only copy: <see cref="MapNone"/> neither reads nor writes it, <see cref="MapBuild"/> starts empty and saves over it, <see cref="MapUse"/> starts from it and leaves it as it was.</param>
		/// <param name="recenterAt">Above 0: seconds in, the host's tracking is recentered, and a minute later Player 2's, which each has to come back from.</param>
		public static string Launch(float minutes, bool graph = true, int seed = 0, int stops = 6, float spacing = 6f, float speed = 0f, int map = MapNone, float recenterAt = 0)
		{
			if (!SimPlayer.IsMainEditor) return "run this from the main editor, not a clone or a virtual player";
			if (Application.isPlaying) return "leave play mode first";
			if (EditorApplication.isCompiling) return "compiling";
			if (IsRunning) return "a run is already under way";
			string clone = ClonesManager.GetCloneProjectsPath().FirstOrDefault();
			if (clone == null) return "no ParrelSync clone of this project: make one in ParrelSync > Clones Manager";

			ReimportSettings();
			run = new Run
			{
				folder = Path.Combine(LogRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss")),
				createdAt = Now, minutes = minutes, graph = graph, seed = seed, stops = stops, spacing = spacing, speed = speed, map = map, recenterAt = recenterAt,
			};
			Directory.CreateDirectory(run.folder);
			File.WriteAllText(RunPath, JsonUtility.ToJson(run));
			File.Delete(LatestPath);

			// what the settings were goes back when the run ends
			SessionState.SetString(SettingsKey, $"{PlayModeAutoConnect.Setting.Value},{PlayModeXRSimulation.Setting.Value},{PlayModeTrackingDrift.Setting.Value}");
			PlayModeAutoConnect.Setting.Value = true;
			PlayModeXRSimulation.Setting.Value = true;
			PlayModeTrackingDrift.Setting.Value = true;
			SessionState.SetBool(RunningKey, true);

			try
			{
				if (!ClonesManager.IsCloneProjectRunning(clone)) ClonesManager.OpenProject(clone);
			}
			catch (Exception e)
			{
				Finish();
				return $"can't open the clone: {e.Message}";
			}

			waitingForPlayer = true;
			launchedAt = EditorApplication.timeSinceStartup;
			return null;
		}

		/// <summary>The summary of the last run to finish, from either editor; empty while one is under way.</summary>
		public static string LastSummary => File.Exists(LatestPath) ? File.ReadAllText(LatestPath) : "";

		// ---- paths and clocks ------------------------------------------------------

		private static string LogRoot => Path.Combine(ClonesManager.GetOriginalProjectPath(), "Logs", "TwoPlayerTest");
		private static string RunPath => Path.Combine(LogRoot, "run.json");
		private static string LatestPath => Path.Combine(LogRoot, "latest.txt");
		// the clone leaves this in the run's folder as it enters play
		private static string CloneReadyPath(Run forRun) => Path.Combine(forRun.folder, "clone-ready");
		private static double Now => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

		private static Run ReadRun()
		{
			if (!File.Exists(RunPath)) return null;
			Run read = JsonUtility.FromJson<Run>(File.ReadAllText(RunPath));
			return read != null && Now < read.createdAt + read.minutes * 60 + RunGrace ? read : null;
		}

		// ---- entering and leaving play mode -----------------------------------------

		private static void OnPlayModeChanged(PlayModeStateChange change)
		{
			switch (change)
			{
				case PlayModeStateChange.ExitingEditMode:
					// before anything wakes: the two players must not walk or drift alike
					Run coming = ReadRun();
					ApplyMapMode(coming);
					if (coming != null && coming.seed != 0) SimLoopTest.SeedNextPlay(coming.seed + (SimPlayer.IsMainEditor ? 0 : 1));
					break;

				case PlayModeStateChange.EnteredPlayMode:
					run = ReadRun();
					ApplyMapMode(run);
					configured = false;
					nextSample = nextWalk = nextGraphDump = 0;
					if (run == null) return;
					logPath = Path.Combine(run.folder, SimPlayer.IsMainEditor ? "host.jsonl" : "player2.jsonl");
					endsAt = Time.realtimeSinceStartup + run.minutes * 60f;
					recenterDue = run.recenterAt > 0 ? Time.realtimeSinceStartup + run.recenterAt + (SimPlayer.IsMainEditor ? 0 : 60) : 0;
					break;

				case PlayModeStateChange.ExitingPlayMode:
					logPath = null;
					break;

				case PlayModeStateChange.EnteredEditMode:
					ApplyMapMode(null);
					if (SimPlayer.IsMainEditor && IsRunning) Finish();
					break;
			}
		}

		// by default every run from an empty map: a saved one holds earlier runs' drift, and would
		// grow with this one's. Only the host's map is ever read or written
		private static void ApplyMapMode(Run coming)
		{
			int map = coming?.map ?? MapNone;
			bool host = SimPlayer.IsMainEditor;
			Maps.VisualMapPersistence.SkipLoadForTests = coming != null && !(host && map == MapUse);
			Maps.VisualMapPersistence.SkipSaveForTests = coming != null && !(host && map == MapBuild);
		}

		// the host's summary, the settings as they were. The run gone is what closes Player 2
		private static void Finish()
		{
			waitingForPlayer = false;
			SessionState.SetBool(RunningKey, false);
			Run ended = run ?? ReadRun();
			if (ended != null)
			{
				string summary = Summarize(ended);
				File.WriteAllText(Path.Combine(ended.folder, "summary.txt"), summary);
				File.WriteAllText(LatestPath, summary);
				Debug.Log($"[{nameof(TwoPlayerTest)}] {summary}");
			}

			if (File.Exists(RunPath)) File.Delete(RunPath);
			run = null;

			string[] settings = SessionState.GetString(SettingsKey, "").Split(',');
			if (settings.Length != 3) return;
			PlayModeAutoConnect.Setting.Value = bool.Parse(settings[0]);
			PlayModeXRSimulation.Setting.Value = bool.Parse(settings[1]);
			PlayModeTrackingDrift.Setting.Value = bool.Parse(settings[2]);
		}

		private static void Tick()
		{
			if (ClonesManager.IsClone()) TickClone();

			if (waitingForPlayer)
			{
				if (run != null && File.Exists(CloneReadyPath(run)))
				{
					waitingForPlayer = false;
					EditorApplication.isPlaying = true;
				}
				else if (EditorApplication.timeSinceStartup - launchedAt > LaunchTimeout)
				{
					Report("Player 2 never launched: is the clone stuck on a dialog, or is a stale Temp/UnityLockfile left in it?");
					Finish();
				}

				return;
			}

			if (!Application.isPlaying || logPath == null) return;

			if (!configured) configured = Configure();

			double time = Time.realtimeSinceStartupAsDouble;
			if (recenterDue > 0 && Time.realtimeSinceStartup >= recenterDue)
			{
				recenterDue = 0;
				Debug.Log($"[{nameof(TwoPlayerTest)}] recentering");
				MainXRRig.SimulateRecenter();
			}

			if (time >= nextSample)
			{
				nextSample = time + 1;
				WriteSample();
				if (time >= nextGraphDump && VisualColocationConstraintProvider.Instance != null)
				{
					nextGraphDump = time + 20;
					KeyframeGraph graph = VisualColocationConstraintProvider.Instance.Graph;
					File.WriteAllText(Path.ChangeExtension(logPath, ".graph.json"), graph.ToDebugJson());
					File.AppendAllLines(Path.ChangeExtension(logPath, ".pairs.csv"), graph.PairLog);
					graph.PairLog.Clear();
					File.WriteAllLines(Path.ChangeExtension(logPath, ".outcomes.txt"), VisualColocationConstraintProvider.Instance.RecentOutcomes);
				}
			}

			// a tour ends, and the next one starts: nobody stops playing either
			if (time >= nextWalk)
			{
				nextWalk = time + 5;
				VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
				// a joiner walks before it has found the host's frame, as a player would
				if (provider != null && VisualLocalizer.Instance != null && VisualLocalizer.Instance.Map.Count + (SimPlayer.IsMainEditor ? 0 : 1) > 0 && !SimLoopTest.Current && SimWalker.FindRoom() != null)
				{
					SimLoopTest.PlayerSpeed = run.speed;
					SimLoopTest.Run(run.stops, run.spacing);
				}
			}

			if (SimPlayer.IsMainEditor && Time.realtimeSinceStartup >= endsAt) EditorApplication.isPlaying = false;
		}

		// a prefab that never saved a setting holds the default from its last import, not the code's
		private static void ReimportSettings() => AssetDatabase.ImportAsset("Assets/Anaglyph/LaserTag/MapManager.prefab", ImportAssetOptions.ForceUpdate);

		private static void TickClone()
		{
			if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
			Run current = ReadRun();
			if (!cloneEntered)
			{
				if (current == null || Application.isPlaying) return;
				// the clone shares Assets, and may have been open since before they changed. A reload here starts this over
				if (!cloneRefreshed)
				{
					cloneRefreshed = true;
					ReimportSettings();
					AssetDatabase.Refresh();
					return;
				}

				cloneEntered = true;
				PlayModeAutoConnect.Setting.Value = true;
				PlayModeXRSimulation.Setting.Value = true;
				PlayModeTrackingDrift.Setting.Value = true;
				File.WriteAllText(CloneReadyPath(current), "");
				EditorApplication.isPlaying = true;
			}
			else if (current == null) EditorApplication.Exit(0);
		}

		private static bool Configure()
		{
			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			if (provider == null) return false;

			SerializedObject providerFields = new(provider);
			providerFields.FindProperty("graphSettings.enabled").boolValue = run.graph;
			providerFields.ApplyModifiedPropertiesWithoutUndo();
			return true;
		}

		private static void WriteSample()
		{
			Transform room = SimWalker.FindRoom();
			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			VisualLocalizer localizer = VisualLocalizer.Instance;
			if (room == null || MainXRRig.Instance == null || provider == null || localizer == null) return;

			Vector3 forward = room.forward;
			Sample s = new()
			{
				time = Now,
				phase = provider.Alignment.CurrentPhase.ToString(),
				room = room.position,
				roomForward = new Vector2(forward.x, forward.z),
				head = MainXRRig.Camera.transform.position,
				frame = Time.frameCount,
				burst = Unity.Burst.BurstCompiler.IsEnabled,
				workers = Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount,
				mapping = localizer.Mapping.ToString(),
				graph = provider.Graph.Summary,
				walked = provider.Track.Path,
				sites = provider.Sites.Count,
				knots = provider.Graph.KnotCount,
				edges = provider.Graph.EdgeCount,
				pairs = provider.Graph.PairsTried,
				solves = provider.Graph.Solves,
				lastSolveSeconds = provider.Graph.LastSolveSeconds,
				lastSolveMoved = provider.Graph.LastSolveMoved,
			};

			if (provider.Alignment.CurrentPhase == CanonAlignment.Phase.Aligned)
			{
				Pose canon = provider.Alignment.CanonSpacePose;
				Matrix4x4 worldFromRaw = MainXRRig.TrackingSpace.localToWorldMatrix * Matrix4x4.TRS(canon.position, canon.rotation, Vector3.one).inverse;
				Matrix4x4 rawFromWorld = worldFromRaw.inverse;
				s.rawHead = rawFromWorld.MultiplyPoint3x4(s.head);
				s.rawRoom = rawFromWorld.MultiplyPoint3x4(s.room);
				Vector3 forwardInRaw = rawFromWorld.MultiplyVector(forward);
				s.rawRoomYaw = Mathf.Atan2(forwardInRaw.x, forwardInRaw.z);
				s.rigPosition = worldFromRaw.GetPosition();
				Vector3 rigForward = worldFromRaw.MultiplyVector(Vector3.forward);
				s.rigYaw = Mathf.Atan2(rigForward.x, rigForward.z);
				s.hasTip = provider.Graph.TryTipTransform(out Unity.Mathematics.float4 tip);
				s.tipPosition = tip.xyz;
				s.tipYaw = tip.w;
			}

			Transform drift = SimTrackingDrift.Frame;
			if (drift)
			{
				s.trueHead = drift.InverseTransformPoint(s.head);
				s.driftPosition = drift.localPosition;
				s.driftYaw = drift.localRotation.eulerAngles.y;
			}

			foreach (VisualKeyframe k in localizer.Map.Keyframes)
			{
				if (provider.Track.IsOwn(k.node)) s.ownViews++;
				else s.otherViews++;
				if (k.shared) s.sharedViews++;
				if (k.locked) s.lockedViews++;
			}

			foreach (ConstraintSites.Site site in provider.Sites.Sites)
				if (site.crossLink)
					s.crossLinks++;

			File.AppendAllText(logPath, JsonUtility.ToJson(s) + "\n");
		}

		// ---- the summary ---------------------------------------------------------

		private static List<Sample> ReadSamples(string path)
		{
			List<Sample> samples = new();
			if (!File.Exists(path)) return samples;
			foreach (string line in File.ReadLines(path))
				if (line.Length > 0)
					samples.Add(JsonUtility.FromJson<Sample>(line));
			return samples;
		}

		private static float Yaw(in Sample s) => Mathf.Atan2(s.roomForward.x, s.roomForward.y);

		// where the viewer's world really has the other player's head: through its own room
		private static float HeadOffset(in Sample other, in Sample viewer)
		{
			float otherYaw = Yaw(other), viewerYaw = Yaw(viewer);
			float dx = other.head.x - other.room.x, dz = other.head.z - other.room.z;
			float height = other.head.y - other.room.y + viewer.room.y - other.head.y;
			float c = Mathf.Cos(-otherYaw), s = Mathf.Sin(-otherYaw);
			float roomX = c * dx + s * dz, roomZ = -s * dx + c * dz;
			c = Mathf.Cos(viewerYaw);
			s = Mathf.Sin(viewerYaw);
			float trueX = viewer.room.x + c * roomX + s * roomZ, trueZ = viewer.room.z - s * roomX + c * roomZ;
			return Mathf.Sqrt((trueX - other.head.x) * (trueX - other.head.x) + height * height + (trueZ - other.head.z) * (trueZ - other.head.z));
		}

		private static float Rms(IEnumerable<float> values)
		{
			float sum = 0;
			int count = 0;
			foreach (float v in values)
			{
				sum += v * v;
				count++;
			}

			return count > 0 ? Mathf.Sqrt(sum / count) : 0;
		}

		// how far the host's simulated tracking actually drifted, by walk apart, to hold against the headset's
		private static string DriftSummary(List<Sample> samples)
		{
			(float lo, float hi)[] ranges = { (3f, 10f), (10f, 30f), (30f, 60f), (60f, 120f), (120f, 250f), (250f, 600f) };
			float[] floorSum = new float[ranges.Length], yawSum = new float[ranges.Length];
			int[] counts = new int[ranges.Length];
			for (int i = 0; i < samples.Count; i++)
			{
				Sample a = samples[i];
				Quaternion turnA = Quaternion.Euler(0, a.driftYaw, 0);
				for (int j = i + 1; j < samples.Count; j++)
				{
					Sample b = samples[j];
					float walked = b.walked - a.walked;
					int range = Array.FindIndex(ranges, r => walked >= r.lo && walked < r.hi);
					if (range < 0) continue;
					// the same true place, through the tracking of each moment
					Vector3 difference = b.driftPosition + Quaternion.Euler(0, b.driftYaw, 0) * a.trueHead - (a.driftPosition + turnA * a.trueHead);
					floorSum[range] += difference.x * difference.x + difference.z * difference.z;
					float yaw = Mathf.DeltaAngle(a.driftYaw, b.driftYaw);
					yawSum[range] += yaw * yaw;
					counts[range]++;
				}
			}

			return "host's drift by walk apart: " + string.Join(", ", ranges.Select((r, i) =>
				counts[i] == 0 ? $"{r.lo:F0}-{r.hi:F0} m none" : $"{r.lo:F0}-{r.hi:F0} m {Mathf.Sqrt(floorSum[i] / counts[i]) * 100:F0} cm {Mathf.Sqrt(yawSum[i] / counts[i]):F2}°"));
		}

		private static string Summarize(Run ended)
		{
			List<Sample> host = ReadSamples(Path.Combine(ended.folder, "host.jsonl"));
			List<Sample> player = ReadSamples(Path.Combine(ended.folder, "player2.jsonl"));
			StringBuilder text = new($"two players, {ended.minutes:F1} min, graph {(ended.graph ? "on" : "off")}{(ended.seed != 0 ? $", seed {ended.seed}" : "")}{(ended.speed > 0 ? $", {ended.speed:F1} m/s" : "")}{(ended.map == MapUse ? ", from a saved map" : "")}: ");
			if (host.Count == 0 || player.Count == 0) return text.Append($"no comparison: {host.Count} host samples, {player.Count} from Player 2").ToString();

			List<(double time, float offset, float yaw, float apart)> pairs = new();
			int h = 0;
			foreach (Sample p in player)
			{
				// aligned, and not just because it hasn't begun to look
				if (p.phase != nameof(CanonAlignment.Phase.Aligned) || p.otherViews == 0) continue;
				while (h + 1 < host.Count && Math.Abs(host[h + 1].time - p.time) <= Math.Abs(host[h].time - p.time)) h++;
				Sample o = host[h];
				if (Math.Abs(o.time - p.time) > MaxSampleGap || o.phase != nameof(CanonAlignment.Phase.Aligned)) continue;
				float apart = new Vector2(p.head.x - o.head.x, p.head.z - o.head.z).magnitude;
				pairs.Add((p.time, HeadOffset(p, o), Mathf.DeltaAngle(Yaw(o) * Mathf.Rad2Deg, Yaw(p) * Mathf.Rad2Deg), apart));
				pairs.Add((p.time, HeadOffset(o, p), Mathf.DeltaAngle(Yaw(o) * Mathf.Rad2Deg, Yaw(p) * Mathf.Rad2Deg), apart));
			}

			if (pairs.Count == 0) return text.Append("no moment with both players aligned").ToString();

			int third = Mathf.Max(1, pairs.Count / 3);
			text.Append($"head offset within 20 m RMS {Rms(pairs.Where(x => x.apart < 20f).Select(x => x.offset)) * 100:F1} cm; ");
			text.Append("RMS ");
			text.Append(string.Join(" / ", Enumerable.Range(0, 3).Select(i => $"{Rms(pairs.Skip(i * third).Take(i < 2 ? third : int.MaxValue).Select(x => x.offset)) * 100:F0}")));
			text.Append($" cm by third of the run ({pairs.Count / 2} moments); ");
			(float lo, float hi)[] ranges = { (0f, 5f), (5f, 15f), (15f, float.PositiveInfinity) };
			text.Append("by distance apart: ");
			text.Append(string.Join(", ", ranges.Select(r =>
			{
				List<float> within = pairs.Where(x => x.apart >= r.lo && x.apart < r.hi).Select(x => x.offset).ToList();
				return $"{r.lo:F0}-{(float.IsInfinity(r.hi) ? "" : r.hi.ToString("F0"))} m {Rms(within) * 100:F0} cm";
			})));
			text.Append($"; yaw disagreement RMS {Rms(pairs.Select(x => x.yaw)):F2}°");

			if (host.All(s => s.room == host[0].room) && player.All(s => s.room == player[0].room))
				text.Append("; THE ROOM NEVER MOVED: is SimEnvironmentRigFollower enabled?");

			text.Append($"; {DriftSummary(host)}");

			Sample lastHost = host[^1], lastPlayer = player[^1];
			text.Append($"; host walked {lastHost.walked:F0} m, {lastHost.ownViews} own views + {lastHost.otherViews} others, {lastHost.sites} sites ({lastHost.crossLinks} cross-links), graph {lastHost.knots} knots, {lastHost.edges} edges from {lastHost.pairs} pairs, {lastHost.solves} solves (last {lastHost.lastSolveSeconds * 1000:F0} ms, moved {lastHost.lastSolveMoved * 100:F1} cm)");
			text.Append($"; Player 2 walked {lastPlayer.walked:F0} m, {lastPlayer.ownViews} own views + {lastPlayer.otherViews} others, {lastPlayer.sites} sites ({lastPlayer.crossLinks} cross-links), {lastPlayer.pairs} pairs tried, heard {lastPlayer.solves} solves");
			return text.ToString();
		}
	}

	/// <summary>A clone shares the main project's Assets and ProjectSettings, so it saves nothing into them.</summary>
	public class CloneSaveBlock : AssetModificationProcessor
	{
		public static string[] OnWillSaveAssets(string[] paths) => ClonesManager.IsClone() ? Array.Empty<string>() : paths;
	}
}
#endif
