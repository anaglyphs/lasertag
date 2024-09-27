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
using System.Collections.Generic;
using System.Linq;
using Meta.XR.Editor.StatusMenu;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Meta.XR.Editor.UserInterface.Styles;
using static Meta.XR.Editor.UserInterface.Styles.Contents;
using static Meta.XR.Editor.UserInterface.Utils;
using static Meta.XR.Editor.UserInterface.Utils.ColorScope;

namespace Meta.XR.ImmersiveDebugger.Editor
{
    internal class Settings : SettingsProvider
    {
        public const string SettingsName = Utils.PublicName;
        public static string SettingsPath => $"{OVRProjectSetupSettingsProvider.SettingsPath}/{SettingsName}";

        public static readonly string Description =
            "Displays the console and track Game Objects, MonoBehaviors and their members in real time within your headset." +
            "\n\nYou can track your components' members by using either of the following methods: " +
            $"\n• <i>In code:</i> Add the <b>{nameof(DebugMember)}</b> attribute to any member you want to track" +
            $"\n• <i>In scene:</i> Add and configure the <b>{nameof(DebugInspector)}</b> component to any GameObject you want to track";

        private const string PanelLayerName = "Panel Layer";
        private const string MeshLayerName = "Mesh Layer";

        private static readonly GUIContent LayersDescription = new GUIContent(
            $"<b>{SettingsName}</b> requires two layers :" +
            $"\n- <b>{PanelLayerName}</b> : Used to handle and renders the canvas into a Render Target." +
            $"\n- <b>{MeshLayerName}</b> : Used to render the mesh used for the overlay, will be passed to the Overlay Renderer." +
            "\nAt runtime in headset, those layers are only used as temporary rendering layers before <b>OVROverlayCanvas</b> renders the interface. Both layers should be culled by the cameras, because the rendering will occur on an overlay pass." +
            "\nAt runtime in editor, both layers need to be rendered by the main camera since there won't be any overlay pass." +
            "\n\nWe recommend to assign two layers that are not used by the project itself, and set <i>Automatically Update Culling Mask</i> on");

        private static Item.Origins? _lastOrigin = null;
        private bool _activated;

