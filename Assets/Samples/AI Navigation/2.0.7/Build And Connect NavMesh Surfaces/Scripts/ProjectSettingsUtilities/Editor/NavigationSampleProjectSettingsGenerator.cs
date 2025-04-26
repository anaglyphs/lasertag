using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.AI.Navigation.Samples.Editor
{
    /// <summary>
    /// This class creates (if missing) or resets (after reimporting the samples) the agent types used by the samples.
    /// It is in no way necessary for using the Navigation package and is only used for the correct functioning of the samples.
    /// </summary>
    public static class NavigationSampleProjectSettingsGenerator
    {
        const string k_NavMeshSettingsPath = "ProjectSettings/NavMeshAreas.asset";

        const float k_DefaultAgentRadius = 0.5f;
        const float k_DefaultAgentHeight = 2.0f;
        const float k_DefaultAgentClimb = 0.4f;
        const float k_DefaultAgentSlope = 45.0f;

        const string k_HumanoidAgentTypeName = "Humanoid for Navigation Sample";
        const int k_HumanoidAgentTypeID = 1;
        const float k_HumanoidAgentClimb = 0.75f;

        const string k_OgreAgentTypeName = "Ogre for Navigation Sample";
        const int k_OgreAgentTypeID = 2;
        const float k_OgreAgentRadius = 1.0f;
        const float k_OgreAgentSlope = 36.0f;

        static SerializedObject s_NavMeshParameters;
        static SerializedProperty s_AgentsSettings;
        static SerializedProperty s_SettingNames;

        public static void GenerateAllProjectSettings(NavigationSampleSettingsState settingsState)
        {
            s_NavMeshParameters = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(k_NavMeshSettingsPath)[0]);

            s_AgentsSettings = s_NavMeshParameters.FindProperty("m_Settings");
            s_SettingNames = s_NavMeshParameters.FindProperty("m_SettingNames");

            var hasInitializedOnProjectLoad = settingsState.generated;

            GenerateProjectSettings(k_HumanoidAgentTypeName, k_HumanoidAgentTypeID, CreateHumanoidAgentSettings, hasInitializedOnProjectLoad);
            GenerateProjectSettings(k_OgreAgentTypeName, k_OgreAgentTypeID, CreateOgreAgentSettings, hasInitializedOnProjectLoad);
            s_NavMeshParameters.ApplyModifiedProperties();
            settingsState.generated = true;

            AssetDatabase.Refresh();
        }

        static void GenerateProjectSettings(string agentTypeName, int agentTypeID, CreateAgentSettings createAgentSettings, bool hasInitializedOnProjectLoad)
        {
            var agentProperty = GetSerializedSettingsByID(agentTypeID, out var index);
            if (index < 0)
            {
                // Create new settings
                var agentSettings = createAgentSettings();
                agentSettings.agentTypeID = agentTypeID;
                AddAgentSettings(agentSettings, agentTypeName, agentTypeID);
            }
            else
            {
                if (!HasSettingsNameAtIndex(index, agentTypeName))
                {
                    // Don't update the settings
                    var settingsName = s_SettingNames.GetArrayElementAtIndex(index).stringValue;
                    if (!IsAgentTypeSetWithDefaultValues(index, agentTypeID))
                    {
                        Debug.LogWarning($"The agent type {agentTypeName} used in the Navigation Samples could not be created. The agent type {settingsName} will be used instead. {settingsName} does not have the expected values for {agentTypeName}. The expected values for {agentTypeName} are written in the README.md file of the samples. The values of agent types are updatable in the Agents tab of the AI > Navigation window.");
                    }
                }
                else if (!hasInitializedOnProjectLoad && !IsAgentTypeSetWithDefaultValues(index, agentTypeID))
                {
                    // Update existing settings to default values
                    var radius = agentProperty.FindPropertyRelative("agentRadius").floatValue;
                    var height = agentProperty.FindPropertyRelative("agentHeight").floatValue;
                    var climb = agentProperty.FindPropertyRelative("agentClimb").floatValue;
                    var slope = agentProperty.FindPropertyRelative("agentSlope").floatValue;

                    var tempAgentSettings = createAgentSettings();
                    UpdateSettings(agentProperty, tempAgentSettings);
                    NavMesh.RemoveSettings(tempAgentSettings.agentTypeID);

                    Debug.Log($"Navigation Samples reimport detected. The agent type {agentTypeName} has been reset to its default values. Its values before the reset were: Radius = {radius}, Height = {height}, Step height = {climb} and Max Slope = {slope}.");
                }
            }
        }

        static bool IsAgentTypeSetWithDefaultValues(int index, int agentTypeID)
        {
            var agentTypeSettings = s_AgentsSettings.GetArrayElementAtIndex(index);
            var radius = agentTypeSettings.FindPropertyRelative("agentRadius").floatValue;
            var height = agentTypeSettings.FindPropertyRelative("agentHeight").floatValue;
            var climb = agentTypeSettings.FindPropertyRelative("agentClimb").floatValue;
            var slope = agentTypeSettings.FindPropertyRelative("agentSlope").floatValue;

            var result = false;
            switch (agentTypeID)
            {
                case k_HumanoidAgentTypeID:
                    result = radius == k_DefaultAgentRadius && height == k_DefaultAgentHeight && climb == k_HumanoidAgentClimb && slope == k_DefaultAgentSlope;
                    break;
                case k_OgreAgentTypeID:
                    result = radius == k_OgreAgentRadius && height == k_DefaultAgentHeight && climb == k_DefaultAgentClimb && slope == k_OgreAgentSlope;
                    break;
            }

            return result;
        }

        delegate NavMeshBuildSettings CreateAgentSettings();

        static NavMeshBuildSettings CreateHumanoidAgentSettings()
        {
            var humanoidAgentSettings = NavMesh.CreateSettings();
            humanoidAgentSettings.agentRadius = k_DefaultAgentRadius;
            humanoidAgentSettings.agentHeight = k_DefaultAgentHeight;
            humanoidAgentSettings.agentClimb = k_HumanoidAgentClimb;
            humanoidAgentSettings.agentSlope = k_DefaultAgentSlope;

            return humanoidAgentSettings;
        }

        static NavMeshBuildSettings CreateOgreAgentSettings()
        {
            var ogreAgentSettings = NavMesh.CreateSettings();
            ogreAgentSettings.agentRadius = k_OgreAgentRadius;
            ogreAgentSettings.agentHeight = k_DefaultAgentHeight;
            ogreAgentSettings.agentClimb = k_DefaultAgentClimb;
            ogreAgentSettings.agentSlope = k_OgreAgentSlope;

            return ogreAgentSettings;
        }

        static SerializedProperty GetSerializedSettingsByID(int agentTypeID, out int index)
        {
            index = -1;
            SerializedProperty settings = null;
            for (var i = 0; i < s_AgentsSettings.arraySize; i++)
            {
                if (s_AgentsSettings.GetArrayElementAtIndex(i).FindPropertyRelative("agentTypeID").intValue == agentTypeID)
                {
                    index = i;
                    settings = s_AgentsSettings.GetArrayElementAtIndex(i);
                    break;
                }
            }

            return settings;
        }

        static bool HasSettingsNameAtIndex(int index, string agentTypeName)
        {
            return s_SettingNames.GetArrayElementAtIndex(index).stringValue.Equals(agentTypeName);
        }

        static void AddAgentSettings(NavMeshBuildSettings agentSettings, string agentTypeName, int agentTypeID)
        {
            var nbNames = s_SettingNames.arraySize;
            s_SettingNames.InsertArrayElementAtIndex(nbNames);
            var newName = s_SettingNames.GetArrayElementAtIndex(nbNames);
            newName.stringValue = agentTypeName;

            var nbAgents = s_AgentsSettings.arraySize;
            s_AgentsSettings.InsertArrayElementAtIndex(nbAgents);
            var addedAgentType = s_AgentsSettings.GetArrayElementAtIndex(nbAgents);

            UpdateSettings(addedAgentType, agentSettings);

            SetAgentPropertyValue(addedAgentType, "agentTypeID", agentTypeID);
        }

        static void UpdateSettings(SerializedProperty agentToUpdate, NavMeshBuildSettings newSettings)
        {
            SetAgentPropertyValue(agentToUpdate, "agentRadius", newSettings.agentRadius);
            SetAgentPropertyValue(agentToUpdate, "agentHeight", newSettings.agentHeight);
            SetAgentPropertyValue(agentToUpdate, "agentClimb", newSettings.agentClimb);
            SetAgentPropertyValue(agentToUpdate, "agentSlope", newSettings.agentSlope);
        }

        static void SetAgentPropertyValue(SerializedProperty agent, string propertyName, int value)
        {
            var property = agent.FindPropertyRelative(propertyName);
            property.intValue = value;
        }

        static void SetAgentPropertyValue(SerializedProperty agent, string propertyName, float value)
        {
            var property = agent.FindPropertyRelative(propertyName);
            property.floatValue = value;
        }
    }
}