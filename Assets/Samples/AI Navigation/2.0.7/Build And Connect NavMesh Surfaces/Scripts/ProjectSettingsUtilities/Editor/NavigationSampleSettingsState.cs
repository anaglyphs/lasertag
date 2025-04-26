using UnityEngine;

namespace Unity.AI.Navigation.Samples.Editor
{
    /// <summary>
    /// This ScriptableObject is used by the NavigationSampleProjectSettingsGenerator to check whether the generation of agent types for the samples has already been done.
    /// It is in no way necessary for using the Navigation package and is only used for the correct functioning of the samples.
    /// </summary>
    public class NavigationSampleSettingsState : ScriptableObject
    {
        [SerializeField]
        bool hasGeneratedSettings;

        public bool generated
        {
            get => hasGeneratedSettings;
            set => hasGeneratedSettings = value;
        }
    }
}