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
    [CustomEditor(typeof(HuggingFaceProvider))]
    public class HuggingFaceProviderEditor : AIProviderEditorBase
    {
        private SerializedProperty _modelId;
        private SerializedProperty _endpoint;
        private SerializedProperty _supportsVision;
        private SerializedProperty _inlineRemoteImages;
        private SerializedProperty _resolveRemoteRedirects;
        private SerializedProperty _maxInlineBytes;

        private bool _visionOpen;

        private void OnEnable()
        {
            InitializeCredentialStorage(nameof(HuggingFaceProvider.apiKey));

            _modelId = serializedObject.FindProperty(nameof(HuggingFaceProvider.modelId));
            _endpoint = serializedObject.FindProperty(nameof(HuggingFaceProvider.endpoint));
            _supportsVision = serializedObject.FindProperty(nameof(HuggingFaceProvider.supportsVision));
            _inlineRemoteImages = serializedObject.FindProperty(nameof(HuggingFaceProvider.inlineRemoteImages));
            _resolveRemoteRedirects = serializedObject.FindProperty(nameof(HuggingFaceProvider.resolveRemoteRedirects));
            _maxInlineBytes = serializedObject.FindProperty(nameof(HuggingFaceProvider.maxInlineBytes));
        }

        private void OnDisable()
        {
            CleanupValidationRequest();
        }

        protected override void OnTestConnection()
        {
            var provider = target as HuggingFaceProvider;
            if (provider is IUsesCredential credentialProvider)
            {
                var config = credentialProvider.GetTestConfig();
                TestConnection(config.Endpoint, config.Model, config.ProviderId);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Hugging Face Provider", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Load cached validation result before drawing
            var provider = target as HuggingFaceProvider;
            if (provider is IUsesCredential credProvider)
            {
                var config = credProvider.GetTestConfig();
                TryLoadCachedValidation(config.Endpoint, config.Model, config.ProviderId);
            }

            DrawApiKeyField("API Key", "https://huggingface.co/settings/tokens",
                drawExtraTopRight: () => DrawTestConnectionButton());
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_endpoint, new GUIContent("Endpoint",
                "Examples:\n- HF Router: https://router.huggingface.co/hf-inference/models/<modelId>\n- OpenAI-style: https://api.groq.com/openai/v1/chat/completions"));

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_modelId, new GUIContent("Model ID",
                "e.g. meta-llama/Llama-3.2-11B-Vision-Instruct, facebook/detr-resnet-101, llama-3.3-70b-versatile"));

            EditorGUILayout.Space();
            DrawVisionSettingsFoldout(ref _visionOpen, _supportsVision, _inlineRemoteImages, _resolveRemoteRedirects, _maxInlineBytes);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
