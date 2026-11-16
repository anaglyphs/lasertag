#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Anaglyph.Editor
{
	/// <summary>
	/// Batch-mode store build of any build profile, for Tools/release:
	/// <c>-executeMethod Anaglyph.Editor.ReleaseBuild.BuildFromCommandLine -releaseProfile &lt;asset&gt; -releaseOutput &lt;path&gt; -releaseBuildNumber N</c>.
	/// Android signs with the keystore passwords in RELEASE_KEYSTORE_PASS and RELEASE_KEYALIAS_PASS.
	/// </summary>
	public static class ReleaseBuild
	{
		/// <summary>Exit code asking for a relaunch: the profile was just activated, so its defines aren't compiled in yet.</summary>
		public const int ProfileActivatedExitCode = 2;

		public static void BuildFromCommandLine()
		{
			BuildReport report = null;
			try
			{
				string profilePath = CommandLineValue("-releaseProfile");
				if (ActivateProfileUnlessActive(profilePath))
				{
					Debug.Log($"[{nameof(ReleaseBuild)}] activated {profilePath}; relaunch to build with its defines");
					EditorApplication.Exit(ProfileActivatedExitCode);
					return;
				}

				report = Build(
					profilePath,
					CommandLineValue("-releaseOutput"),
					int.Parse(CommandLineValue("-releaseBuildNumber")));
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}

			Debug.Log($"[{nameof(ReleaseBuild)}] {(report == null ? "not built" : $"{report.summary.result}, {report.summary.totalErrors} errors at {report.summary.outputPath}")}");
			EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
		}

		/// <summary>
		/// Editor code compiles with the active profile's defines, and some build processors (ARKit's) act
		/// only on those, so a newly activated profile needs a relaunch before it builds correctly.
		/// </summary>
		private static bool ActivateProfileUnlessActive(string profilePath)
		{
			BuildProfile profile = LoadProfile(profilePath);
			if (BuildProfile.GetActiveBuildProfile() == profile) return false;
			BuildProfile.SetActiveBuildProfile(profile);
			AssetDatabase.SaveAssets();
			return true;
		}

		private static BuildProfile LoadProfile(string profilePath) =>
			AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath) ?? throw new ArgumentException($"no build profile at {profilePath}");

		public static BuildReport Build(string profilePath, string outputPath, int buildNumber)
		{
			BuildProfile profile = LoadProfile(profilePath);
			BuildProfile.SetActiveBuildProfile(profile);
			SetBuildNumberAndSigning(EditorUserBuildSettings.activeBuildTarget, buildNumber);
			AssetDatabase.SaveAssets();

			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
			return BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
			{
				buildProfile = profile,
				locationPathName = outputPath,
				options = BuildOptions.None,
			});
		}

		private static void SetBuildNumberAndSigning(BuildTarget target, int buildNumber)
		{
			switch (target)
			{
				case BuildTarget.Android:
					PlayerSettings.Android.bundleVersionCode = buildNumber;
					if (!PlayerSettings.Android.useCustomKeystore) break;
					PlayerSettings.Android.keystorePass = RequiredEnvironmentVariable("RELEASE_KEYSTORE_PASS");
					PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("RELEASE_KEYALIAS_PASS") is { Length: > 0 } aliasPass ? aliasPass : PlayerSettings.Android.keystorePass;
					break;
				case BuildTarget.iOS:
					PlayerSettings.iOS.buildNumber = buildNumber.ToString();
					break;
				default:
					throw new NotSupportedException($"{nameof(ReleaseBuild)} doesn't number {target} builds yet");
			}
		}

		private static string CommandLineValue(string name)
		{
			string[] args = Environment.GetCommandLineArgs();
			int index = Array.IndexOf(args, name);
			if (index < 0 || index + 1 >= args.Length) throw new ArgumentException($"missing {name}");
			return args[index + 1];
		}

		private static string RequiredEnvironmentVariable(string name) =>
			Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new ArgumentException($"{name} is not set");
	}
}
#endif
