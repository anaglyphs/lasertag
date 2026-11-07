#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Keeps the XR Simulation environment out of player builds. Its preferences asset lives in a Resources
	/// folder, so the environment it points to ships with every build. Cleared before a build, restored after.
	/// </summary>
	public class SimEnvironmentBuildExclusion : IPreprocessBuildWithReport
	{
		// in Library, so an editor crash mid-build still restores it next session
		private const string EnvironmentClearedForBuildFile = "Library/SimEnvironmentClearedForBuild.txt";

		// AR Foundation's own exclusion (order 1) puts the asset back before the build on Unity 6000.6
		public int callbackOrder => 2;

		public void OnPreprocessBuild(BuildReport report)
		{
			string environment = SimEnvironmentSetting.Current;
			if (environment == "") return;

			File.WriteAllText(EnvironmentClearedForBuildFile, environment);
			string problem = SimEnvironmentSetting.Clear();
			if (problem != null) throw new BuildFailedException($"Could not clear the XR Simulation environment: {problem}");
			EditorApplication.update += RestoreOnceBuildIsOver;
		}

		[InitializeOnLoadMethod]
		private static void RestoreAfterInterruptedBuild() => EditorApplication.update += RestoreOnceBuildIsOver;

		private static void RestoreOnceBuildIsOver()
		{
			if (BuildPipeline.isBuildingPlayer) return;
			EditorApplication.update -= RestoreOnceBuildIsOver;
			if (!File.Exists(EnvironmentClearedForBuildFile)) return;

			string environment = File.ReadAllText(EnvironmentClearedForBuildFile);
			string problem = SimEnvironmentSetting.Set(environment);
			if (problem != null)
			{
				Debug.LogWarning($"Could not restore the XR Simulation environment {environment}: {problem}");
				return;
			}
			File.Delete(EnvironmentClearedForBuildFile);
		}
	}
}
#endif