        private Settings(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
            : base(path, scopes, keywords)
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateProjectValidationSettingsProvider()
        {
            return new Settings(SettingsPath, SettingsScope.Project);
        }

        public override void OnTitleBarGUI()
        {
            Utils.Item.DrawHeaderFromSettingProvider();
        }

        private static readonly string InternalNotice =
            $"[Experimental] <b>{Utils.PublicName}</b> is currently an experimental feature.";

        private void ShowExperimentalNotice()
        {
            EditorGUILayout.BeginHorizontal(GUIStyles.ExperimentalNoticeBox);
            using (new ColorScope(Scope.Content, Colors.DarkGray))
            {
                EditorGUILayout.LabelField(ExperimentalIcon, GUIStyles.NoticeIconStyle,
                    GUILayout.Width(GUIStyles.NoticeIconStyle.fixedWidth));
            }

            EditorGUILayout.LabelField(InternalNotice, GUIStyles.ExperimentalNoticeTextStyle);
            EditorGUILayout.EndHorizontal();
        }

        public override void OnGUI(string searchContext)
        {
            ShowExperimentalNotice();

            EditorGUILayout.BeginHorizontal(GUIStyles.DialogBox);
            EditorGUILayout.LabelField(DialogIcon, GUIStyles.DialogIconStyle,
                GUILayout.Width(GUIStyles.DialogIconStyle.fixedWidth));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(Description, GUIStyles.DialogTextStyle);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUI.indentLevel++;

            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Styles.Constants.LabelWidth;

            DrawToggle(() => RuntimeSettings.Instance.ImmersiveDebuggerEnabled,
                (val) => RuntimeSettings.Instance.ImmersiveDebuggerEnabled = val,
                new GUIContent("Enable",
                    "Enable Immersive Debugger panels in runtime and allow collecting scripting attributes in Editor"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Behavior", GUIStyles.BoldLabel);
            DrawToggle(() => RuntimeSettings.Instance.ImmersiveDebuggerDisplayAtStartup,
                (val) => RuntimeSettings.Instance.ImmersiveDebuggerDisplayAtStartup = val,
                new GUIContent("Display at Start-up", "Display Immersive Debugger panel when the application starts"));
            DrawPopup(() => RuntimeSettings.Instance.ImmersiveDebuggerToggleDisplayButton,
                (val) => RuntimeSettings.Instance.ImmersiveDebuggerToggleDisplayButton = val,
                new GUIContent("Toggle Display Input Button",
                    "Customize the input button to show/hide the Immersive Debugger panel in runtime"));
            DrawToggle(() => RuntimeSettings.Instance.FollowOverride,
                (val) => RuntimeSettings.Instance.FollowOverride = val,
                new GUIContent("Follow Camera Rig Translation",
                    "Whether or not the Immersive Debugger panel follows the player by default"));
            DrawToggle(() => RuntimeSettings.Instance.RotateOverride,
                (val) => RuntimeSettings.Instance.RotateOverride = val,
                new GUIContent("Follow Camera Rig Rotation",
                    "Whether or not the Immersive Debugger panel rotates with the player by default"));
            DrawPopup(() => RuntimeSettings.Instance.PanelDistance, val => RuntimeSettings.Instance.PanelDistance = val,
                new GUIContent("Panel Distance", "Set default panel distance from VR camera on startup."));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Default Toggles", GUIStyles.BoldLabel);
            DrawToggle(() => RuntimeSettings.Instance.ShowInspectors,
                (val) => RuntimeSettings.Instance.ShowInspectors = val,
                new GUIContent("Show Inspectors", "Display Inspectors panel by default"));
            DrawToggle(() => RuntimeSettings.Instance.ShowConsole,
                (val) => RuntimeSettings.Instance.ShowConsole = val,
                new GUIContent("Show Console", "Display Console by default"));
            DrawToggle(() => RuntimeSettings.Instance.ShowInfoLog,
                (val) => RuntimeSettings.Instance.ShowInfoLog = val,
                new GUIContent("Show Info Logs in Console", "Show Info Logs in Console by default"));
            DrawToggle(() => RuntimeSettings.Instance.ShowWarningLog,
                (val) => RuntimeSettings.Instance.ShowWarningLog = val,
                new GUIContent("Show Warning Logs in Console", "Show Warning Logs in Console by default"));
            DrawToggle(() => RuntimeSettings.Instance.ShowErrorLog,
                (val) => RuntimeSettings.Instance.ShowErrorLog = val,
                new GUIContent("Show Error Logs in Console", "Show Error Logs in Console by default"));
            DrawToggle(() => RuntimeSettings.Instance.CollapsedIdenticalLogEntries,
                val => RuntimeSettings.Instance.CollapsedIdenticalLogEntries = val,
                new GUIContent("Collapse Identical Log Entries"));
            DrawIntField(() => RuntimeSettings.Instance.MaximumNumberOfLogEntries,
                (val) => RuntimeSettings.Instance.MaximumNumberOfLogEntries = val,
                new GUIContent("Maximum number of Log Entries", "Limits the number of Log Entries on the console"));

            var dataAssets = RuntimeSettings.Instance.InspectedDataAssets;
            if (dataAssets.Count != 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    new GUIContent("Inspected Data Sets", "A list of prebuilt dataset of inspected members of InspectedData scriptable object"),
                    GUIStyles.BoldLabel);
                var dataToggle = RuntimeSettings.Instance.InspectedDataEnabled;
                for (int i = 0; i < dataAssets.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    DrawToggle(() => dataToggle[i], val => dataToggle[i] = val,
                        new GUIContent(dataAssets[i].DisplayName));
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ObjectField(dataAssets[i], typeof(InspectedData), false, GUILayout.ExpandWidth(true));
                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Advanced", GUIStyles.BoldLabel);
            if (Foldout("Rendering", "Rendering"))
            {
                using (new IndentScope(EditorGUI.indentLevel + 1))
                {
                    var dialogRect = EditorGUILayout.BeginHorizontal(GUIStyles.DialogBox);
                    using (var color = new XR.Editor.UserInterface.Utils.ColorScope(XR.Editor.UserInterface.Utils.ColorScope.Scope.Content, Colors.Meta))
                    {
                        EditorGUILayout.LabelField(Meta.XR.Editor.UserInterface.Styles.Contents.InstructionsIcon, GUIStyles.DialogIconStyle, GUILayout.Width(GUIStyles.DialogIconStyle.fixedWidth));
                    }

                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField(LayersDescription, GUIStyles.DialogTextStyle);
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();

                    DrawLayerField(() => RuntimeSettings.Instance.PanelLayer,
                        val => RuntimeSettings.Instance.PanelLayer = val,
                        new GUIContent(PanelLayerName, "Set a layer for Immersive Debugger panels / objects in runtime"),
                        sendTelemetry: false);
                    DrawLayerField(() => RuntimeSettings.Instance.MeshRendererLayer,
                        val => RuntimeSettings.Instance.MeshRendererLayer = val,
                        new GUIContent(MeshLayerName, "Set a different layer for Immersive Debugger mesh renderer in runtime"),
                        sendTelemetry: false);
                    DrawToggle(() => RuntimeSettings.Instance.AutomaticLayerCullingUpdate,
                        (val) => RuntimeSettings.Instance.AutomaticLayerCullingUpdate = val,
                        new GUIContent("Automatically Update Culling Mask",
                            "The culling mask of the camera used by the Immersive Debugger is automatically updated to properly cull on/off the two layers."));
                    DrawTextField(() => RuntimeSettings.Instance.OverlayDepth.ToString(),
                        val => RuntimeSettings.Instance.OverlayDepth = int.Parse(val),
                        new GUIContent("Overlay Depth",
                            "Set a depth for the overlay canvas where Immersive Debugger panel is drawn in runtime"),
                        sendTelemetry: false);
                }
            }

            if (Foldout("Integration", "Integration"))
            {
                using (new IndentScope(EditorGUI.indentLevel + 1))
                {
                    DrawToggle(() => RuntimeSettings.Instance.CreateEventSystem,
                        (val) => RuntimeSettings.Instance.CreateEventSystem = val,
                        new GUIContent("Create Event System",
                            "As the Immersive Debugger requires an EventSystem, it can be automatically created if none was found."));

                    DrawToggle(() => RuntimeSettings.Instance.UseCustomIntegrationConfig,
                        (val) => RuntimeSettings.Instance.UseCustomIntegrationConfig = val,
                        new GUIContent("Use Custom Integration Config",
                            "Supply an implementation of CustomIntegrationConfigBase to define how Immersive Debugger works with the project"));
                    EditorGUI.BeginDisabledGroup(!RuntimeSettings.Instance.UseCustomIntegrationConfig);
                    DrawObjectField(
                        () => FindMonoScriptByTypeName(RuntimeSettings.Instance.CustomIntegrationConfigClassName),
                        (val) => RuntimeSettings.Instance.CustomIntegrationConfigClassName =
                            (val as MonoScript)?.GetClass()?.AssemblyQualifiedName,
                        new GUIContent("Custom Integration Config"), typeof(MonoScript), sendTelemetry: false);
                    EditorGUI.EndDisabledGroup();
                }
            }

            EditorGUIUtility.labelWidth = previousLabelWidth;
            EditorGUI.indentLevel--;
        }

        public static void OpenSettingsWindow(Item.Origins origin)
        {
            _lastOrigin = origin;
            SettingsService.OpenProjectSettings(SettingsPath);
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            if (_activated) return;

            _lastOrigin = _lastOrigin ?? Item.Origins.Settings;
            OVRTelemetry.Start(Telemetry.MarkerId.SettingsAccessed)
                .AddAnnotation(Telemetry.AnnotationType.Origin, _lastOrigin.ToString())
                .Send();

            _activated = true;
        }

        private void DrawToggle(Func<bool> get, Action<bool> set, GUIContent content, bool sendTelemetry = true)
        {
            DrawSetting(get, set, content, (guiContent, func) => EditorGUILayout.Toggle(guiContent, func.Invoke()),
                sendTelemetry);
        }

        private void DrawIntField(Func<int> get, Action<int> set, GUIContent content, bool sendTelemetry = true)
        {
            DrawSetting(get, set, content, (guiContent, func) => EditorGUILayout.IntField(guiContent, func.Invoke()),
                sendTelemetry);
        }

        private void DrawLayerField(Func<int> get, Action<int> set, GUIContent content, bool sendTelemetry = true)
        {
            DrawSetting(get, set, content, (guiContent, func) =>
                {
                    EditorGUILayout.BeginHorizontal();
                    var value = EditorGUILayout.IntSlider(guiContent, func.Invoke(), 0, 31);
                    EditorGUI.BeginChangeCheck();
                    var otherValue = EditorGUILayout.LayerField(func.Invoke());
                    if (EditorGUI.EndChangeCheck())
                    {
                        value = otherValue;
                    }
                    EditorGUILayout.EndHorizontal();
                    return value;
                },
                sendTelemetry);
        }

        private void DrawPopup<T>(Func<T> get, Action<T> set, GUIContent content, bool sendTelemetry = true)
            where T : Enum
        {
            DrawSetting(get, set, content,
                ((guiContent, getFunction) => (T)EditorGUILayout.EnumPopup(guiContent, getFunction.Invoke())),
                sendTelemetry);
        }

        private void DrawTextField(Func<string> get, Action<string> set, GUIContent content, bool sendTelemetry = true)
        {
            DrawSetting(get, set, content,
                ((guiContent, getFunction) => EditorGUILayout.TextField(guiContent, getFunction.Invoke())),
                sendTelemetry);
        }

        private void DrawObjectField(Func<UnityEngine.Object> get, Action<UnityEngine.Object> set, GUIContent content,
            Type type, bool sendTelemetry = true)
        {
            DrawSetting(get, set, content,
                ((guiContent, getFunction) =>
                    EditorGUILayout.ObjectField(guiContent, getFunction.Invoke(), type, false)), sendTelemetry);
        }

        private void DrawSetting<T>(Func<T> get, Action<T> set, GUIContent content,
            Func<GUIContent, Func<T>, T> editorGuiFunction, bool sendTelemetry = true)
        {
            EditorGUI.BeginChangeCheck();
            var value = editorGuiFunction.Invoke(content, get);
            if (EditorGUI.EndChangeCheck())
            {
                set.Invoke(value);

                if (sendTelemetry)
                {
                    OVRTelemetry.Start(Telemetry.MarkerId.SettingsChanged)
                        .AddAnnotation(Telemetry.AnnotationType.Type, content.text)
                        .AddAnnotation(Telemetry.AnnotationType.Value, value.ToString())
                        .Send();
                }

                EditorUtility.SetDirty(RuntimeSettings.Instance);
            }
        }

        private MonoScript FindMonoScriptByTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            var possibleFileName = Type.GetType(typeName)?.Name; // file name most likely the same to class name
            var guid = AssetDatabase.FindAssets(possibleFileName).FirstOrDefault();
            if (guid == null) // heavier if file not found by class name - iterate through MonoScript for the type
            {
                guid = AssetDatabase.FindAssets("t:MonoScript")
                    .FirstOrDefault(script => script.GetType().AssemblyQualifiedName == typeName);
            }
            return !string.IsNullOrEmpty(guid)
                ? AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid))
                : null;
        }
    }
}
