
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Anaglyph.Editor
{
	/// Unity restores the last open scene from Library/, which isn't in the repo,
	/// so a fresh clone opens an empty untitled scene instead of the game.
	[InitializeOnLoad]
	public static class OpenDefaultSceneOnFirstLoad
	{
		private const string DefaultScenePath = "Assets/Anaglyph/LaserTag/MainScene.unity";
		private const string SessionKey = "Anaglyph.CheckedDefaultScene";

		static OpenDefaultSceneOnFirstLoad()
		{
			if (SessionState.GetBool(SessionKey, false))
				return;

			SessionState.SetBool(SessionKey, true);
			EditorApplication.delayCall += OpenIfNoSceneLoaded;
		}

		private static void OpenIfNoSceneLoaded()
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
				return;

			Scene active = SceneManager.GetActiveScene();
			if (!string.IsNullOrEmpty(active.path) || active.isDirty)
				return;

			if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DefaultScenePath) != null)
				EditorSceneManager.OpenScene(DefaultScenePath, OpenSceneMode.Single);
		}
	}
}
#endif
