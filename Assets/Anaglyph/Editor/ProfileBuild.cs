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
	/// Batch-mode build of any build profile, for the scripts in Tools:
	/// <c>-executeMethod Anaglyph.Editor.ProfileBuild.BuildFromCommandLine -buildProfilePath &lt;asset&gt; -buildOutputPath &lt;path&gt;
	/// [-buildNumber N] [-developmentBuild]</c>.
	/// Android signs with the keystore passwords in RELEASE_KEYSTORE_PASS and RELEASE_KEYALIAS_PASS.
	/// </summary>
	public static class ProfileBuild
	{
		/// <summary>Exit code asking for a relaunch: the profile was just activated, so its defines aren't compiled in yet.</summary>
		public const int ProfileActivatedExitCode = 2;

		public static void BuildFromCommandLine()
		{
			BuildReport report = null;
			try
			{
				string profilePath = CommandLineValue("-buildProfilePath");
				if (ActivateProfileUnlessActive(profilePath))
				{
					Debug.Log($"[{nameof(ProfileBuild)}] activated {profilePath}; relaunch to build with its defines");
					EditorApplication.Exit(ProfileActivatedExitCode);
					return;
				}

				string buildNumber = OptionalCommandLineValue("-buildNumber");
				report = Build(
					profilePath,
					CommandLineValue("-buildOutputPath"),
					buildNumber == null ? null : int.Parse(buildNumber),
					HasCommandLineFlag("-developmentBuild"));
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}

			Debug.Log($"[{nameof(ProfileBuild)}] {(report == null ? "not built" : $"{report.summary.result}, {report.summary.totalErrors} errors at {report.summary.outputPath}")}");
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

		/// <param name="buildNumber">null keeps the profile's own number</param>
		public static BuildReport Build(string profilePath, string outputPath, int? buildNumber, bool development)
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
				options = development ? BuildOptions.Development : BuildOptions.None,
			});
		}

		private static void SetBuildNumberAndSigning(BuildTarget target, int? buildNumber)
		{
			switch (target)
			{
				case BuildTarget.Android:
					if (buildNumber.HasValue) PlayerSettings.Android.bundleVersionCode = buildNumber.Value;
					if (!PlayerSettings.Android.useCustomKeystore) break;
					PlayerSettings.Android.keystorePass = RequiredEnvironmentVariable("RELEASE_KEYSTORE_PASS");
					PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("RELEASE_KEYALIAS_PASS") is { Length: > 0 } aliasPass ? aliasPass : PlayerSettings.Android.keystorePass;
					break;
				case BuildTarget.iOS:
					if (buildNumber.HasValue) PlayerSettings.iOS.buildNumber = buildNumber.Value.ToString();
					break;
				// desktop players have no build number of their own; BuildProcess stamps Android's into the game
				case BuildTarget.StandaloneWindows64:
					if (buildNumber.HasValue) PlayerSettings.Android.bundleVersionCode = buildNumber.Value;
					break;
				default:
					if (buildNumber.HasValue) throw new NotSupportedException($"{nameof(ProfileBuild)} doesn't number {target} builds yet");
					break;
			}
		}

		private static string OptionalCommandLineValue(string name)
		{
			string[] args = Environment.GetCommandLineArgs();
			int index = Array.IndexOf(args, name);
			return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
		}

		private static string CommandLineValue(string name) =>
			OptionalCommandLineValue(name) ?? throw new ArgumentException($"missing {name}");

		private static bool HasCommandLineFlag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

		private static string RequiredEnvironmentVariable(string name) =>
			Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new ArgumentException($"{name} is not set");
	}
}
#endif
