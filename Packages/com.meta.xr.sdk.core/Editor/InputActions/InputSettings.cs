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

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Meta.XR.Editor.UserInterface.Styles;
using static Meta.XR.Editor.UserInterface.Styles.Contents;
using static Meta.XR.Editor.UserInterface.Utils;
using static Meta.XR.Editor.UserInterface.Utils.ColorScope;

namespace Meta.XR.InputActions.Editor
{

    public class InputSettings : SettingsProvider
    {
        public const string SettingsName = "Input Actions";
        public static string SettingsPath => $"{OVRProjectSetupSettingsProvider.SettingsPath}/{SettingsName}";

        public static readonly string Description =
            "<b>Input Actions</b> are a way to define how inputs from certain devices such as the Logitech MX Ink Stylus are made available through the Meta Core SDK.\n" +
            "Actions are defined using the Open XR Action specification, where an Action describes how that particular action, e.g. a button press, could be retrieved from a particular controller.\n" +
            " - The Action name describes how the action can be accessed in code.\n" +
            " - The Interaction Profile identifies which device the action applies to, e.g. <i>/interaction_profiles/oculus/touch_controller</i> would indicate this action should be used if the attached device is a Meta Quest Touch controller.\n" +
            " - The Paths identify which input is to be returned from the device, e.g. <i>/user/hand/left/input/grip/pose</i> would indicate the action should return the grip pose of the left controller.\n\n" +
            "Multiple actions can exist with the same name so long as they have different Interaction Profiles. When that action name is queried the Open XR runtime will determine the right action to use based on which devices are attached.";

        public InputSettings(string path, SettingsScope scopes, IEnumerable<string> keywords = null) : base(path, scopes, keywords)
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateProjectValidationSettingsProvider()
        {
            return new InputSettings(SettingsPath, SettingsScope.Project);
        }

        public override bool HasSearchInterest(string searchContext)
        {
            return false;
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            base.OnActivate(searchContext, rootElement);
            _serializedSettings = new SerializedObject(RuntimeSettings.Instance);
            _runtimeSettings = _serializedSettings.FindProperty("InputActionDefinitions");
            _serializedActionSets = _serializedSettings.FindProperty("InputActionSets");
        }

        private SerializedProperty _runtimeSettings;
        private SerializedObject _serializedSettings;
        private SerializedProperty _serializedActionSets;

        public override void OnDeactivate()
        {
            base.OnDeactivate();
        }

        public override void OnFooterBarGUI()
        {
            base.OnFooterBarGUI();
        }

        public override void OnGUI(string searchContext)
        {
            ShowExperimentalNotice();

            EditorGUILayout.BeginHorizontal(GUIStyles.DialogBox);
            EditorGUILayout.LabelField(DialogIcon, GUIStyles.DialogIconStyle, GUILayout.Width(GUIStyles.DialogIconStyle.fixedWidth));
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(Description, GUIStyles.DialogTextStyle);
            EditorGUILayout.Space();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(_runtimeSettings);

            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(_serializedActionSets);

            _serializedSettings.ApplyModifiedProperties();

            EditorGUI.indentLevel--;
        }


        public override void OnInspectorUpdate()
        {
            base.OnInspectorUpdate();
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
    }

}
