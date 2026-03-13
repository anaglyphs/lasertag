/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEditor.SceneManagement;
using System.Collections;
using System.Collections.Generic;

[CustomEditor(typeof(OVRManager))]
public class OVRManagerEditor : Editor
{
    private SerializedProperty _requestBodyTrackingPermissionOnStartup;
    private SerializedProperty _requestFaceTrackingPermissionOnStartup;
    private SerializedProperty _requestEyeTrackingPermissionOnStartup;
    private SerializedProperty _requestScenePermissionOnStartup;
    private SerializedProperty _requestRecordAudioPermissionOnStartup;
    private SerializedProperty _requestPassthroughCameraAccessPermissionOnStartup;

    private const string kOVRManagerEditorExpandedSectionsKey = "OVRManagerEditor_ExpandedSections";
    private const string kOVRManagerEditorActiveTargetPlatformKey = "OVRManagerEditor_ActiveTargetPlaform";

    class Styles
    {
        public static readonly GUIStyle categoryBox = new GUIStyle(EditorStyles.helpBox);
        public static readonly GUIStyle foldoutHeader = new GUIStyle(EditorStyles.foldout);
        static Styles()
        {
            categoryBox.border.left = -4;
            categoryBox.padding.left = 4;
            foldoutHeader.fontStyle = FontStyle.Bold;
        }
    }

    private enum TargetPlatform
    {
        Quest,
        Link
    }

    private bool _activeTargetPlatformSet;
    private TargetPlatform _activeTargetPlatform;

    [System.Flags]
    private enum EditorSection
    {
        TargetDevices = (1 << 0),
        PerformanceQuality = (1 << 1),
        Tracking = (1 << 2),
        HeadTracking = (1 << 3),
        ControllerTracking = (1 << 4),
        HandTracking = (1 << 5),
        BodyTracking = (1 << 6),
        FaceTracking = (1 << 7),
        InsightPassthrough = (1 << 8),
        MixedRealityCapture = (1 << 9),
        PermissionRequest = (1 << 10),
        ProfilingMetrics = (1 << 11),
        ProjectConfig = (1 << 12),

    }

    private bool _expandedEditorSectionsSet = false;
    private EditorSection _expandedEditorSections;

    private int _nestedSectionLevel = 0;

