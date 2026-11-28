using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Builds a non-Quest Android headset APK from its own build profile. All Android headsets share the
	/// Android target and its OpenXR feature set, which build profiles don't carry, so the build turns Meta's
	/// features off and the headset's on, uses the headset's manifest, and restores Quest's afterwards.
	/// </summary>
	public static class OpenXRVariantBuild
	{
		/// <summary>Exit code asking for a relaunch: the profile was just activated, so its defines aren't compiled in yet.</summary>
		public const int ProfileActivatedExitCode = 2;

		private const string ManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
		private const string TemplateProfilePath = "Assets/Settings/Build Profiles/Release.asset";

		private static readonly string[] MetaOnlyAssemblies =
		{
			"Unity.XR.MetaOpenXR",
			"Oculus.VR",
			"Unity.XR.OpenXR.Features.MetaQuestSupport",
		};

		private static readonly string[] MetaOnlyFeatures =
		{
			"Anaglyph.XR.PassthroughStylingFeature",
		};

		/// <summary>Name of the profile being built here, or null.</summary>
		public static string ProfileInProgress { get; private set; }

		public class Variant
		{
			public string ProfileName;
			public string ProfilePath;
			public string AndroidAppId;
			public string ManifestPath;
			public string DefaultOutputPath;
			/// <summary>Full type names of the OpenXR features this headset needs on.</summary>
			public string[] EnabledFeatures;
			/// <summary>Turns off a feature Quest has on, beyond Meta's own.</summary>
			public Func<OpenXRFeature, bool> AlsoDisable = _ => false;
			/// <summary>Headset-specific settings, applied after the features are switched.</summary>
			public Action<OpenXRFeature[]> Prepare = _ => { };
		}

		public static bool IsMetaOnly(OpenXRFeature feature) =>
			MetaOnlyAssemblies.Contains(feature.GetType().Assembly.GetName().Name) ||
			MetaOnlyFeatures.Contains(feature.GetType().FullName);

		public static string Describe(BuildReport report) => report == null
			? "not built: see the console"
			: $"{report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / 1e6:F0} MB at {report.summary.outputPath}";

		public static void BuildFromMenu(Variant variant)
		{
			BuildReport report = Build(variant, variant.DefaultOutputPath);
			EditorUtility.DisplayDialog($"{variant.ProfileName} build", Describe(report), "OK");
		}

		/// <summary>
		/// For <c>-executeMethod</c> in batch mode, with optional <c>-buildOutputPath &lt;apk&gt;</c> and <c>-developmentBuild</c>.
		/// Exits 0 when the APK was built, or <see cref="ProfileActivatedExitCode"/> when it activated the profile and needs a relaunch.
		/// </summary>
		public static void BuildFromCommandLine(Variant variant)
		{
			BuildProfile profile = EnsureProfile(variant);
			if (BuildProfile.GetActiveBuildProfile() != profile)
			{
				BuildProfile.SetActiveBuildProfile(profile);
				AssetDatabase.SaveAssets();
				Debug.Log($"[{nameof(OpenXRVariantBuild)}] activated {variant.ProfilePath}; relaunch to build with its defines");
				EditorApplication.Exit(ProfileActivatedExitCode);
				return;
			}

			string[] args = Environment.GetCommandLineArgs();
			int outputIndex = Array.IndexOf(args, "-buildOutputPath");
			string outputPath = outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] : variant.DefaultOutputPath;
			BuildReport report = Build(variant, outputPath, development: Array.IndexOf(args, "-developmentBuild") >= 0);
			Debug.Log($"[{nameof(OpenXRVariantBuild)}] {Describe(report)}");
			EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
		}

		public static BuildReport Build(Variant variant, string outputPath, bool development = false)
		{
			BuildProfile profile = EnsureProfile(variant);
			OpenXRSettings openXR = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
			if (openXR == null)
			{
				Debug.LogError($"[{nameof(OpenXRVariantBuild)}] no OpenXR settings for Android");
				return null;
			}

			OpenXRFeature[] features = openXR.GetFeatures<OpenXRFeature>();
			string[] missing = variant.EnabledFeatures.Where(name => features.All(feature => feature.GetType().FullName != name)).ToArray();
			if (missing.Length > 0)
			{
				Debug.LogError($"[{nameof(OpenXRVariantBuild)}] {variant.ProfileName} OpenXR features missing ({string.Join(", ", missing)}): is its package installed?");
				return null;
			}

			Dictionary<OpenXRFeature, bool> questFeatureStates = features.ToDictionary(feature => feature, feature => feature.enabled);
			byte[] questManifest = File.ReadAllBytes(ManifestPath);
			try
			{
				File.Copy(variant.ManifestPath, ManifestPath, true);
				AssetDatabase.ImportAsset(ManifestPath);
				ProfileInProgress = variant.ProfileName;

				foreach (OpenXRFeature feature in features)
				{
					if (IsMetaOnly(feature) || variant.AlsoDisable(feature)) feature.enabled = false;
					if (variant.EnabledFeatures.Contains(feature.GetType().FullName)) feature.enabled = true;
				}
				variant.Prepare(features);
				SetMetaOpenXRLoaderIncluded(false);
				AssetDatabase.SaveAssets();

				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
				return BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
				{
					buildProfile = profile,
					locationPathName = outputPath,
					options = development ? BuildOptions.Development : BuildOptions.None,
				});
			}
			finally
			{
				ProfileInProgress = null;
				SetMetaOpenXRLoaderIncluded(true);
				File.WriteAllBytes(ManifestPath, questManifest);
				AssetDatabase.ImportAsset(ManifestPath);
				foreach ((OpenXRFeature feature, bool enabled) in questFeatureStates)
					if (feature != null) feature.enabled = enabled;
				EditorUtility.SetDirty(openXR);
				AssetDatabase.SaveAssets();
			}
		}

		// OVRPlugin.aar carries its own OpenXR loader, which collides with other headsets'; Meta's build step
		// only decides on it while its own feature is enabled
		private static void SetMetaOpenXRLoaderIncluded(bool included)
		{
			foreach (PluginImporter plugin in PluginImporter.GetAllImporters())
				if (Path.GetFileName(plugin.assetPath).StartsWith("OVRPlugin", StringComparison.Ordinal))
					plugin.SetIncludeInBuildDelegate(included ? null : _ => false);
		}

		// a copy of the Release profile, under its own name and app id, signed with the debug key
		public static BuildProfile EnsureProfile(Variant variant)
		{
			if (!File.Exists(variant.ProfilePath))
			{
				if (!AssetDatabase.CopyAsset(TemplateProfilePath, variant.ProfilePath)) throw new InvalidOperationException($"couldn't copy {TemplateProfilePath}");
				string yaml = File.ReadAllText(variant.ProfilePath)
					.Replace("m_Name: Release", $"m_Name: {variant.ProfileName}")
					.Replace("Android: com.trev3d.lasertag'", $"Android: {variant.AndroidAppId}'")
					.Replace("androidUseCustomKeystore: 1", "androidUseCustomKeystore: 0");
				File.WriteAllText(variant.ProfilePath, yaml);
				AssetDatabase.ImportAsset(variant.ProfilePath, ImportAssetOptions.ForceUpdate);
			}

			return AssetDatabase.LoadAssetAtPath<BuildProfile>(variant.ProfilePath);
		}

		/// <summary>
		/// Strips the Quest entries Meta's SDK writes into every Android manifest (permissions, required
		/// passthrough feature, splash and device metadata), which would hide the app from other headsets' stores.
		/// Runs after Meta's own manifest step.
		/// </summary>
		private class StripMetaManifestEntries : UnityEditor.Android.IPostGenerateGradleAndroidProject
		{
			private static readonly System.Xml.Linq.XNamespace Android = "http://schemas.android.com/apk/res/android";

			public int callbackOrder => 100000;

			public void OnPostGenerateGradleAndroidProject(string path)
			{
				if (ProfileInProgress == null) return;

				string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
				System.Xml.Linq.XDocument manifest = System.Xml.Linq.XDocument.Load(manifestPath);
				manifest.Descendants()
					.Where(element => IsMetaName((string)element.Attribute(Android + "name")))
					.ToList()
					.ForEach(element => element.Remove());
				manifest.Save(manifestPath);
			}

			private static bool IsMetaName(string name) => name != null && (
				name.StartsWith("com.oculus.", StringComparison.Ordinal) ||
				name.StartsWith("oculus.", StringComparison.Ordinal) ||
				name.StartsWith("horizonos.", StringComparison.Ordinal) ||
				name == "libhzos.meta.so" ||
				name == "com.samsung.android.vr.application.mode");
		}

		/// <summary>A headset profile builds only through <see cref="Build"/>: built any other way it would be a Quest app.</summary>
		public static void ThrowUnlessBuildingHere(string profileName, string menuItem)
		{
			BuildProfile active = BuildProfile.GetActiveBuildProfile();
			if (ProfileInProgress != profileName && active != null && active.name == profileName)
				throw new BuildFailedException($"Build the {profileName} profile with {menuItem}");
		}
	}
}
