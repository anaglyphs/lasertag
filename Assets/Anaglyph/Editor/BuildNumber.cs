#if UNITY_EDITOR
using Anaglyph.VariableObjects;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Anaglyph.Editor
{
	public class BuildProcess : IPreprocessBuildWithReport
	{
		public int callbackOrder => 0;

		public void OnPreprocessBuild(BuildReport report)
		{
			string[] result = AssetDatabase.FindAssets("BuildNumber t:StringObject", new[] { "Assets/Anaglyph/" });

			if (result.Length == 0)
				throw new BuildFailedException("Could not find BuildNumber StringObject asset under Assets/Anaglyph/");

			string path = AssetDatabase.GUIDToAssetPath(result[0]);
			StringObject config = AssetDatabase.LoadAssetAtPath<StringObject>(path);

			config.SetDefaultVal(report.summary.platform == BuildTarget.iOS
				? PlayerSettings.iOS.buildNumber
				: PlayerSettings.Android.bundleVersionCode.ToString());

			EditorUtility.SetDirty(config);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
	}
}
#endif
