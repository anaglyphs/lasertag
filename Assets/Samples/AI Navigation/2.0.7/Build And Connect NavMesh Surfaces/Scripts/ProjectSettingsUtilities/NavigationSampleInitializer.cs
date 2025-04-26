using System;
#if UNITY_EDITOR
using Unity.AI.Navigation.Samples.Editor;
using UnityEditor.SceneManagement;
#endif
using UnityEngine;

namespace Unity.AI.Navigation.Samples
{
    /// <summary>
    /// The Navigation samples use a couple of custom agent types.
    /// This class calls the NavigationSampleProjectSettingsGenerator to ensure that these agent types do exist within your Unity project.
    /// It is in no way necessary for using the Navigation package and is only used for the correct functioning of the samples.
    /// </summary>
    [ExecuteAlways]
    public class NavigationSampleInitializer : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField]
        NavigationSampleSettingsState settingsState;

        void Start()
        {
            if (!Application.isPlaying)
            {
                NavigationSampleProjectSettingsGenerator.GenerateAllProjectSettings(settingsState);

                DestroyGameObjectAndSave(gameObject);
            }
        }

        static void DestroyGameObjectAndSave(GameObject gameObject)
        {
            var scene = gameObject.scene;
            DestroyImmediate(gameObject);
            EditorSceneManager.SaveScene(scene);
        }
#endif
    }
}