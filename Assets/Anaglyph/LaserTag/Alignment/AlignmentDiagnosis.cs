using System;
using System.IO;
using System.Text;
using Anaglyph.Netcode;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning;
using Unity.Collections;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Saves what alignment works from, for diagnosing it offline with
	/// <see cref="Anaglyph.XR.DepthKit.EnvScanning.Registration.ScanRegistrationBench"/>:
	/// the live map, the relocalization scratch volume (while relocalizing), the
	/// authority's snapshot a joiner registers against (while searching), and a text
	/// summary of the provider's state. One folder per capture under
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

				ScanRegistrationColocationConstraintProvider provider = ScanRegistrationColocationConstraintProvider.Instance;
				StringBuilder info = new();
				info.AppendLine($"captured {DateTime.Now:u}, build {NetcodeManagement.GameVersion}, device {SystemInfo.deviceModel}");
				AppendState(info, provider);

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

		private static void AppendState(StringBuilder info, ScanRegistrationColocationConstraintProvider provider)
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
				info.AppendLine("provider: none");
				return;
			}

			info.AppendLine($"provider phase {provider.CurrentPhase}, alignment generation {provider.AlignmentGeneration}");
			info.AppendLine($"canon space pose {provider.CanonSpacePose.position} {provider.CanonSpacePose.rotation.eulerAngles}, slam correction {provider.SlamCorrection}");
			info.AppendLine($"last result {provider.LastResult}");
		}
	}
}
