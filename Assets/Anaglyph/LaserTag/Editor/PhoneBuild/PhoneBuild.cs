using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Builds the Android phone APK from the "Android Phone" build profile. Quest and phones share
	/// the Android target, whose XR loaders build profiles don't carry, so the build swaps OpenXR
	/// for ARCore and swaps it back afterwards.
	/// </summary>
	public static class PhoneBuild
	{
		public const string ProfilePath = "Assets/Settings/Build Profiles/Android Phone.asset";
		private const string TemplateProfilePath = "Assets/Settings/Build Profiles/Debugging.asset";
		private const string RendererPath = "Assets/Anaglyph/LaserTag/URP Renderer Data.asset";
		private const string ARCoreLoader = "UnityEngine.XR.ARCore.ARCoreLoader";
		private const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";
		private const string OutputPath = "Builds/Phone/Lasertag Phone.apk";
		private const string ManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
		/// <summary>The phone's own manifest: no headset requirements, turning with the phone. Stands in for the Quest one while the phone builds.</summary>
		public const string PhoneManifestPath = "Assets/Settings/Build Profiles/Android Phone/AndroidManifest.xml";

		/// <summary>A phone build is under way, with its loader and manifest in place.</summary>
		public static bool InProgress { get; private set; }

		[MenuItem("Lasertag/Build Android Phone APK")]
		public static void BuildFromMenu()
		{
			BuildReport report = Build();
			EditorUtility.DisplayDialog("Android phone build", Describe(report), "OK");
		}

		/// <summary>For <c>-executeMethod</c> in batch mode: exits 0 when the APK was built.</summary>
		public static void BuildFromCommandLine()
		{
			BuildReport report = Build();
			Debug.Log($"[{nameof(PhoneBuild)}] {Describe(report)}");
			EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
		}

		private static string Describe(BuildReport report) => report == null
			? "not built: see the console"
			: $"{report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / 1e6:F0} MB at {report.summary.outputPath}";

		public static BuildReport Build()
		{
			BuildProfile profile = EnsureProfile();
			EnsureCommandBufferSupport();

			XRManagerSettings android = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android)?.Manager;
			if (android == null)
			{
				Debug.LogError($"[{nameof(PhoneBuild)}] no XR settings for Android");
				return null;
			}

			bool hadOpenXR = android.activeLoaders.Any(loader => loader != null && loader.GetType().FullName == OpenXRLoader);
			byte[] questManifest = File.ReadAllBytes(ManifestPath);
			try
			{
				File.Copy(PhoneManifestPath, ManifestPath, true);
				AssetDatabase.ImportAsset(ManifestPath);
				InProgress = true;

				if (hadOpenXR) XRPackageMetadataStore.RemoveLoader(android, OpenXRLoader, BuildTargetGroup.Android);
				if (!XRPackageMetadataStore.AssignLoader(android, ARCoreLoader, BuildTargetGroup.Android))
				{
					Debug.LogError($"[{nameof(PhoneBuild)}] couldn't assign the ARCore loader");
					return null;
				}

				Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
				return BuildPipeline.BuildPlayer(new BuildPlayerWithProfileOptions
				{
					buildProfile = profile,
					locationPathName = OutputPath,
					options = BuildOptions.Development,
				});
			}
			finally
			{
				InProgress = false;
				File.WriteAllBytes(ManifestPath, questManifest);
				AssetDatabase.ImportAsset(ManifestPath);
				XRPackageMetadataStore.RemoveLoader(android, ARCoreLoader, BuildTargetGroup.Android);
				if (hadOpenXR) XRPackageMetadataStore.AssignLoader(android, OpenXRLoader, BuildTargetGroup.Android);
				AssetDatabase.SaveAssets();
			}
		}

		// a copy of the Debugging profile, under its own name and app id, turning with the phone
		private static BuildProfile EnsureProfile()
		{
			if (!File.Exists(ProfilePath))
			{
				if (!AssetDatabase.CopyAsset(TemplateProfilePath, ProfilePath)) throw new InvalidOperationException($"couldn't copy {TemplateProfilePath}");
				string yaml = File.ReadAllText(ProfilePath)
					.Replace("m_Name: Debugging", "m_Name: Android Phone")
					.Replace("productName: Lasertag DEBUGGING", "productName: Lasertag Phone")
					.Replace("Android: com.trev3d.lasertagDEBUG", "Android: com.trev3d.lasertagphone")
					.Replace("defaultScreenOrientation: 3", "defaultScreenOrientation: 4");
				File.WriteAllText(ProfilePath, yaml);
				AssetDatabase.ImportAsset(ProfilePath, ImportAssetOptions.ForceUpdate);
			}

			return AssetDatabase.LoadAssetAtPath<BuildProfile>(ProfilePath);
		}

		// ARCore on Vulkan needs this feature in the renderer; it does nothing under any other provider
		private static void EnsureCommandBufferSupport()
		{
			UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
			if (renderer == null || renderer.rendererFeatures.Any(feature => feature is ARCommandBufferSupportRendererFeature)) return;

			ARCommandBufferSupportRendererFeature feature = ScriptableObject.CreateInstance<ARCommandBufferSupportRendererFeature>();
			feature.name = nameof(ARCommandBufferSupportRendererFeature);
			AssetDatabase.AddObjectToAsset(feature, renderer);
			AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

			// the renderer keeps each feature's file id beside it, as its inspector adds them
			SerializedObject serialized = new(renderer);
			SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
			SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
			features.InsertArrayElementAtIndex(features.arraySize);
			features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
			map.InsertArrayElementAtIndex(map.arraySize);
			map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			AssetDatabase.SaveAssets();
		}
	}
}
