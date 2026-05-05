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
    [CustomEditor(typeof(OpenAIProvider))]
    public sealed class OpenAIProviderEditor : AIProviderEditorBase
    {
        private static readonly string[] Voices =
        {
            "alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse",
            CustomOption
        };

        private SerializedProperty _endpoint, _model;
        private SerializedProperty _supportsVision, _inlineRemoteImages, _visionDetail;
        private SerializedProperty _sttLanguage, _sttResponseFormat, _sttTemperature;
        private SerializedProperty _ttsVoice, _ttsResponseFormat, _ttsSpeed, _ttsInstructions;
        private bool _showChat, _showStt, _showTts;

        private void OnEnable()
        {
            InitializeCredentialStorage(nameof(OpenAIProvider.apiKey));
            _endpoint = serializedObject.FindProperty(nameof(OpenAIProvider.apiRoot));
            _model = serializedObject.FindProperty(nameof(OpenAIProvider.model));
            _supportsVision = serializedObject.FindProperty(nameof(OpenAIProvider.supportsVision));
            _inlineRemoteImages = serializedObject.FindProperty(nameof(OpenAIProvider.inlineRemoteImages));
            _visionDetail = serializedObject.FindProperty(nameof(OpenAIProvider.resolveRemoteRedirects));
            _sttLanguage = serializedObject.FindProperty(nameof(OpenAIProvider.sttLanguage));
            _sttResponseFormat = serializedObject.FindProperty(nameof(OpenAIProvider.sttResponseFormat));
            _sttTemperature = serializedObject.FindProperty(nameof(OpenAIProvider.sttTemperature));
            _ttsVoice = serializedObject.FindProperty(nameof(OpenAIProvider.ttsVoice));
            _ttsResponseFormat = serializedObject.FindProperty(nameof(OpenAIProvider.ttsOutputFormat));
            _ttsSpeed = serializedObject.FindProperty(nameof(OpenAIProvider.ttsSpeed));
            _ttsInstructions = serializedObject.FindProperty(nameof(OpenAIProvider.ttsInstructions));
            InitializeModelCache("OpenAI", OnFetchModels);
        }

        private void OnDisable()
        {
            CleanupValidationRequest();
        }

        protected override void OnTestConnection()
        {
            var provider = target as OpenAIProvider;
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
            EditorGUILayout.LabelField("OpenAI Provider", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            var provider = target as OpenAIProvider;
            if (provider is IUsesCredential credProvider)
            {
                var config = credProvider.GetTestConfig();
                TryLoadCachedValidation(config.Endpoint, config.Model, config.ProviderId);
            }

            DrawApiKeyField("API Key", "https://platform.openai.com/api-keys",
                drawExtraTopRight: () => DrawTestConnectionButton());
            EditorGUILayout.Space();
            Prop(_endpoint, "Endpoint");
            EditorGUILayout.Space();
            DrawModelPickerInline();
            EditorGUILayout.Space();
            DrawModelStatusBox();

            EditorGUILayout.Space(6);

            _showChat = EditorGUILayout.BeginFoldoutHeaderGroup(_showChat, "Chat / Vision Settings");
            if (_showChat)
            {
                Indent(() =>
                {
                    Prop(_supportsVision, "Supports Vision");
                    Prop(_inlineRemoteImages, "Inline Remote Images");
                    Prop(_visionDetail, "Resolve Remote Redirects");
                });
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            _showStt = EditorGUILayout.BeginFoldoutHeaderGroup(_showStt, "Speech-to-Text (STT)");
            if (_showStt)
            {
                Indent(() =>
                {
                    WideText(_sttLanguage, "Language (ISO, optional)");
                    EditorGUILayout.Space();
                    WideText(_sttResponseFormat, "Response Format (json/text/srt/verbose_json/vtt)");
                    EditorGUILayout.Space();
                    EditorGUILayout.PropertyField(_sttTemperature, new GUIContent("Temperature"));
                });
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            _showTts = EditorGUILayout.BeginFoldoutHeaderGroup(_showTts, "Text-to-Speech (TTS)");
            if (_showTts)
            {
                Indent(() =>
                {
                    DrawVoicePickerInline();
                    EditorGUILayout.Space();
                    WideText(_ttsResponseFormat, "Response Format (mp3/opus/aac/flac/wav/pcm)");
                    EditorGUILayout.Space();
                    EditorGUILayout.Slider(_ttsSpeed, 0.25f, 4f, new GUIContent("Speed"));
                    EditorGUILayout.Space();
                    WideTextArea(_ttsInstructions, "Instructions (tone/style)");
                });
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            serializedObject.ApplyModifiedProperties();
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

        private void DrawVoicePickerInline()
        {
            DrawDropdownPickerWithCustom(_ttsVoice, Voices, "Voice",
                "Select an OpenAI TTS voice or enter a custom voice name");
        }

        private void DrawModelPickerInline()
        {
            string[] fallbackModels =
            {
                "gpt-5",
                "gpt-4o",
                "gpt-4o-mini",
                "gpt-4-turbo",
                "gpt-4",
                "gpt-3.5-turbo",
                CustomOption
            };

            var availableModels = FetchedModels is { Count: > 0 } ? FetchedModels.ToArray() : fallbackModels;
            DrawDropdownPickerWithCustom(_model, availableModels, "Model",
                "Select a model or enter a custom model ID", OnFetchModels, IsFetchingModels);
        }

        private void OnFetchModels()
        {
            FetchModelsWithBearerAuth(_endpoint, "https://api.openai.com/v1", "OpenAI");
        }
    }
}
