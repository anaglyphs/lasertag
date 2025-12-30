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

using UnityEditor;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CustomEditor(typeof(ReplicateProvider))]
    public class ReplicateProviderEditor : UnityEditor.Editor
    {
        // Auth & Model
        private SerializedProperty _apiKey;
        private SerializedProperty _modelId;
        private SerializedProperty _supportsVision;

        // Endpoint Override (foldout)
        private SerializedProperty _overrideEndpointUrl;

        // Images (foldout)
        private SerializedProperty _sendBase64InsteadOfDataUri;
        private SerializedProperty _maxInlineBytes;

        // Foldout states
        private bool _endpointOpen;
        private bool _imagesOpen;
        private bool _debugOpen;

        private void OnEnable()
        {
            _apiKey = serializedObject.FindProperty("apiKey");
            _modelId = serializedObject.FindProperty("modelId");
            _supportsVision = serializedObject.FindProperty("supportsVision");
            _overrideEndpointUrl = serializedObject.FindProperty("overrideEndpointUrl");
            _sendBase64InsteadOfDataUri = serializedObject.FindProperty("sendBase64InsteadOfDataUri");
            _maxInlineBytes = serializedObject.FindProperty("maxInlineBytes");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("OpenAI Provider", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            DrawApiKeyRow(_apiKey, "API Key", "https://replicate.com/account/api-tokens");
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_modelId, new GUIContent("Model ID",
                    "Format: 'owner/model' or 'owner/model:version'. The endpoint is derived automatically unless overridden."));
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_supportsVision,
                new GUIContent("Supports Vision", "Enable if the selected model accepts image input."));
            EditorGUILayout.Space();

            _endpointOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_endpointOpen, "Endpoint Override");
            if (_endpointOpen)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.PropertyField(_overrideEndpointUrl, new GUIContent("Override Endpoint URL",
                        "Optional full predictions URL. If set, this is used verbatim instead of the default Replicate endpoint derived from Model ID."));
                    EditorGUILayout.HelpBox(
                        "Leave blank to use the standard Replicate endpoint built from the Model ID.\n" +
                        "Example (derived): https://api.replicate.com/v1/models/owner/model/predictions\n" +
                        "Example (with version): https://api.replicate.com/v1/models/owner/model/versions/<version>/predictions",
                        MessageType.None);
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            _imagesOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_imagesOpen, "Image Settings");
            if (_imagesOpen)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    if (_sendBase64InsteadOfDataUri != null)
                    {
                        EditorGUILayout.PropertyField(_sendBase64InsteadOfDataUri, new GUIContent(
                            "Send Base64 Instead of Data URI",
                            "If ON, sends raw base64 (image_base64); if OFF, sends a data URI (image). Choose what your model expects."));
                    }

                    EditorGUILayout.PropertyField(_maxInlineBytes, new GUIContent("Max Inline Bytes",
                        "Maximum allowed bytes when inlining images."));
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawApiKeyRow(SerializedProperty apiProp, string label, string getKeyUrl, float buttonWidth = 95f)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(apiProp, new GUIContent(label));
                GUILayout.Space(6);

                var val = apiProp?.stringValue?.Trim();
                if (string.IsNullOrEmpty(val) && !string.IsNullOrEmpty(getKeyUrl))
                {
                    if (GUILayout.Button(new GUIContent("Get Key…", $"Open {getKeyUrl}"), GUILayout.Width(buttonWidth)))
                    {
                        Application.OpenURL(getKeyUrl);
                    }
                }
            }
        }
    }
}
