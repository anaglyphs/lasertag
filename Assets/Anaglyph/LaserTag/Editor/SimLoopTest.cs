#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
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
		private const string SeedKey = "Anaglyph.SimLoopTest.Seed";
		private const string FreezeKey = "Anaglyph.SimLoopTest.Freeze";
		private static int seed;

		// before any scene object wakes: the seeds and overrides a launched run asked for, used
		// once (a play session started any other way runs as usual)
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ApplyLaunchOverrides()
		{
			seed = SessionState.GetInt(SeedKey, 0);
			int freeze = SessionState.GetInt(FreezeKey, -1);
			SessionState.EraseInt(SeedKey);
			SessionState.EraseInt(FreezeKey);
			SimTrackingDrift.SeedOverride = seed != 0 ? seed : null;
			SimEnvironmentRigFollower.SeedOverride = seed != 0 ? seed : null;
			VisualLocalizer.FreezeOverride = freeze < 0 ? null : freeze == 1;
		}

		// set when Launch entered play mode for the run: it leaves play mode again when done
		private static bool exitPlayModeWhenDone;

		private SimWalker walker;
		private SimCaptureTiming timing;
		private SimLandmarkDepthCheck depths;
		private Transform room;
		private Pose roomStart;
		private float started, nextSample, worstFrame;
		private int sitesMeasured, yawOnlySites, stops;
		private float maxMisalignment, sumMisalignment;
		private int samples;
		private StreamWriter writer;
		private readonly List<string> events = new();

		// ground truth: the correction the alignment needs now, mapping where the uncorrected
		// alignment places things onto where the room had them at the start. Every drift
		// measurement and every site is held against it at the head
		private readonly List<(float translation, float yaw)> siteErrors = new();
		// every drift measurement against the truth, accepted or not
		private readonly List<(float translation, float yaw, bool accepted)> measurementErrors = new();
		// and against the map it matched: a revisit aligns with the map as it was built, which
		// carries whatever drift there was then. Per segment, how its keyframes were misplaced:
		// truth = error ∘ placed. A measurement should equal error⁻¹ ∘ ideal
		private readonly Dictionary<ulong, Pose4> segmentPlacementError = new();
		private readonly List<(float translation, float yaw, bool accepted)> mapRelativeErrors = new();

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
		/// <param name="seed">Non-zero: the drift, the room's random origin and the walker follow it, so runs repeat.</param>
		/// <param name="freeze">Overrides whether established landmarks are frozen, for A/B runs.</param>
		public static string Launch(int stops, float minSpacing, int seed = 0, bool? freeze = null)
		{
			if (Application.isPlaying) return Run(stops, minSpacing);
			if (EditorApplication.isCompiling) return "compiling";
			LastSummary = "";
			SessionState.SetInt(SeedKey, seed);
			SessionState.SetInt(FreezeKey, freeze.HasValue ? (freeze.Value ? 1 : 0) : -1);
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
			test.depths = host.AddComponent<SimLandmarkDepthCheck>();
			if (player) walker.PlayLikeAPlayer();
			if (seed != 0) walker.Reseed(seed);
			string problem = test.Begin(walker, stops, minSpacing, returnTrip);
			if (problem != null) Destroy(host);
			return problem;
		}

		private string Begin(SimWalker w, int stopCount, float minSpacing, bool returnTrip)
		{
			walker = w;
			room = SimWalker.FindRoom();
			if (!room) return "no XR Simulation environment with a NavMeshSurface";
			if (depths) depths.Begin(room);

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

			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			if (provider != null)
			{
				provider.MeasurementSolved += OnMeasured;
				provider.SiteMeasured += OnSite;
			}

			if (VisualLocalizer.Instance != null) VisualLocalizer.Instance.KeyframeAdded += OnKeyframe;
			walker.Arrived += OnArrived;

			Note($"run through {route.Count} stops with {respawnCount} runs back to base{(returnTrip ? " and back" : "")} ({tour.Count} legs), spacing ≥ {minSpacing} m, " +
			     $"{walker.walkSpeed:F1} m/s, head shake {walker.shakeDegrees:F0}°, glances ±{walker.glanceAngle:F0}° every ~{walker.glanceInterval:F1} s; " +
			     $"seed {(seed != 0 ? seed.ToString() : "random")}, landmarks {(VisualLocalizer.Instance != null && VisualLocalizer.Instance.Map.freezeEstablished ? "frozen once established" : "refit by every sighting")}");
			return null;
		}

		/// <summary>
		/// The correction the alignment needs now: where the provider's uncorrected alignment
		/// (its canon tracking-space pose) places a point, onto where the room had that point
		/// at the start. The room follows the drifted tracking frame, so this is the drift.
		/// </summary>
		private bool TryIdealCorrection(out Pose4 ideal)
		{
			ideal = Pose4.Identity;
			CanonColocationProvider provider = CanonColocationProvider.Active;
			if (provider == null || MainXRRig.Instance == null) return false;
			Pose canon = provider.CanonSpacePose;
			Matrix4x4 start = Matrix4x4.TRS(roomStart.position, roomStart.rotation, Vector3.one);
			Matrix4x4 space = MainXRRig.TrackingSpace.localToWorldMatrix;
			Matrix4x4 canonInverse = Matrix4x4.TRS(canon.position, canon.rotation, Vector3.one).inverse;
			ideal = Pose4.FromMatrix(start * room.worldToLocalMatrix * space * canonInverse);
			return true;
		}

		// how far apart two corrections put the head, and how far turned
		private static (float translation, float yaw) Error(Pose4 measured, Pose4 actual, Vector3 at)
		{
			float t = Vector3.Distance(measured.Apply((Unity.Mathematics.float3)at), actual.Apply((Unity.Mathematics.float3)at));
			return (t, Mathf.Abs(Pose4.WrapAngle(measured.yaw - actual.yaw)) * Mathf.Rad2Deg);
		}

		// the head where the uncorrected alignment places it: where corrections act
		private Vector3 HeadInCanon()
		{
			CanonColocationProvider provider = CanonColocationProvider.Active;
			Pose canon = provider != null ? provider.CanonSpacePose : Pose.identity;
			Vector3 local = MainXRRig.TrackingSpace.InverseTransformPoint(MainXRRig.Camera.transform.position);
			return canon.position + canon.rotation * local;
		}

		// how a new segment's keyframes are misplaced: where the room truly had the camera, against where the alignment put it
		private void OnKeyframe(VisualKeyframe keyframe)
		{
			if (segmentPlacementError.ContainsKey(keyframe.node)) return;
			// the camera when the frame was taken (the room moves only with drift, slowly)
			if (HeadPoseHistory.Instance == null || !HeadPoseHistory.Instance.TryGetLocalPose(keyframe.timestampNs, out Pose local)) return;
			Transform space = MainXRRig.TrackingSpace;
			Vector3 world = space.TransformPoint(local.position);
			Quaternion worldRotation = space.rotation * local.rotation;
			Pose4 placed = Pose4.FromPose(keyframe.camera.position, keyframe.camera.rotation);
			Vector3 inRoom = room.InverseTransformPoint(world);
			Pose4 actual = Pose4.FromPose(roomStart.position + roomStart.rotation * inRoom, roomStart.rotation * Quaternion.Inverse(room.rotation) * worldRotation);
			segmentPlacementError[keyframe.node] = actual * placed.Inverse;
		}

		private void OnMeasured(VisualQuery query, Pose4 correction, GravityPnPResult pose, ulong anchor, string rejection)
		{
			if (!TryIdealCorrection(out Pose4 ideal)) return;
			bool crossLink = anchor == 0;
			Vector3 head = HeadInCanon();
			(float t, float y) = Error(correction, ideal, head);
			(float dt, float dy) = Error(Pose4.Identity, ideal, head);
			measurementErrors.Add((t, y, rejection == null));
			float mt = -1, my = -1;
			if (segmentPlacementError.TryGetValue(anchor, out Pose4 placement))
			{
				(mt, my) = Error(correction, placement.Inverse * ideal, head);
				mapRelativeErrors.Add((mt, my, rejection == null));
			}

			writer?.WriteLine($"{{\"measurement\":{{\"crossLink\":{(crossLink ? "true" : "false")},\"error\":{F(t)},\"errorYaw\":{F(y)},\"mapRelativeError\":{F(mt)},\"mapRelativeErrorYaw\":{F(my)},\"uncorrectedError\":{F(dt)},\"uncorrectedErrorYaw\":{F(dy)}," +
			                  $"\"sigma\":{F(pose.translationSigma)},\"sigmaYaw\":{F(pose.yawSigma * Mathf.Rad2Deg)},\"inliers\":{pose.inliers},\"correspondences\":{pose.correspondences}," +
			                  $"\"rmsPx\":{F(pose.rmsPixels)},\"rejection\":{Quote(rejection)}}}}}");
		}

		private void OnSite(ConstraintSites.Site site)
		{
			sitesMeasured++;
			if (site.sigmaTranslation > 0.5f) yawOnlySites++;
			string verdict = "no truth";
			if (TryIdealCorrection(out Pose4 ideal))
			{
				Vector3 head = HeadInCanon();
				(float t, float y) = Error(site.correction, ideal, head);
				(float dt, float dy) = Error(Pose4.Identity, ideal, head);
				siteErrors.Add((t, y));
				verdict = $"off by {t * 100:F1} cm / {y:F2}° (uncorrected {dt * 100:F1} cm / {dy:F2}°)";
				writer?.WriteLine($"{{\"site\":{{\"id\":{site.id},\"measurements\":{site.measurements},\"crossLink\":{(site.crossLink ? "true" : "false")},\"error\":{F(t)},\"errorYaw\":{F(y)},\"uncorrectedError\":{F(dt)},\"uncorrectedErrorYaw\":{F(dy)},\"sigma\":{F(site.sigmaTranslation)}}}}}");
			}

			Note($"site {site.id} ({site.measurements} measurements) σ {site.sigmaTranslation * 100:F1} cm / {site.sigmaYaw * Mathf.Rad2Deg:F2}°: {verdict}");
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
				VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
				sb.Append($",\"drift\":{F(driftMeters)},\"driftYaw\":{F(driftYaw)},\"sitesMeasured\":{sitesMeasured},\"sites\":{(provider != null ? provider.Sites.Count : 0)}");
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
			              $"{sitesMeasured} site measurements ({yawOnlySites} yaw-only); final misalignment {misalignment * 100:F1} cm / {yaw:F2}° " +
			              $"(mean {(samples > 0 ? sumMisalignment / samples * 100 : 0):F1} cm, max {maxMisalignment * 100:F1} cm) against {driftMeters * 100:F0} cm of raw drift";
			if (siteErrors.Count > 0)
			{
				float sumT = 0, sumY = 0, maxT = 0;
				foreach ((float t, float y) in siteErrors) { sumT += t; sumY += y; maxT = Mathf.Max(maxT, t); }
				LastSummary += $"; sites off by {sumT / siteErrors.Count * 100:F1} cm / {sumY / siteErrors.Count:F2}° on average (max {maxT * 100:F1} cm)";
			}

			if (measurementErrors.Count > 0)
			{
				int good = 0, goodRejected = 0, acceptedCount = 0, badAccepted = 0;
				foreach ((float t, float y, bool accepted) in measurementErrors)
				{
					bool right = t < 0.15f && y < 1f;
					if (right) good++;
					if (right && !accepted) goodRejected++;
					if (accepted) acceptedCount++;
					if (accepted && !right) badAccepted++;
				}

				LastSummary += $"; {measurementErrors.Count} drift measurements, {good} within 15 cm / 1° of the room ({goodRejected} of those turned down), {acceptedCount} passed the gates ({badAccepted} of those off the room)";
			}

			if (mapRelativeErrors.Count > 0)
			{
				int good = 0, acceptedCount = 0, badAccepted = 0;
				float sumT = 0, sumY = 0;
				foreach ((float t, float y, bool accepted) in mapRelativeErrors)
				{
					bool right = t < 0.15f && y < 1f;
					if (right) good++;
					if (!accepted) continue;
					acceptedCount++;
					sumT += t;
					sumY += y;
					if (!right) badAccepted++;
				}

				LastSummary += $"; against the map they matched: {good} of {mapRelativeErrors.Count} within 15 cm / 1°, accepted ones off by " +
				               $"{(acceptedCount > 0 ? sumT / acceptedCount * 100 : 0):F1} cm / {(acceptedCount > 0 ? sumY / acceptedCount : 0):F2}° on average ({badAccepted} of {acceptedCount} wrong)";
			}

			if (VisualLocalizer.Instance != null) LastSummary += $"; mapping: {VisualLocalizer.Instance.Mapping}";
			if (depths) LastSummary += $"; {depths.Summary()}; {depths.Calibration()}";
			if (timing) LastSummary += $"; {timing.Summary}";
			Note(LastSummary);

			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			if (provider != null)
			{
				provider.MeasurementSolved -= OnMeasured;
				provider.SiteMeasured -= OnSite;
			}

			if (VisualLocalizer.Instance != null) VisualLocalizer.Instance.KeyframeAdded -= OnKeyframe;
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
