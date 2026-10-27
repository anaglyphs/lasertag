using System;
using System.IO;
using System.Text;
using Anaglyph.Netcode;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.VisualLocalization;
using Unity.Collections;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Saves what alignment works from, for diagnosing and tuning it offline: the live
	/// environment map (an .envscan the registration bench can read); the visual
	/// localizer's map and the frames behind it (visual/: images, poses,
	/// intrinsics, features, landmark tracks, what became of every recent query, and the
	/// settings they ran under); the constraint sites drift is corrected from (sites.json);
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

				StringBuilder info = new();
				info.AppendLine($"captured {DateTime.Now:u}, build {NetcodeManagement.GameVersion}, device {SystemInfo.deviceModel}");
				AppendState(info, CanonColocationProvider.Active);

				await Save(info, folder, "map", () => EnvScan.CaptureAsync(scanner, label: "live map"));

				await SaveVisual(info, folder);
				SaveSites(info, folder);

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

		// the constraint sites (where drift was measured, the correction each needed, how
		// much each counts at the head now) and the correction the rig is following
		private static void SaveSites(StringBuilder info, string folder)
		{
			try
			{
				VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
				if (provider == null)
				{
					info.AppendLine("sites: no visual provider");
					return;
				}

				ConstraintSites sites = provider.Sites;
				TrackAnchoring track = provider.Track;
				Vector3 head = XR.MainXRRig.Instance != null ? XR.MainXRRig.Camera.transform.position : Vector3.zero;
				Json j = new();
				j.BeginObject();
				j.Field("correction", provider.SiteCorrection.AsPose);
				j.Field("path", track.Path);
				j.Field("segment", track.Current);
				j.Field("head", head);
				j.Key("sites");
				j.BeginArray();
				foreach (ConstraintSites.Site site in sites.Sites)
				{
					j.BeginObject();
					j.Field("id", site.id);
					j.Field("position", site.position);
					j.Field("correction", site.correction.AsPose);
					j.Field("sigmaTranslation", site.sigmaTranslation);
					j.Field("sigmaYaw", site.sigmaYaw);
					j.Field("path", site.path);
					j.Field("time", site.time);
					j.Field("crossLink", site.crossLink);
					j.Field("measurements", site.measurements);
					j.EndObject();
				}

				j.EndArray();
				j.EndObject();
				File.WriteAllText(Path.Combine(folder, "sites.json"), j.ToString());
				info.AppendLine($"constraint sites: {sites.Count}, correction {provider.SiteCorrection}, walked {track.Path:F0} m");
			}
			catch (Exception e)
			{
				info.AppendLine($"pose graph: FAILED {e.Message}");
				Debug.LogException(e);
			}
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
			info.AppendLine($"canon space pose {provider.CanonSpacePose.position} {provider.CanonSpacePose.rotation.eulerAngles}, site correction {provider.SiteCorrection}");
			info.AppendLine($"last attempt {provider.LastAttemptDescription}");
		}
	}
}
