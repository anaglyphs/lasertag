
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using VariableObjects;

namespace Anaglyph
{
	public class BuildProcess : IPreprocessBuildWithReport
	{
		public int callbackOrder => 0;

		public void OnPreprocessBuild(BuildReport report)
		{
			string[] result = AssetDatabase.FindAssets("BuildNumber", new[] { "Assets/Anaglyph/" });

			string path = AssetDatabase.GUIDToAssetPath(result[0]);
			StringObject config = (StringObject)AssetDatabase.LoadAssetAtPath(path, typeof(StringObject));

			config.Value = Application.platform == RuntimePlatform.IPhonePlayer
				? PlayerSettings.iOS.buildNumber
				: PlayerSettings.Android.bundleVersionCode.ToString();

			EditorUtility.SetDirty(config);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
	}
}
#endif
