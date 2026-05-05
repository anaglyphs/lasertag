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
    [CustomEditor(typeof(LlamaApiProvider))]
    public class LlamaApiProviderEditor : AIProviderEditorBase
    {
        private SerializedProperty _endpointUrl;
        private SerializedProperty _model;
        private SerializedProperty _supportsVision;
        private SerializedProperty _temperature;
        private SerializedProperty _topP;
        private SerializedProperty _repetitionPenalty;
        private SerializedProperty _maxCompletionTokens;
        private SerializedProperty _inlineRemoteImages;
        private SerializedProperty _resolveRemoteRedirects;
        private SerializedProperty _maxInlineBytes;
        private bool _defaultsOpen;

        private void OnEnable()
        {
            InitializeCredentialStorage(nameof(LlamaApiProvider.apiKey));
            _endpointUrl = serializedObject.FindProperty(nameof(LlamaApiProvider.endpointUrl));
            _model = serializedObject.FindProperty(nameof(LlamaApiProvider.model));
            _supportsVision = serializedObject.FindProperty(nameof(LlamaApiProvider.supportsVision));
            _temperature = serializedObject.FindProperty(nameof(LlamaApiProvider.temperature));
            _topP = serializedObject.FindProperty(nameof(LlamaApiProvider.topP));
            _repetitionPenalty = serializedObject.FindProperty(nameof(LlamaApiProvider.repetitionPenalty));
            _maxCompletionTokens = serializedObject.FindProperty(nameof(LlamaApiProvider.maxCompletionTokens));
            _inlineRemoteImages = serializedObject.FindProperty(nameof(LlamaApiProvider.inlineRemoteImages));
            _resolveRemoteRedirects = serializedObject.FindProperty(nameof(LlamaApiProvider.resolveRemoteRedirects));
            _maxInlineBytes = serializedObject.FindProperty(nameof(LlamaApiProvider.maxInlineBytes));
            InitializeModelCache("LlamaAPI", FetchModelsFromAPI);
        }

        private void OnDisable()
        {
            CleanupValidationRequest();
        }

        protected override void OnTestConnection()
        {
            var provider = target as LlamaApiProvider;
            if (provider is not IUsesCredential credentialProvider)
            {
                return;
            }

            var config = credentialProvider.GetTestConfig();
            TestConnection(config.Endpoint, config.Model, config.ProviderId);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Llama API Provider", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            var provider = target as LlamaApiProvider;
            if (provider is IUsesCredential credProvider)
            {
                var config = credProvider.GetTestConfig();
                TryLoadCachedValidation(config.Endpoint, config.Model, config.ProviderId);
            }

            DrawApiKeyField("API Key", "https://llama-api.com",
                drawExtraTopRight: () => DrawTestConnectionButton());

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_endpointUrl, new GUIContent("Endpoint",
                "Full chat completions endpoint, e.g. https://api.llama.com/v1/chat/completions"));
            EditorGUILayout.Space();

            DrawModelPickerInline();
            EditorGUILayout.Space();
            DrawModelStatusBox();
            EditorGUILayout.Space();

            _defaultsOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_defaultsOpen, "Chat / Vision Settings");
            if (_defaultsOpen)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.PropertyField(_temperature, new GUIContent("Temperature"));
                    EditorGUILayout.PropertyField(_topP, new GUIContent("Top P"));
                    EditorGUILayout.PropertyField(_repetitionPenalty, new GUIContent("Repetition Penalty"));
                    EditorGUILayout.PropertyField(_maxCompletionTokens, new GUIContent("Max Completion Tokens"));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Image Settings", EditorStyles.boldLabel);

                    EditorGUILayout.PropertyField(_supportsVision, new GUIContent("Supports Vision"));
                    EditorGUILayout.PropertyField(_inlineRemoteImages, new GUIContent("Inline Remote Images",
                        "When ON, http(s) image URLs are fetched locally and sent as base64."));
                    using (new EditorGUI.DisabledScope(_inlineRemoteImages.boolValue))
                    {
                        EditorGUILayout.PropertyField(_resolveRemoteRedirects, new GUIContent("Resolve Redirects",
                            "If NOT inlining, resolve redirects locally and send the final URL."));
                    }

                    EditorGUILayout.PropertyField(_maxInlineBytes, new GUIContent("Max Inline Bytes",
                        "Maximum bytes to download per image when inlining."));
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawModelPickerInline()
        {
            string[] fallbackModels =
            {
                "Llama-4-Maverick-17B-128E-Instruct-FP8",
                "Llama-3.3-70B-Instruct",
                CustomOption
            };

            var availableModels = FetchedModels is { Count: > 0 } ? FetchedModels.ToArray() : fallbackModels;
            DrawDropdownPickerWithCustom(_model, availableModels, "Model", "Select a model or enter a custom model ID",
                FetchModelsFromAPI, IsFetchingModels);
        }

        private void DrawModelStatusBox()
        {
            string modelInfo = null;
            if (FetchedModelData != null && FetchedModelData.TryGetValue(_model?.stringValue, out var modelData))
            {
                modelInfo = $"Model: {modelData.id}\nOwned by: {modelData.owned_by}";
            }

            DrawModelStatusBox(IsFetchingModels, FetchError, FetchedModels is { Count: > 0 }, _model?.stringValue,
                modelInfo);
        }

        private void FetchModelsFromAPI()
        {
            FetchModelsWithBearerAuth(_endpointUrl, "https://api.llama.com/v1", "LlamaAPI");
        }
    }
}
