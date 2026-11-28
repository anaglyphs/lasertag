using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>Builds the Android XR (Galaxy XR and other Android XR headsets) APK from the "Android XR" build profile.</summary>
	public static class AndroidXRBuild
	{
		public const string ProfileName = "Android XR";
		public const string ProfilePath = "Assets/Settings/Build Profiles/Android XR.asset";
		private const string MenuItem = "Lasertag/Build Android XR APK";

		private static readonly OpenXRVariantBuild.Variant Variant = new()
		{
			ProfileName = ProfileName,
			ProfilePath = ProfilePath,
			AndroidAppId = "com.trev3d.lasertagandroidxr",
			ManifestPath = "Assets/Settings/Build Profiles/Android XR/AndroidManifest.xml",
			DefaultOutputPath = "Builds/AndroidXR/Lasertag Android XR.apk",
			EnabledFeatures = new[]
			{
				"UnityEngine.XR.OpenXR.Features.Android.AndroidXRSupportFeature",
				"UnityEngine.XR.OpenXR.Features.Android.ARSessionFeature",
				"UnityEngine.XR.OpenXR.Features.Android.ARCameraFeature",
				"UnityEngine.XR.OpenXR.Features.Android.AROcclusionFeature",
				"UnityEngine.XR.OpenXR.Features.Android.ARAnchorFeature",
				"UnityEngine.XR.OpenXR.Features.Android.DisplayUtilitiesFeature",
			},
		};

		[MenuItem(MenuItem)]
		public static void BuildFromMenu() => OpenXRVariantBuild.BuildFromMenu(Variant);

		/// <inheritdoc cref="OpenXRVariantBuild.BuildFromCommandLine"/>
		public static void BuildFromCommandLine() => OpenXRVariantBuild.BuildFromCommandLine(Variant);

		public static BuildReport Build(string outputPath, bool development = false) =>
			OpenXRVariantBuild.Build(Variant, outputPath, development);

		private class Guard : IPreprocessBuildWithReport
		{
			public int callbackOrder => -1000;

			public void OnPreprocessBuild(BuildReport report) => OpenXRVariantBuild.ThrowUnlessBuildingHere(ProfileName, MenuItem);
		}
	}
}
