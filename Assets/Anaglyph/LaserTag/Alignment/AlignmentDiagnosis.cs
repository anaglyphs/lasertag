using System;
using System.IO;
using System.Text;
using Anaglyph.Netcode;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.DepthKit.EnvScanning.Slam;
using Anaglyph.XR.VisualLocalization;
using Unity.Collections;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Saves what alignment works from, for diagnosing and tuning it offline: the live
	/// map, the relocalization scratch volume (while relocalizing) and the authority's
	/// snapshot a joiner registers against (while searching), for
	/// <see cref="Anaglyph.XR.DepthKit.EnvScanning.Registration.ScanRegistrationBench"/>;
	/// the visual localizer's map and the frames behind it (visual/: images, poses,
	/// intrinsics, features, landmark tracks, what became of every recent query, and the
	/// settings they ran under); the pose graph the visual map hangs off (graph.json);
	/// and a text summary of the active provider's state. One folder per capture under
	/// <see cref="EnvScan.DefaultDirectory"/>/diagnosis — on a Quest that is
	/// /sdcard/Android/data/&lt;package&gt;/files/EnvScans/diagnosis
	/// </summary>
	public static class AlignmentDiagnosis
	{
		public static bool InProgress { get; private set; }

		/// <summary>Captures everything available; returns the folder written, or throws.</summary>
		public static async Awaitable<string> CaptureAsync()
		{
			if (InProgress) throw new InvalidOperationException("a capture is already in progress");
			EnvScanner scanner = EnvScanner.Instance;
			if (scanner == null) throw new InvalidOperationException("no environment scanner");

			InProgress = true;
			try
			{
				string folder = Path.Combine(EnvScan.DefaultDirectory, "diagnosis", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
				Directory.CreateDirectory(folder);

				// the scratch and reference volumes are scan registration's; the state is whichever provider is active
				ScanRegistrationColocationConstraintProvider provider = ScanRegistrationColocationConstraintProvider.Instance;
				StringBuilder info = new();
				info.AppendLine($"captured {DateTime.Now:u}, build {NetcodeManagement.GameVersion}, device {SystemInfo.deviceModel}");
				AppendState(info, CanonColocationProvider.Active);

				// the scratch volume first: a relocalization that succeeds meanwhile disposes it
				EnvVolume scratch = provider != null ? provider.ScratchVolume : null;
				if (scratch != null && scratch.IsCreated)
					await Save(info, folder, "scratch", () => EnvScan.CaptureAsync(scanner, scratch, "relocalization scratch"));
				else
					info.AppendLine("scratch: none (not relocalizing)");

				await Save(info, folder, "map", () => EnvScan.CaptureAsync(scanner, label: "live map"));

				if (provider != null && provider.ReferenceChunks.Count > 0)
					await Save(info, folder, "reference", () => FromReference(scanner, provider));
				else
					info.AppendLine("reference: none (not searching, or no snapshot received)");

				await SaveVisual(info, folder);
				SaveGraph(info, folder);

				File.WriteAllText(Path.Combine(folder, "info.txt"), info.ToString());
				Debug.Log($"[{nameof(AlignmentDiagnosis)}] saved to {folder}\n{info}");
				return folder;
			}
			finally
			{
				InProgress = false;
			}
		}

		// one file's failure is noted in the summary rather than losing the others
		private static async Awaitable Save(StringBuilder info, string folder, string name, Func<Awaitable<EnvScan>> capture)
		{
			try
			{
				using EnvScan scan = await capture();
				scan.Save(Path.Combine(folder, name + EnvScan.Extension));
				info.AppendLine($"{name}: {scan}");
			}
			catch (Exception e)
			{
				info.AppendLine($"{name}: FAILED {e.Message}");
				Debug.LogException(e);
			}
		}

		private static async Awaitable SaveVisual(StringBuilder info, string folder)
		{
			VisualLocalizer localizer = VisualLocalizer.Instance;
			if (localizer == null)
			{
				info.AppendLine("visual: no localizer");
				return;
			}

			try
			{
				string visual = Path.Combine(folder, "visual");
				await localizer.ExportAsync(visual);
				info.AppendLine($"visual: {localizer.Quality}, {localizer.Frames.Count} frames logged{(localizer.IsAvailable ? "" : " (localizer unavailable: no camera or models)")}");

				VisualColocationConstraintProvider colocation = VisualColocationConstraintProvider.Instance;
				if (colocation != null)
				{
					File.WriteAllLines(Path.Combine(visual, "outcomes.txt"), colocation.RecentOutcomes);
					File.WriteAllText(Path.Combine(visual, "colocation-settings.json"), JsonUtility.ToJson(colocation, true));
					info.AppendLine($"visual last outcome: {colocation.LastOutcome ?? "none"}");
				}
			}
			catch (Exception e)
			{
				info.AppendLine($"visual: FAILED {e.Message}");
				Debug.LogException(e);
			}
		}

		// nodes (odometry and world poses, trajectory links) and edges, so node-relative
		// keyframe poses can be placed and closures inspected
		private static void SaveGraph(StringBuilder info, string folder)
		{
			try
			{
				PoseGraph graph = PoseGraph.Shared;
				Json j = new();
				j.BeginObject();
				j.Field("version", graph.Version);
				j.Field("driftPerMeter", graph.DriftPerMeter);
				j.Field("loopDrift", graph.LoopDrift);
				SharedSlam slam = SharedSlam.Instance;
				j.Field("activeNode", slam != null ? slam.ActiveNode : 0);
				j.Field("tracking", slam != null && slam.IsTracking);

				j.Key("nodes");
				j.BeginArray();
				foreach (PoseNode n in graph.Nodes)
				{
					j.BeginObject();
					j.Field("key", n.key);
					j.Field("previous", n.previous);
					j.Field("segmentStart", n.segmentStart);
					j.Field("hasPrior", n.hasPrior);
					j.Field("odometry", n.odometry.AsPose);
					j.Field("world", n.world.AsPose);
					j.EndObject();
				}

				j.EndArray();
				j.Key("edges");
				j.BeginArray();
				foreach (PoseEdge e in graph.Edges)
				{
					j.BeginObject();
					j.Field("id", e.id);
					j.Field("kind", e.kind.ToString());
					j.Field("from", e.from);
					j.Field("to", e.to);
					j.Field("measurement", e.measurement.AsPose);
					j.Field("sigmaTranslation", e.sigmaTranslation);
					j.Field("sigmaYaw", e.sigmaYaw);
					j.Field("rejected", e.rejected);
					j.EndObject();
				}

				j.EndArray();
				j.EndObject();
				File.WriteAllText(Path.Combine(folder, "graph.json"), j.ToString());
				info.AppendLine($"pose graph: {graph.NodeCount} nodes, {graph.EdgeCount} edges, v{graph.Version}{(slam != null ? $", tracking {slam.IsTracking}" : "")}");
			}
			catch (Exception e)
			{
				info.AppendLine($"pose graph: FAILED {e.Message}");
				Debug.LogException(e);
			}
		}

		// the snapshot is in the authority's frame, whose tracking space this headset never knew
		private static Awaitable<EnvScan> FromReference(EnvScanner scanner, ScanRegistrationColocationConstraintProvider provider)
		{
			EnvScan scan = EnvScan.NewFor(scanner);
			scan.Label = "authority snapshot (authority frame)";
			foreach ((int index, NativeArray<EnvScanner.Voxel> voxels) in provider.ReferenceChunks)
				scan.Chunks[index] = new NativeArray<EnvScanner.Voxel>(voxels, Allocator.Persistent);

			AwaitableCompletionSource<EnvScan> done = new();
			done.SetResult(scan);
			return done.Awaitable;
		}

		private static void AppendState(StringBuilder info, CanonColocationProvider provider)
		{
			if (MainXRRig.Instance)
			{
				Transform space = MainXRRig.TrackingSpace;
				info.AppendLine($"tracking space {space.position} {space.rotation.eulerAngles}");
				if (MainXRRig.Camera) info.AppendLine($"head {MainXRRig.Camera.transform.position} {MainXRRig.Camera.transform.rotation.eulerAngles}");
			}

			if (ColocationManager.Instance != null)
				info.AppendLine($"colocation state {ColocationManager.Instance.AlignmentState}");

			if (provider == null)
			{
				info.AppendLine("provider: none matching against a map");
				return;
			}

			info.AppendLine($"provider {provider.GetType().Name}, phase {provider.CurrentPhase}, alignment generation {provider.AlignmentGeneration}");
			info.AppendLine($"canon space pose {provider.CanonSpacePose.position} {provider.CanonSpacePose.rotation.eulerAngles}, slam correction {provider.SlamCorrection}");
			info.AppendLine($"last attempt {provider.LastAttemptDescription}");
		}
	}
}
