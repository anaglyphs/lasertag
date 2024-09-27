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

namespace Meta.XR.InputActions.Editor
{

    public class InputSettings : SettingsProvider
    {
        public const string SettingsName = "Input Actions";
        public static string SettingsPath => $"{OVRProjectSetupSettingsProvider.SettingsPath}/{SettingsName}";

        public static readonly string Description =
            "Define new <b>Actions</b> to get input from new devices and controllers.\nYou can also import <b>Action Sets</b> for Third Party controllers here.";

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
    }

}
