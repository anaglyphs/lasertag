#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Test of relocalization: jumps the tracking space to a random pose, as a recenter or
	/// tracking loss would (the simulated room follows the tracking space, so the head keeps
	/// seeing the same view while every map in world coordinates is now off by the jump),
	/// raises the recenter, and lets the real path run. Ground truth is the jump itself: the
	/// alignment found should put the tracking space back where it was. Reports the error,
	/// how long it took, and what the visual localizer said.
	///
	/// <see cref="Launch"/> runs it from edit mode, in and out of play mode. A trial that
	/// fails is undone, so the next starts from where the failed one did, and every run ends
	/// in a summary counting the trials that failed and those that never ran. The more the
	/// map holds (a saved one, or walking around first), the more the trials mean.
	/// </summary>
	public static class RelocalizationTest
	{
		private const string Menu = "Lasertag/Relocalization Test/";
		private const float Timeout = 60f;
		// for each trial's start: outlasts a saved map's own attempt to relocalize when play mode starts
		private const float ReadyTimeout = 90f;

		private const string SummaryKey = "Anaglyph.RelocalizationTest.Summary";
		private const string PendingKey = "Anaglyph.RelocalizationTest.Pending";

		/// <summary>The summary of the last finished run, for whoever drives the test. Kept across play mode's domain reloads.</summary>
		public static string LastSummary
		{
			get => SessionState.GetString(SummaryKey, null);
			private set => SessionState.SetString(SummaryKey, value ?? "");
		}

		private class Trial
		{
			public int index;
			public Pose before;
			public Pose4 jump;
			public int generation;
			public double started;
		}

		private static readonly Queue<int> remaining = new();
		private static Trial trial;
		private static readonly List<(float translation, float yaw, float seconds, bool succeeded)> results = new();
		private static readonly System.Random random = new();
		private static int requested;
		private static double readySince;
		private static bool running;
		// set when Launch entered play mode for the run: it leaves play mode again when done
		private static bool exitPlayModeWhenDone;

		// unity-mcp can deliver a command again as play mode starts: the same Launch, or the
		// same menu item, can arrive here while its first delivery is still on its way
		private static bool Busy => running || (EditorApplication.isPlayingOrWillChangePlaymode && SessionState.GetInt(PendingKey, 0) != 0);

		[MenuItem(Menu + "Randomize Coordinate Space and Relocalize")]
		private static void RunOne() => Launch(1);

		[MenuItem(Menu + "Run 10 Trials")]
		private static void RunTen() => Launch(10);

		[MenuItem(Menu + "Cancel")]
		private static void Cancel() => End("cancelled");

		[MenuItem(Menu + "Randomize Coordinate Space and Relocalize", true)]
		[MenuItem(Menu + "Run 10 Trials", true)]
		private static bool CanRun() => !Busy && !EditorApplication.isCompiling;

		[MenuItem(Menu + "Cancel", true)]
		private static bool CanCancel() => running;

		private static VisualColocationConstraintProvider Provider => VisualColocationConstraintProvider.Active;

		/// <summary>
		/// Runs trials from edit mode or play mode. From edit mode it enters play mode, and
		/// leaves it when the run ends. Read <see cref="LastSummary"/> afterwards. Returns why
		/// not, or null.
		/// </summary>
		public static string Launch(int count)
		{
			if (Busy) return "a run is already under way";
			if (EditorApplication.isCompiling) return "compiling";
			LastSummary = "";
			if (Application.isPlaying)
			{
				Run(count);
				return null;
			}

			SessionState.SetInt(PendingKey, count);
			EditorApplication.isPlaying = true;
			return null;
		}

		[InitializeOnLoadMethod]
		private static void ListenForPlayMode() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

		private static void OnPlayModeChanged(PlayModeStateChange change)
		{
			if (change != PlayModeStateChange.EnteredPlayMode) return;
			int pending = SessionState.GetInt(PendingKey, 0);
			if (pending == 0) return;
			SessionState.EraseInt(PendingKey);
			exitPlayModeWhenDone = true;
			Run(pending);
		}

		private static void Run(int count)
		{
			results.Clear();
			remaining.Clear();
			requested = count;
			for (int i = 1; i <= count; i++) remaining.Enqueue(i);
			readySince = EditorApplication.timeSinceStartup;
			running = true;
			EditorApplication.update -= Tick;
			EditorApplication.update += Tick;
		}

		private static void Tick()
		{
			if (!Application.isPlaying)
			{
				End("play mode ended");
				return;
			}

			if (trial == null)
			{
				if (remaining.Count == 0)
				{
					End(null);
					return;
				}

				// a recenter and what follows it can take a while to settle, from one trial to the next
				string problem = WhyNotReady();
				if (problem == null) Begin(remaining.Dequeue());
				else if (EditorApplication.timeSinceStartup - readySince > ReadyTimeout) End($"still {problem} after {ReadyTimeout:F0} s");
				return;
			}

			VisualColocationConstraintProvider provider = Provider;
			double elapsed = EditorApplication.timeSinceStartup - trial.started;
			if (provider != null && provider.Alignment.CurrentPhase == CanonAlignment.Phase.Aligned &&
			    provider.Alignment.Generation != trial.generation)
			{
				Finish(true, null);
				return;
			}

			if (elapsed > Timeout) Finish(false, $"timed out after {Timeout:F0} s in phase {provider?.Alignment.CurrentPhase}");
		}

		private static string WhyNotReady()
		{
			VisualColocationConstraintProvider provider = Provider;
			if (provider == null) return "no map-matching provider (colocation method is not Visual)";
			if (provider.Alignment.CurrentPhase != CanonAlignment.Phase.Aligned) return $"provider is {provider.Alignment.CurrentPhase}, not Aligned";
			if (ColocationManager.Instance == null || !ColocationManager.IsColocated) return "not colocated yet";
			if (MainXRRig.Instance == null) return "no XR rig";
			VisualLocalizer localizer = VisualLocalizer.Instance;
			return localizer == null || localizer.Map.Count == 0 ? "the visual map is empty" : null;
		}

		private static void Begin(int index)
		{
			VisualColocationConstraintProvider provider = Provider;
			VisualLocalizer localizer = VisualLocalizer.Instance;
			Transform space = MainXRRig.TrackingSpace;
			Pose before = new(space.position, space.rotation);

			// a recenter turns the frame about the head and slides it: anything up to a full turn and a few meters
			float yaw = math.radians((float)(random.NextDouble() * 360 - 180));
			float3 slide = new((float)(random.NextDouble() * 6 - 3), (float)(random.NextDouble() * 0.2 - 0.1), (float)(random.NextDouble() * 6 - 3));
			float3 head = MainXRRig.Camera.transform.position;
			Pose4 jump = new Pose4(head + slide, yaw) * new Pose4(-head, 0);

			Pose after = jump.Apply(before);
			space.SetPositionAndRotation(after.position, after.rotation);

			trial = new Trial
			{
				index = index, before = before, jump = jump,
				generation = provider.Alignment.Generation, started = EditorApplication.timeSinceStartup,
			};

			MainXRRig.SimulateRecenter();
			Debug.Log($"[{nameof(RelocalizationTest)}] trial {index}: jumped the coordinate space by {jump} (map {localizer.Quality}); relocalizing");
		}

		private static void Finish(bool aligned, string failure)
		{
			if (trial == null) return;
			Trial t = trial;
			trial = null;
			readySince = EditorApplication.timeSinceStartup;
			float seconds = (float)(EditorApplication.timeSinceStartup - t.started);

			if (!aligned)
			{
				results.Add((float.NaN, float.NaN, seconds, false));
				Debug.LogWarning($"[{nameof(RelocalizationTest)}] trial {t.index} FAILED: {failure}. Last visual outcome: {VisualColocationConstraintProvider.Instance?.LastOutcome}");
				Undo(t);
				return;
			}

			// the alignment found, against the tracking space's pose before the jump (the canon
			// pose leaves out the SLAM correction, which a new alignment resets anyway): where
			// each puts the head, tracking-local, and how far they turn the frame apart
			Pose canon = Provider.Alignment.CanonSpacePose;
			Vector3 headLocal = MainXRRig.TrackingSpace.InverseTransformPoint(MainXRRig.Camera.transform.position);
			float headError = Vector3.Distance(t.before.position + t.before.rotation * headLocal, canon.position + canon.rotation * headLocal);
			float yawError = math.degrees(math.abs(Pose4.WrapAngle(Pose4.TwistYaw(canon.rotation) - Pose4.TwistYaw(t.before.rotation))));
			results.Add((headError, yawError, seconds, true));

			VisualLocalizationResult last = VisualColocationConstraintProvider.Instance != null ? VisualColocationConstraintProvider.Instance.LastResult : default;
			Debug.Log($"[{nameof(RelocalizationTest)}] trial {t.index}: relocalized in {seconds:F1} s, error at the head {headError * 100:F1} cm, yaw {yawError:F2}° " +
			          $"(jump {t.jump}); last solve: {last}");
		}

		// back where the trial jumped from, and aligned there: left relocalizing, the provider
		// would keep at it and no further trial could start
		private static void Undo(Trial t)
		{
			if (!Application.isPlaying || MainXRRig.Instance == null) return;
			MainXRRig.TrackingSpace.SetPositionAndRotation(t.before.position, t.before.rotation);
			VisualColocationConstraintProvider provider = Provider;
			if (provider != null) provider.Alignment.AbandonRelocalization();
		}

		private static void End(string why)
		{
			EditorApplication.update -= Tick;
			running = false;
			Finish(false, why);
			int notRun = remaining.Count;
			remaining.Clear();
			Summarize(why, notRun);

			if (exitPlayModeWhenDone)
			{
				exitPlayModeWhenDone = false;
				EditorApplication.isPlaying = false;
			}
		}

		private static void Summarize(string why, int notRun)
		{
			StringBuilder sb = new();
			int ok = 0;
			float sumT = 0, sumYaw = 0, maxT = 0, maxYaw = 0, sumSeconds = 0;
			foreach ((float translation, float yaw, float seconds, bool succeeded) in results)
			{
				sumSeconds += seconds;
				if (!succeeded) continue;
				ok++;
				sumT += translation;
				sumYaw += yaw;
				maxT = math.max(maxT, translation);
				maxYaw = math.max(maxYaw, yaw);
			}

			sb.Append($"{ok}/{requested} relocalized, {results.Count - ok} failed, {notRun} not run");
			if (why != null) sb.Append($" ({why})");
			if (results.Count > 0) sb.Append($"; mean {sumSeconds / results.Count:F1} s");
			if (ok > 0) sb.Append($"; head error mean {sumT / ok * 100:F1} cm (max {maxT * 100:F1}), yaw mean {sumYaw / ok:F2}° (max {maxYaw:F2}°)");
			LastSummary = sb.ToString();
			Debug.Log($"[{nameof(RelocalizationTest)}] {LastSummary}");
		}
	}
}
#endif
