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

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CustomEditor(typeof(LlmAgentHelper))]
    public class LlmAgentHelperEditor : UnityEditor.Editor
    {
        private SerializedProperty _userInput,
            _selectedPrompt,
            _includeImage,
            _imageSource,
            _promptImage,
            _promptImageUrl,
            _useEditorFakeCameraPreview;

        private void OnEnable()
        {
            _userInput = serializedObject.FindProperty("userInput");
            _selectedPrompt = serializedObject.FindProperty("selectedPrompt");
            _includeImage = serializedObject.FindProperty("includeImage");
            _imageSource = serializedObject.FindProperty("imageSource");
            _promptImage = serializedObject.FindProperty("promptImage");
            _promptImageUrl = serializedObject.FindProperty("promptImageUrl");
            _useEditorFakeCameraPreview = serializedObject.FindProperty("useEditorFakeCameraPreview");
        }

        public override void OnInspectorGUI()
        {
            var helper = (LlmAgentHelper)target;
            serializedObject.Update();

            EditorGUILayout.PropertyField(_userInput, new GUIContent("User Input"));
            EditorGUILayout.PropertyField(_selectedPrompt, new GUIContent("Default Prompt"));

            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(_includeImage, new GUIContent("Include Image"));

            if (_includeImage.boolValue)
            {
                EditorGUILayout.PropertyField(_imageSource, new GUIContent("Image Source"));
                var src = (PromptImageSource)_imageSource.enumValueIndex;
                switch (src)
                {
                    case PromptImageSource.InspectorTexture:
                        EditorGUILayout.PropertyField(_promptImage, new GUIContent("Prompt Image"));
                        break;

                    case PromptImageSource.ImageUrl:
                        EditorGUILayout.PropertyField(_promptImageUrl, new GUIContent("Prompt Image URL"));
                        break;

                    case PromptImageSource.Camera:
                        if (_useEditorFakeCameraPreview != null)
                        {
                            EditorGUILayout.PropertyField(_useEditorFakeCameraPreview, new GUIContent("Use Editor Fake Camera"));
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Send Prompt"))
                {
                    helper.SendPrompt();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