    void OnEnable()
    {
        if (!_activeTargetPlatformSet)
        {
            _activeTargetPlatform = (TargetPlatform)EditorPrefs.GetInt(kOVRManagerEditorActiveTargetPlatformKey,
                (int)TargetPlatform.Quest);
            _activeTargetPlatformSet = true;
        }

        if (!_expandedEditorSectionsSet)
        {
            _expandedEditorSections = (EditorSection)EditorPrefs.GetInt(kOVRManagerEditorExpandedSectionsKey, 0);
            _expandedEditorSectionsSet = true;
        }
        _requestBodyTrackingPermissionOnStartup =
            serializedObject.FindProperty(nameof(OVRManager.requestBodyTrackingPermissionOnStartup));
        _requestFaceTrackingPermissionOnStartup =
            serializedObject.FindProperty(nameof(OVRManager.requestFaceTrackingPermissionOnStartup));
        _requestEyeTrackingPermissionOnStartup =
            serializedObject.FindProperty(nameof(OVRManager.requestEyeTrackingPermissionOnStartup));
        _requestScenePermissionOnStartup =
            serializedObject.FindProperty(nameof(OVRManager.requestScenePermissionOnStartup));
        _requestRecordAudioPermissionOnStartup =
            serializedObject.FindProperty(nameof(OVRManager.requestRecordAudioPermissionOnStartup));
        _requestPassthroughCameraAccessPermissionOnStartup =
            serializedObject.FindProperty(nameof(OVRManager.requestPassthroughCameraAccessPermissionOnStartup));

        OVRManager manager = target as OVRManager;
        manager?.UpdateDynamicResolutionVersion();


        OVRRuntimeSettings runtimeSettings = OVRRuntimeSettings.GetRuntimeSettings();
        if (!runtimeSettings.QuestVisibilityMeshOverriden)
        {
            string occlusionMeshShaderPath = "Packages/com.unity.render-pipelines.universal/Shaders/XR/XROcclusionMesh.shader";
            if (File.Exists(occlusionMeshShaderPath))
            {
                string shaderText = File.ReadAllText(occlusionMeshShaderPath);
                if (shaderText.Contains("USE_XR_OCCLUSION_MESH_COMBINED_MULTIVIEW"))
                {
                    runtimeSettings.VisibilityMesh = true;
                    OVRRuntimeSettings.CommitRuntimeSettings(runtimeSettings);
                }
            }
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.ApplyModifiedProperties();
        bool modified = false;
        OVRRuntimeSettings runtimeSettings = OVRRuntimeSettings.GetRuntimeSettings();
        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;
        OVRManager manager = (OVRManager)target;

        DrawTargetPlatformMenu();

        DrawTargetDevicesSection(projectConfig);

        DrawProjectConfigSection(projectConfig);


        DrawPerformanceQualitySection(manager, runtimeSettings, ref modified);

        DrawTrackingSection(manager, runtimeSettings, projectConfig, ref modified);

        DrawOvrMetricsSection(manager, ref modified);

        DrawInsightPassthroughSection(manager, projectConfig, ref modified);

        DrawPermissionRequestsSection(manager, projectConfig, ref modified);

        DrawMixedRealityCaptureSection(manager, projectConfig, ref modified);

        if (modified)
        {
            EditorUtility.SetDirty(target);
        }

        serializedObject.ApplyModifiedProperties();

        if (Highlighter.activeVisible)
        {
            if (Event.current.type == EventType.MouseDown || Event.current.type == EventType.ScrollWheel)
            {
                Highlighter.Stop();
            }
        }
    }
    private bool BeginExpandSection(EditorSection section, string name, string docLink = null)
    {

        if (_nestedSectionLevel == 0)
        {
            EditorGUILayout.BeginVertical(Styles.categoryBox);
        }
        else
        {
            EditorGUILayout.Space();
        }
        EditorGUI.indentLevel++;

        EditorGUILayout.BeginHorizontal();
        bool wasExpanded = (_expandedEditorSections & section) != 0;
        bool isExpanded = EditorGUILayout.Foldout(wasExpanded,
            name, Styles.foldoutHeader);
        if (docLink != null)
        {
            OVREditorUtil.DisplayDocLink(docLink);
        }
        EditorGUILayout.EndHorizontal();

        _nestedSectionLevel++;

        if (isExpanded == wasExpanded)
        {
            return isExpanded;
        }

        if (isExpanded)
        {
            _expandedEditorSections |= section;
        }
        else
        {
            _expandedEditorSections &= ~section;
        }
        EditorPrefs.SetInt(kOVRManagerEditorExpandedSectionsKey, (int)_expandedEditorSections);

        return isExpanded;
    }

    private void EndExpandSection()
    {
        _nestedSectionLevel--;
        EditorGUI.indentLevel--;

        if (_nestedSectionLevel == 0)
        {
            EditorGUILayout.EndVertical();
        }
    }

    private void ShowQuestFeaturesHelpBox(string helpInfo, OVRProjectConfigEditor.ProjectConfigTab tab = OVRProjectConfigEditor.ProjectConfigTab.General, OVRProjectConfigEditor.HighlightLabel highlightLabel = OVRProjectConfigEditor.HighlightLabel.ProjectConfig, MessageType messageType = MessageType.Info)
    {
        EditorGUILayout.HelpBox(helpInfo, messageType,
            true);
        if (Event.current.type == EventType.MouseDown && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
        {
            OVRProjectConfigEditor.selectedTab = tab;
            _expandedEditorSections |= EditorSection.ProjectConfig;
            Highlighter.Highlight("Inspector", highlightLabel.ToString());
        }
    }

    private void DrawTargetPlatformMenu()
    {
        TargetPlatform newTargetPlatform =
            (TargetPlatform)GUILayout.Toolbar((int)_activeTargetPlatform, new[] { "Quest", "Editor + Link" });

        if (newTargetPlatform != _activeTargetPlatform)
        {
            EditorPrefs.SetInt(kOVRManagerEditorActiveTargetPlatformKey, (int)newTargetPlatform);
            _activeTargetPlatform = newTargetPlatform;
        }
    }

    private void DrawProjectConfigSection(OVRProjectConfig projectConfig)
    {
        if (_activeTargetPlatform != TargetPlatform.Quest)
        {
            return;
        }

        if (!BeginExpandSection(EditorSection.ProjectConfig, "Quest Features"))
        {
            EndExpandSection();
            return;
        }
        OVRProjectConfigEditor.DrawProjectConfigInspector(projectConfig, drawBoxAndLabel: false);
        EndExpandSection();
    }

    private void DrawTargetDevicesSection(OVRProjectConfig projectConfig)
    {
        if (_activeTargetPlatform != TargetPlatform.Quest)
        {
            return;
        }

        if (!BeginExpandSection(EditorSection.TargetDevices, "Target Devices"))
        {
            EndExpandSection();
            return;
        }

        OVRProjectConfigEditor.DrawTargetDeviceInspector(projectConfig, drawLabel: false);
        EndExpandSection();
    }

    private void DrawPerformanceQualitySection(OVRManager manager, OVRRuntimeSettings runtimeSettings, ref bool modified)
    {
        if (!BeginExpandSection(EditorSection.PerformanceQuality, "Performance & Quality"))
        {
            EndExpandSection();
            return;
        }

        var sharpenType = manager.sharpenType;
        OVREditorUtil.SetupEnumField(target, new GUIContent("Sharpen Type", "The sharpen filter of the eye buffer. This amplifies contrast and fine details."), ref sharpenType, ref modified);
        manager.sharpenType = sharpenType;


        OVRManager.ColorSpace colorGamut = runtimeSettings.colorSpace;
        OVREditorUtil.SetupEnumField(target, new GUIContent("Color Gamut",
                "The target color gamut when displayed on the HMD"), ref colorGamut, ref modified,
            "https://developer.oculus.com/documentation/unity/unity-color-space/");
        manager.colorGamut = colorGamut;

        bool localDimming = manager.localDimming;
        bool visibilityMesh = runtimeSettings.VisibilityMesh;

        if (_activeTargetPlatform == TargetPlatform.Quest)
        {
            OVREditorUtil.SetupBoolField(target,
                new GUIContent("Local Dimming",
                    "Available only for devices that support local dimming. It improves visual quality with " +
                    "a better display contrast ratio, but at a minor GPU performance cost."),
                ref localDimming, ref modified);


            OVREditorUtil.SetupBoolField(target,
                new GUIContent("Visibility Mesh",
                    "Toggle Visibility Mesh for Quest. This feature is supported on OpenXR with URP 14.0.7 Meta Fork or URP 17.0.3."),
                ref visibilityMesh, ref modified);
        }

        if (modified)
        {
            manager.localDimming = localDimming;
            runtimeSettings.colorSpace = colorGamut;
            if (runtimeSettings.VisibilityMesh != visibilityMesh)
            {
                runtimeSettings.VisibilityMesh = visibilityMesh;
                runtimeSettings.QuestVisibilityMeshOverriden = true;
            }
            OVRRuntimeSettings.CommitRuntimeSettings(runtimeSettings);
        }

        DrawDynamicResolutionSection(manager, ref modified);


        EndExpandSection();
    }

    private void DrawTrackingSection(OVRManager manager, OVRRuntimeSettings runtimeSettings, OVRProjectConfig projectConfig, ref bool modified)
    {
        if (!BeginExpandSection(EditorSection.Tracking, "Tracking"))
        {
            EndExpandSection();
            return;
        }

        var trackingOriginType = manager.trackingOriginType;
        OVREditorUtil.SetupEnumField(target, new GUIContent("Tracking Origin Type", "Defines the origin position of the player relative to their tracking space"), ref trackingOriginType, ref modified);
        manager.trackingOriginType = trackingOriginType;

        DrawHeadTrackingSection(manager, runtimeSettings, ref modified);

        DrawControllerTrackingSection(manager, runtimeSettings, ref modified);

        DrawHandTrackingSection(manager, runtimeSettings, projectConfig, ref modified);

        DrawBodyTrackingSection(manager, runtimeSettings, projectConfig, ref modified);

        DrawFaceTrackingSection(manager, runtimeSettings, projectConfig, ref modified);

        EndExpandSection();
    }

    private void DrawHeadTrackingSection(OVRManager manager, OVRRuntimeSettings runtimeSettings, ref bool modified)
    {
        if (!BeginExpandSection(EditorSection.HeadTracking, "Head Tracking"))
        {
            EndExpandSection();
            return;
        }

        OVREditorUtil.SetupBoolField(target, new GUIContent("Use Position Tracking", "If true, head tracking will affect the position of each OVRCameraRig's cameras."), ref manager.usePositionTracking, ref modified);

        var headPoseRelativeRotation = manager.headPoseRelativeOffsetRotation;
        var headPoseRelativeTranslation = manager.headPoseRelativeOffsetTranslation;
        OVREditorUtil.SetupVector3Field(target, new GUIContent("Head Pose Relative Offset Rotation", "Applies a fixed rotation offset to head pose"), ref headPoseRelativeRotation, ref modified);
        OVREditorUtil.SetupVector3Field(target, new GUIContent("Head Pose Relative Offset Translation", "Applies a fixed translation offset to head pose"), ref headPoseRelativeTranslation, ref modified);

        if (modified)
        {
            manager.headPoseRelativeOffsetRotation = headPoseRelativeRotation;
            manager.headPoseRelativeOffsetTranslation = headPoseRelativeTranslation;
        }

        OVREditorUtil.SetupBoolField(target, new GUIContent("Use IPD in Position Tracking", "If true, the distance between the user's eyes will affect the position of each OVRCameraRig's cameras."), ref manager.useIPDInPositionTracking, ref modified);


        if (_activeTargetPlatform == TargetPlatform.Link)
        {
            OVREditorUtil.SetupBoolField(target,
                new GUIContent("Reset Tracker On Load",
                    "If true, each scene load will cause the head pose to reset."),
                ref manager.resetTrackerOnLoad, ref modified);

            OVREditorUtil.SetupBoolField(target, new GUIContent("Allow Recenter",
                "If true, the Reset View in the universal menu will cause the pose to be reset in PC VR. This should " +
                "generally be enabled for applications with a stationary position in the virtual world and will allow " +
                "the View Reset command to place the person back to a predefined location (such as a cockpit seat). " +
                "Set this to false if you have a locomotion system because resetting the view would effectively teleport " +
                "the player to potentially invalid locations."), ref manager.AllowRecenter, ref modified);
        }
#if UNITY_2020_3_OR_NEWER
        OVREditorUtil.SetupBoolField(target, new GUIContent("Late Latching", "Late latching is a feature that can reduce rendered head/controller latency by a substantial amount. " +
                                                                             "Before enabling, be sure to go over the documentation to ensure that the feature is used correctly. " +
                                                                             "This feature must also be enabled through the Oculus XR Plugin settings."), ref manager.LateLatching, ref modified);
#endif
        EndExpandSection();
    }

    private void DrawControllerTrackingSection(OVRManager manager, OVRRuntimeSettings runtimeSettings, ref bool modified)
    {
        if (!BeginExpandSection(EditorSection.ControllerTracking, "Controller Tracking"))
        {
            EndExpandSection();
            return;
        }

        OVREditorUtil.SetupBoolField(target, new GUIContent("Late Controller Update", "If true, rendered controller latency is reduced by several ms, as the left/right controllers will " +
            "have their positions updated right before rendering."), ref manager.LateControllerUpdate, ref modified);

        var controllerDrivenHandPosesType = manager.controllerDrivenHandPosesType;
        OVREditorUtil.SetupEnumField(target, new GUIContent("Controller Driven Hand Poses Type", "Defines if hand poses can be populated by controller data."), ref controllerDrivenHandPosesType, ref modified);

        if (modified)
        {
            manager.controllerDrivenHandPosesType = controllerDrivenHandPosesType;
        }

        EndExpandSection();
    }

#if UNITY_EDITOR || UNITY_STANDALONE_WIN || UNITY_ANDROID
    private void DrawMixedRealityCaptureSection(OVRManager manager, OVRProjectConfig projectConfig, ref bool modified)
    {
        if (!BeginExpandSection(EditorSection.MixedRealityCapture, "Mixed Reality Capture (Deprecated)", "https://developer.oculus.com/documentation/unity/unity-mrc/"))
        {
            EndExpandSection();
            return;
        }

        string[] layerMaskOptions = new string[32];
        for (int i = 0; i < 32; ++i)
        {
            layerMaskOptions[i] = LayerMask.LayerToName(i);
            if (layerMaskOptions[i].Length == 0)
            {
                layerMaskOptions[i] = "<Layer " + i.ToString() + ">";
            }
        }

        if (_activeTargetPlatform == TargetPlatform.Quest)
        {
            OVREditorUtil.SetupEnumField(target, "Quest MRC Activation Mode", ref manager.mrcActivationMode,
                ref modified);
        }

        if (_activeTargetPlatform == TargetPlatform.Link)
        {
            OVREditorUtil.SetupBoolField(target, "Enable Mixed Reality Capture In Editor",
                ref manager.enableMixedReality,
                ref modified);
        }

        // CompositionMethod.External is the only composition method that is available.
        if (manager.compositionMethod != OVRManager.CompositionMethod.External)
        {
            manager.compositionMethod = OVRManager.CompositionMethod.External;
            modified = true;
        }

        OVREditorUtil.SetupLayerMaskField(target, "Extra Hidden Layers", ref manager.extraHiddenLayers,
            layerMaskOptions, ref modified);
        OVREditorUtil.SetupLayerMaskField(target, "Extra Visible Layers", ref manager.extraVisibleLayers,
            layerMaskOptions, ref modified);
        OVREditorUtil.SetupBoolField(target, "Dynamic Culling Mask", ref manager.dynamicCullingMask, ref modified);

        if (_activeTargetPlatform == TargetPlatform.Link)
        {
            OVREditorUtil.SetupColorField(target, "Backdrop Color",
                ref manager.externalCompositionBackdropColorRift, ref modified);
        }

        if (_activeTargetPlatform == TargetPlatform.Quest)
        {
            OVREditorUtil.SetupColorField(target, "Backdrop Color",
                ref manager.externalCompositionBackdropColorQuest, ref modified);
        }

        EndExpandSection();
    }
#else
    private void DrawMixedRealityCaptureSection(OVRManager manager, OVRProjectConfig projectConfig, ref bool modified) { }
#endif

    private void DrawHandTrackingSection(OVRManager manager, OVRRuntimeSettings runtimeSettings, OVRProjectConfig projectConfig, ref bool modified)
    {
        if (!BeginExpandSection(EditorSection.HandTracking, "Hand Tracking"))
        {
            EndExpandSection();
            return;
        }

        if (_activeTargetPlatform == TargetPlatform.Quest)
        {
            if (projectConfig.handTrackingSupport == OVRProjectConfig.HandTrackingSupport.ControllersOnly)
            {
                ShowQuestFeaturesHelpBox("Hand Tracking Support must be \"Controllers And Hands\" or \"Hands Only\" in the General section of Quest Features to enable Hand Tracking.", highlightLabel: OVRProjectConfigEditor.HighlightLabel.HandTrackingSupport);
            }

            // Multimodal hands and controllers section
            EditorGUI.BeginDisabledGroup(projectConfig.handTrackingSupport !=
                                         OVRProjectConfig.HandTrackingSupport.ControllersAndHands);
        }

        OVREditorUtil.SetupBoolField(target,
            new GUIContent("Simultaneous Hands And Controllers Enabled",
                           "Allows the application to use simultaneous hands and controllers functionality. This option must be enabled at build time."),
            ref manager.SimultaneousHandsAndControllersEnabled,
            ref modified);
        if (_activeTargetPlatform == TargetPlatform.Quest)
        {
            EditorGUI.EndDisabledGroup();
        }

        GUIContent enableSimultaneousHandsAndControllersOnStartup;
        if (_activeTargetPlatform == TargetPlatform.Quest)
        {
            EditorGUI.BeginDisabledGroup(projectConfig.handTrackingSupport == OVRProjectConfig.HandTrackingSupport.ControllersOnly ||
                                         !manager.SimultaneousHandsAndControllersEnabled);
            enableSimultaneousHandsAndControllersOnStartup = new GUIContent(
                "Launch Simultaneous H&C On Startup",
                "Launches simultaneous hands and controllers on startup for the scene. Simultaneous Hands and Controllers Capability must be enabled in Quest Features.");
        }
        else
        {
            enableSimultaneousHandsAndControllersOnStartup = new GUIContent("Enable simultaneous hands and controllers mode on startup",
                "Launches simultaneous hands and controllers on startup for the scene.");
        }
        OVREditorUtil.SetupBoolField(target, enableSimultaneousHandsAndControllersOnStartup,
            ref manager.launchSimultaneousHandsControllersOnStartup,
            ref modified);
        if (_activeTargetPlatform == TargetPlatform.Quest)
        {
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(projectConfig.handTrackingSupport == OVRProjectConfig.HandTrackingSupport.ControllersOnly);
        }

        EditorGUILayout.Space();
        OVREditorUtil.SetupBoolField(target,
            new GUIContent("Wide Motion Mode Hand Poses",
                           "Defines if hand poses can leverage algorithms to retrieve hand poses outside of the normal tracking area."),
            ref manager.wideMotionModeHandPosesEnabled, ref modified);




        EditorGUILayout.Space();
        // Hand Tracking Settings
        OVRHandSkeletonVersion handSkeletonVersion = runtimeSettings.HandSkeletonVersion;
        OVREditorUtil.SetupEnumField(target, new GUIContent("Hand Skeleton Version",
                "The version of the hand skeleton"), ref handSkeletonVersion, ref modified);

        if (_activeTargetPlatform == TargetPlatform.Quest)
        {
            EditorGUI.EndDisabledGroup();
        }

        if (modified)
        {
            runtimeSettings.HandSkeletonVersion = handSkeletonVersion;
            OVRRuntimeSettings.CommitRuntimeSettings(runtimeSettings);
        }
        EditorGUI.EndDisabledGroup();

        EndExpandSection();
    }

    private void DrawBodyTrackingSection(OVRManager manager, OVRRuntimeSettings runtimeSettings,
        OVRProjectConfig projectConfig, ref bool modified)
    {
        if (_activeTargetPlatform != TargetPlatform.Quest)
        {
            return;
        }

        if (!BeginExpandSection(EditorSection.BodyTracking, "Body Tracking"))
        {
            EndExpandSection();
            return;
        }

        if (projectConfig.bodyTrackingSupport == OVRProjectConfig.FeatureSupport.None)
        {
            ShowQuestFeaturesHelpBox("Body Tracking Support must be \"Required\" or \"Supported\" in the General section of Quest Features to enable Body Tracking.", highlightLabel: OVRProjectConfigEditor.HighlightLabel.BodyTrackingSupport);
        }

        // Body Tracking settings
        EditorGUI.BeginDisabledGroup(projectConfig.bodyTrackingSupport == OVRProjectConfig.FeatureSupport.None);
        OVRPlugin.BodyTrackingFidelity2 bodyTrackingFidelity = runtimeSettings.BodyTrackingFidelity;
        OVREditorUtil.SetupEnumField(target, new GUIContent("Body Tracking Fidelity",
                "Body Tracking Fidelity defines quality of tracking"), ref bodyTrackingFidelity, ref modified,
            "https://developer.oculus.com/documentation/unity/move-body-tracking/");

        if (modified)
        {
            runtimeSettings.BodyTrackingFidelity = bodyTrackingFidelity;
            OVRRuntimeSettings.CommitRuntimeSettings(runtimeSettings);
        }

        OVRPlugin.BodyJointSet bodyTrackingJointSet = runtimeSettings.BodyTrackingJointSet;
        OVREditorUtil.SetupEnumField(target, new GUIContent("Body Tracking Joint Set",
                "Body Tracking Joint Set"), ref bodyTrackingJointSet, ref modified,
            "https://developer.oculus.com/documentation/unity/move-body-tracking/");
        if (modified)
        {
            runtimeSettings.BodyTrackingJointSet = bodyTrackingJointSet;
            OVRRuntimeSettings.CommitRuntimeSettings(runtimeSettings);
        }

        EditorGUI.EndDisabledGroup();

        // Also warn about Body API when simultaneous hands and controllers is enabled
        bool bodyEnabled =
#pragma warning disable CS0618 // Type or member is obsolete
            (GameObject.FindObjectOfType<OVRBody>() != null || manager.wideMotionModeHandPosesEnabled == true);
#pragma warning restore CS0618 // Type or member is obsolete
        if (bodyEnabled && manager.SimultaneousHandsAndControllersEnabled)
        {
            EditorGUILayout.HelpBox(
                "Simultaneous hands and controllers are not supported together with Body API. Please select only one of these features",
                MessageType.Warning, true);
        }
        EndExpandSection();
    }
    private void DrawFaceTrackingSection(OVRManager manager, OVRRuntimeSettings runtimeSettings, OVRProjectConfig projectConfig, ref bool modified)
    {
        if (_activeTargetPlatform != TargetPlatform.Quest)
        {
            return;
        }

        if (!BeginExpandSection(EditorSection.FaceTracking, "Face Tracking"))
        {
            EndExpandSection();
            return;
        }

        bool visualFaceTracking = runtimeSettings.RequestsVisualFaceTracking;
        bool audioFaceTracking = runtimeSettings.RequestsAudioFaceTracking;
        bool enableFaceTrackingVisemesOutput = runtimeSettings.EnableFaceTrackingVisemesOutput;
        // Face Tracking settings
        if (projectConfig.faceTrackingSupport == OVRProjectConfig.FeatureSupport.None)
        {
            ShowQuestFeaturesHelpBox("Face Tracking Support must be \"Required\" or \"Supported\" in the General section of Quest Features to enable Face Tracking.", highlightLabel: OVRProjectConfigEditor.HighlightLabel.FaceTrackingSupport);
        }

        EditorGUI.BeginDisabledGroup(projectConfig.faceTrackingSupport == OVRProjectConfig.FeatureSupport.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Face Tracking Data Sources", EditorStyles.boldLabel);

        bool dataSourceSelected = visualFaceTracking || audioFaceTracking;
        // Show the warning under the foldout, even if the foldout is collapsed.
        if (!dataSourceSelected)
        {
            EditorGUILayout.HelpBox(
                "Please specify at least one face tracking data source. If you don't choose, all available data sources will be chosen.",
                MessageType.Warning, true);
        }

        // Warn about face tracking when simultaneous hands and controllers is enabled as an incompatibility exists
        // for Quest 2 headsets
        if (manager.SimultaneousHandsAndControllersEnabled == true)
        {
            EditorGUILayout.HelpBox(
                "For Quest 2, simultaneous hands and controllers are not supported together with face tracking. Please select only one of these features if shipping on Quest 2",
                MessageType.Warning, true);
        }

        OVREditorUtil.SetupBoolField(
            target,
            new GUIContent("Visual", "Estimate face expressions with visual or audiovisual data."),
            ref visualFaceTracking,
            ref modified);
        OVREditorUtil.SetupBoolField(
            target,
            new GUIContent("Audio", "Estimate face expressions using audio data only."),
            ref audioFaceTracking,
            ref modified);

        EditorGUILayout.Space();
        OVREditorUtil.SetupBoolField(
            target,
            new GUIContent("Enable visemes",
                "Enabling visemes allows you to get face expressions in the form of Visemes in addition to blendshape weights. If it's not enabled, visemes will not be available."),
            ref enableFaceTrackingVisemesOutput,
            ref modified);

        EditorGUI.EndDisabledGroup();

        if (modified)
        {
            runtimeSettings.RequestsVisualFaceTracking = visualFaceTracking;
            runtimeSettings.RequestsAudioFaceTracking = audioFaceTracking;
            runtimeSettings.EnableFaceTrackingVisemesOutput = enableFaceTrackingVisemesOutput;
            OVRRuntimeSettings.CommitRuntimeSettings(runtimeSettings);
        }

        EndExpandSection();
    }

    private void DrawInsightPassthroughSection(OVRManager manager, OVRProjectConfig projectConfig, ref bool modified)
    {
        if (!BeginExpandSection(EditorSection.InsightPassthrough, "Insight Passthrough & Guardian Boundary"))
        {
            EndExpandSection();
            return;
        }

        GUIContent enablePassthroughContent;
        if (_activeTargetPlatform == TargetPlatform.Quest)
        {
            bool passthroughCapabilityEnabled =
                projectConfig.insightPassthroughSupport != OVRProjectConfig.FeatureSupport.None;

            if (!passthroughCapabilityEnabled)
            {
                ShowQuestFeaturesHelpBox("Passthrough Support must be \"Required\" or \"Supported\" in the General section of Quest Features to enable Passthrough.", highlightLabel: OVRProjectConfigEditor.HighlightLabel.InsightPassthroughSupport);
            }

            EditorGUI.BeginDisabledGroup(!passthroughCapabilityEnabled);
            enablePassthroughContent = new GUIContent("Enable Passthrough",
                "Enables passthrough functionality for the scene. Can be toggled at runtime. Passthrough Capability must be enabled in Quest Features.");
        }
        else
        {
            enablePassthroughContent = new GUIContent("Enable Passthrough",
                "Enables passthrough functionality for the scene. Can be toggled at runtime.");
        }
        OVREditorUtil.SetupBoolField(target, enablePassthroughContent, ref manager.isInsightPassthroughEnabled,
            ref modified);

        bool prevIsPassthroughCameraAccessEnabled = projectConfig.isPassthroughCameraAccessEnabled;
        OVREditorUtil.SetupBoolField(target, new GUIContent("Enable Passthrough Camera Access",
            "Use 'PassthroughCameraAccess' component from 'Mixed Reality Utility Kit (MRUK)' package to access passthrough camera."), ref projectConfig.isPassthroughCameraAccessEnabled, ref modified);
        if (prevIsPassthroughCameraAccessEnabled != projectConfig.isPassthroughCameraAccessEnabled)
        {
            EditorUtility.SetDirty(projectConfig);
        }

        if (_activeTargetPlatform == TargetPlatform.Quest)
        {
            EditorGUI.EndDisabledGroup();
        }

        var boundaryCapabilityEnabled =
            projectConfig.boundaryVisibilitySupport != OVRProjectConfig.FeatureSupport.None;

        var canModifyBoundary = boundaryCapabilityEnabled && manager.isInsightPassthroughEnabled;
        if (!canModifyBoundary)
            manager.shouldBoundaryVisibilityBeSuppressed = false;

        using (new EditorGUI.DisabledScope(!canModifyBoundary))
        {
            var content = new GUIContent("Should Boundary Visibility Be Suppressed",
                "Request that the Guardian Boundary Visibility be suppressed. " +
                "Can only be suppressed when Passthrough is enabled, and can " +
                "therefore differ from the system boundary state. There can " +
                "be a delay when setting this due to the delay in Passthrough startup. " +
                "Boundary Visibility Capability must be enabled in Quest Features.");
            OVREditorUtil.SetupBoolField(target, content, ref manager.shouldBoundaryVisibilityBeSuppressed, ref modified);
        }

        // actual boundary state (readonly)
        using (new EditorGUI.DisabledScope(true))
        {
            var isBoundaryVisibilitySuppressed = manager.isBoundaryVisibilitySuppressed;
            OVREditorUtil.SetupBoolField(target, new GUIContent("Is Boundary Visibility Suppressed",
                "The system state of the Guardian Boundary Visibility which may differ " +
                "from the requested state."),
                ref isBoundaryVisibilitySuppressed, ref modified);
        }

        EndExpandSection();
    }


    private void DrawDynamicResolutionSection(OVRManager manager, ref bool modified)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Dynamic Resolution", EditorStyles.boldLabel);
        bool dynamicResolutionAvailable = true;

        if (OVREditorUtil.unityVersion < new Version(2022, 3, 49))
        {
            EditorGUILayout.HelpBox(
                "When using OpenXR Plugin, Dynamic Resolution is not available until Unity 2022.3.49f1 or newer.",
                manager.enableDynamicResolution ? MessageType.Warning : MessageType.Info,
                wide: true
            );
            dynamicResolutionAvailable = false;
        }
#if !USING_XR_SDK_OPENXR && (!OCULUS_XR_3_3_0_OR_NEWER || !UNITY_2021_1_OR_NEWER)
        if (!PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android))
        {
            UnityEngine.Rendering.GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (apis.Length >= 1 && apis[0] == UnityEngine.Rendering.GraphicsDeviceType.Vulkan)
            {
                EditorGUILayout.HelpBox(
                "Vulkan Dynamic Resolution is not supported on your current build version. Ensure you are on Unity 2021+ with Oculus XR plugin v3.3.0+",
                manager.enableDynamicResolution ? MessageType.Warning : MessageType.Info,
                wide: true);
                dynamicResolutionAvailable = false;
            }
        }
#endif

        bool enableDynamicResolution = manager.enableDynamicResolution && dynamicResolutionAvailable;

        EditorGUI.BeginDisabledGroup(!dynamicResolutionAvailable);
        OVREditorUtil.SetupBoolField(target, "Enable Dynamic Resolution", ref enableDynamicResolution, ref modified, "https://developers.meta.com/horizon/documentation/unity/dynamic-resolution-unity");
        manager.enableDynamicResolution = enableDynamicResolution;
        if (manager.enableDynamicResolution)
        {
            OVREditorUtil.SetupRangeSlider(target, new GUIContent("Quest 2/Pro Range", "Quest2/Pro resolution scaling factor range when dynamic resolution is enabled."), ref manager.quest2MinDynamicResolutionScale, ref manager.quest2MaxDynamicResolutionScale, 0.7f, 2.0f, ref modified);
            OVREditorUtil.SetupRangeSlider(target, new GUIContent("Quest 3/3S Range", "Quest3/3S Resolution scaling factor range when dynamic resolution is enabled."), ref manager.quest3MinDynamicResolutionScale, ref manager.quest3MaxDynamicResolutionScale, 0.7f, 2.0f, ref modified);
        }
        EditorGUI.EndDisabledGroup();
    }


    private void DrawPermissionRequestsSection(OVRManager manager, OVRProjectConfig projectConfig, ref bool modified)
    {
        if (_activeTargetPlatform != TargetPlatform.Quest)
        {
            return;
        }

        if (!BeginExpandSection(EditorSection.PermissionRequest, "Permission Requests On Startup"))
        {
            EndExpandSection();
            return;
        }

        ShowQuestFeaturesHelpBox("Permissions can only be enabled when the relevant features are enabled in Quest Features");

        // helper function for all permission requests
        static void AddPermissionGroup(bool featureEnabled, string permissionName, string capabilityName, SerializedProperty property)
        {
            using (new EditorGUI.DisabledScope(!featureEnabled))
            {
                if (!featureEnabled)
                {
                    // disable if the Quest Features doesn't have support for it
                    if (property.boolValue == true)
                        property.boolValue = false;
                }

                var label = new GUIContent(permissionName,
                    $"Requests {permissionName} permission on start up. " +
                    $"{capabilityName} Capability must be enabled in Quest Features. " +
                    "It is recommended to manage runtime permissions yourself, " +
                    "and to only request them when needed."
                    );
                EditorGUILayout.PropertyField(property, label);
            }
        }

        AddPermissionGroup(projectConfig.bodyTrackingSupport != OVRProjectConfig.FeatureSupport.None,
            "Body Tracking", "Body Tracking", _requestBodyTrackingPermissionOnStartup);
        AddPermissionGroup(projectConfig.eyeTrackingSupport != OVRProjectConfig.FeatureSupport.None,
            "Eye Tracking", "Eye Tracking", _requestEyeTrackingPermissionOnStartup);
        AddPermissionGroup(projectConfig.faceTrackingSupport != OVRProjectConfig.FeatureSupport.None,
            "Face Tracking", "Face Tracking", _requestFaceTrackingPermissionOnStartup);
        AddPermissionGroup(projectConfig.faceTrackingSupport != OVRProjectConfig.FeatureSupport.None,
            "Record Audio for audio based Face Tracking", "Face Tracking", _requestRecordAudioPermissionOnStartup);
        AddPermissionGroup(projectConfig.sceneSupport != OVRProjectConfig.FeatureSupport.None,
            "Scene", "Scene", _requestScenePermissionOnStartup);
        AddPermissionGroup(projectConfig.isPassthroughCameraAccessEnabled,
            "Passthrough Camera Access", "Passthrough Camera Access", _requestPassthroughCameraAccessPermissionOnStartup);

        EndExpandSection();
    }

    private void DrawOvrMetricsSection(OVRManager manager, ref bool modified)
    {
        if (_activeTargetPlatform != TargetPlatform.Quest)
        {
            return;
        }

        if (!BeginExpandSection(EditorSection.ProfilingMetrics, "Profiling & Metrics"))
        {
            EndExpandSection();
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("OVR Metrics Tool", EditorStyles.boldLabel);
        GUIContent[] metricsOptions = { new GUIContent("Never"), new GUIContent("Development Only"), new GUIContent("Development + Release Builds") };
        int value = manager.enableCoreMetricsRelease ? 2 : manager.enableCoreMetricsDevelopment ? 1 : 0;

        EditorGUILayout.BeginHorizontal();
        OVREditorUtil.SetupPopupField(target, new GUIContent("Core Metric Reporting",
            "When enabled for a target, all core metrics will be sent to OVR Metrics Tool and may be recorded if the recording functionality is enabled."), ref value, metricsOptions,
            ref modified);
        OVREditorUtil.DisplayDocLink("https://developers.meta.com/horizon/documentation/unity/ts-ovrmetricstool");
        EditorGUILayout.EndHorizontal();

        manager.enableCoreMetricsDevelopment = value > 0;
        manager.enableCoreMetricsRelease = value > 1;

        if (manager.enableCoreMetricsRelease || manager.enableCoreMetricsDevelopment)
        {
#if UNITY_PROFILING
            if (manager.enableCoreMetricsRelease)
            {
                EditorGUILayout.HelpBox("Metrics submitted in release build will be visible to all OVR Metrics Tool Users.", MessageType.Info, true);
            }

            ref var visibilityConfig = ref manager.coreMetricVisibility;
            OVREditorUtil.SetupEnumFlagsField(target, new GUIContent("Displayed Memory Stats"),
                ref visibilityConfig.visibleMemoryMetricStats, ref modified);
            OVREditorUtil.SetupEnumFlagsField(target, new GUIContent("Displayed Memory Graphs"),
                ref visibilityConfig.visibleMemoryMetricGraphs, ref modified);

            const OVRMetricsCore.AppRenderMetric frameTimeMetrics =
                (OVRMetricsCore.AppRenderMetric.CPUTotalFrameTime | OVRMetricsCore.AppRenderMetric.GPUFrameTime |
                    OVRMetricsCore.AppRenderMetric.CPUMainThreadFrameTime |
                    OVRMetricsCore.AppRenderMetric.CPURenderThreadFrameTime);

            OVREditorUtil.SetupEnumFlagsField(target, new GUIContent("Displayed Render Stats"),
                ref visibilityConfig.visibleRenderMetricStats, ref modified);
            if ((visibilityConfig.visibleRenderMetricStats & frameTimeMetrics) != 0 && manager.enableCoreMetricsRelease)
            {
                EditorGUILayout.HelpBox("Frame Time Metrics are not available in Release Builds.", MessageType.Warning, true);
            }

            OVREditorUtil.SetupEnumFlagsField(target, new GUIContent("Displayed Render Graphs"),
                ref visibilityConfig.visibleRenderMetricGraphs, ref modified);
            if ((visibilityConfig.visibleRenderMetricGraphs & frameTimeMetrics) != 0 && manager.enableCoreMetricsRelease)
            {
                EditorGUILayout.HelpBox("Frame Time Metrics are not available in Release Builds.", MessageType.Warning, true);
            }
#else
            EditorGUILayout.HelpBox("The Unity Profiling Core API package is Required for Metric Recording. Please import com.unity.profiling.core to enable metrics!", MessageType.Warning, true);
#endif
        }

        EndExpandSection();
    }

}
