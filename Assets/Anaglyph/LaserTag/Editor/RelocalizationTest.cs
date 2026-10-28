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
	/// Play-mode test of relocalization: jumps the tracking space to a random pose, as a
	/// recenter or tracking loss would (the simulated room follows the tracking space, so
	/// the head keeps seeing the same view while every map in world coordinates is now
	/// off by the jump), raises the recenter, and lets the real path run. Ground truth is
	/// the jump itself: the alignment found should put the tracking space back where it
	/// was. Reports the error, how long it took, and what the visual localizer said.
	///
	/// Map first: walk around until the visual map has keyframes and ranged landmarks.
	/// </summary>
	public static class RelocalizationTest
	{
		private const string Menu = "Lasertag/Relocalization Test/";
		private const float Timeout = 60f;

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

		[MenuItem(Menu + "Randomize Coordinate Space and Relocalize")]
		private static void RunOne() => Run(1);

		[MenuItem(Menu + "Run 10 Trials")]
		private static void RunTen() => Run(10);

		[MenuItem(Menu + "Cancel")]
		private static void Cancel()
		{
			remaining.Clear();
			Finish(false, "cancelled");
		}

		[MenuItem(Menu + "Randomize Coordinate Space and Relocalize", true)]
		[MenuItem(Menu + "Run 10 Trials", true)]
		private static bool CanRun() => Application.isPlaying && trial == null;

		[MenuItem(Menu + "Cancel", true)]
		private static bool CanCancel() => trial != null;

		private static VisualColocationConstraintProvider Provider => VisualColocationConstraintProvider.Active;

		private static void Run(int count)
		{
			results.Clear();
			remaining.Clear();
			for (int i = 1; i <= count; i++) remaining.Enqueue(i);
			EditorApplication.update -= Tick;
			EditorApplication.update += Tick;
		}

		private static void Tick()
		{
			if (!Application.isPlaying)
			{
				EditorApplication.update -= Tick;
				trial = null;
				remaining.Clear();
				return;
			}

			if (trial == null)
			{
				if (remaining.Count == 0)
				{
					EditorApplication.update -= Tick;
					Summarize();
					return;
				}

				Begin(remaining.Peek());
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

		private static void Begin(int index)
		{
			VisualColocationConstraintProvider provider = Provider;
			string problem = provider == null ? "no map-matching provider (colocation method is not Visual)"
				: provider.Alignment.CurrentPhase != CanonAlignment.Phase.Aligned ? $"provider is {provider.Alignment.CurrentPhase}, not Aligned"
				: ColocationManager.Instance == null || !ColocationManager.IsColocated ? "not colocated yet"
				: MainXRRig.Instance == null ? "no XR rig"
				: null;

			VisualLocalizer localizer = VisualLocalizer.Instance;
			if (problem == null && (localizer == null || localizer.Map.Count == 0)) problem = "the visual map is empty: walk around first";
			if (problem != null)
			{
				Debug.LogWarning($"[{nameof(RelocalizationTest)}] can't start: {problem}");
				remaining.Clear();
				EditorApplication.update -= Tick;
				return;
			}

			remaining.Dequeue();
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
			float seconds = (float)(EditorApplication.timeSinceStartup - t.started);

			if (!aligned)
			{
				results.Add((float.NaN, float.NaN, seconds, false));
				Debug.LogWarning($"[{nameof(RelocalizationTest)}] trial {t.index} FAILED: {failure}. Last visual outcome: {VisualColocationConstraintProvider.Instance?.LastOutcome}");
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

		private static void Summarize()
		{
			if (results.Count == 0) return;
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

			sb.Append($"[{nameof(RelocalizationTest)}] {ok}/{results.Count} relocalized, mean {sumSeconds / results.Count:F1} s");
			if (ok > 0) sb.Append($"; head error mean {sumT / ok * 100:F1} cm (max {maxT * 100:F1}), yaw mean {sumYaw / ok:F2}° (max {maxYaw:F2}°)");
			Debug.Log(sb.ToString());
		}
	}
}
#endif
