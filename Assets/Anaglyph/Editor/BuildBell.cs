#if UNITY_EDITOR

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BuildFinishedBell : IPostprocessBuildWithReport
{
	public int callbackOrder => 0;

	public void OnPostprocessBuild(BuildReport report)
	{
		AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Anaglyph/Editor/build finished.ogg");
		if (clip != null)
		{
			var go = new GameObject("BuildSoundPlayer");
			go.AddComponent<AudioSource>().PlayOneShot(clip);
			Object.DestroyImmediate(go);
		}
		else
		{
			Debug.LogWarning("Could not find AudioClip for build sound.");
		}
	}
}
#endif