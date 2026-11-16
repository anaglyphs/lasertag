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
		public static void BuildFromCommandLine()
		{
			BuildReport report = null;
			try
			{
				report = Build(
					CommandLineValue("-releaseProfile"),
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

		public static BuildReport Build(string profilePath, string outputPath, int buildNumber)
		{
			BuildProfile profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);
			if (profile == null) throw new ArgumentException($"no build profile at {profilePath}");
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
