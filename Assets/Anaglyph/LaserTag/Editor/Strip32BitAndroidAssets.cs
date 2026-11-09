#if UNITY_EDITOR
using System.IO;
using UnityEditor.Android;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Keeps 32-bit native libraries that plugins store as assets (OVRPlugin's assets/lib/armeabi-v7a) out of
	/// the APK. The ARM64-only target setting only filters jniLibs, and Quest never loads these.
	/// </summary>
	public class Strip32BitAndroidAssets : IPostGenerateGradleAndroidProject
	{
		private const string AssetPatternKey = "ignoreAssetsPattern = \"";
		private const string IgnoreArmeabiV7aFolders = "!armeabi-v7a:";

		public int callbackOrder => 0;

		public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
		{
			// dependency assets are merged in the app module, so its filter is the one that counts
			string launcherGradlePath = Path.Combine(unityLibraryPath, "..", "launcher", "build.gradle");
			string gradle = File.Exists(launcherGradlePath) ? File.ReadAllText(launcherGradlePath) : "";

			if (gradle.Contains(IgnoreArmeabiV7aFolders)) return;
			if (!gradle.Contains(AssetPatternKey))
			{
				Debug.LogWarning($"32-bit plugin assets stay in this build: no {AssetPatternKey}\" in {launcherGradlePath}");
				return;
			}

			File.WriteAllText(launcherGradlePath, gradle.Replace(AssetPatternKey, AssetPatternKey + IgnoreArmeabiV7aFolders));
		}
	}
}
#endif
