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
    public class LlamaApiProviderEditor : UnityEditor.Editor
    {
        private SerializedProperty _endpointUrl;
        private SerializedProperty _apiKey;
        private SerializedProperty _model;
        private SerializedProperty _supportsVision;

        // Defaults (foldout)
        private SerializedProperty _temperature;
        private SerializedProperty _topP;
        private SerializedProperty _repetitionPenalty;
        private SerializedProperty _maxCompletionTokens;

        // Images (under same foldout)
        private SerializedProperty _inlineRemoteImages;
        private SerializedProperty _resolveRemoteRedirects;
        private SerializedProperty _maxInlineBytes;

        // Foldout states
        private bool _defaultsOpen;

        private struct ModelOpt
        {
            public string Display;
            public string ID;
            public bool Multimodal;
            public string Provider;
            public string CtxWindow;
        }

        private static readonly ModelOpt[] KModels = new[]
        {
            new ModelOpt
            {
                Display = "Llama-4-Maverick-17B-128E-Instruct-FP8 (Meta, multimodal)",
                ID = "Llama-4-Maverick-17B-128E-Instruct-FP8",
                Multimodal = true, Provider = "Meta", CtxWindow = "128k tokens"
            },
            new ModelOpt
            {
                Display = "Llama-4-Scout-17B-16E-Instruct-FP8 (Meta, multimodal)",
                ID = "Llama-4-Scout-17B-16E-Instruct-FP8",
                Multimodal = true, Provider = "Meta", CtxWindow = "128k tokens"
            },
            new ModelOpt
            {
                Display = "Llama-3.3-70B-Instruct (Meta, text-only)",
                ID = "Llama-3.3-70B-Instruct",
                Multimodal = false, Provider = "Meta", CtxWindow = "128k tokens"
            },
            new ModelOpt
            {
                Display = "Llama-3.3-8B-Instruct (Meta, text-only)",
                ID = "Llama-3.3-8B-Instruct",
                Multimodal = false, Provider = "Meta", CtxWindow = "128k tokens"
            },
            new ModelOpt
            {
                Display = "Cerebras-Llama-4-Maverick-17B-128E-Instruct (Cerebras, text-only, Preview)",
                ID = "Cerebras-Llama-4-Maverick-17B-128E-Instruct",
                Multimodal = false, Provider = "Cerebras", CtxWindow = "32k tokens"
            },
            new ModelOpt
            {
                Display = "Cerebras-Llama-4-Scout-17B-16E-Instruct (Cerebras, text-only, Preview)",
                ID = "Cerebras-Llama-4-Scout-17B-16E-Instruct",
                Multimodal = false, Provider = "Cerebras", CtxWindow = "32k tokens"
            },
            new ModelOpt
            {
                Display = "Groq-Llama-4-Maverick-17B-128E-Instruct (Groq, text-only, Preview)",
                ID = "Groq-Llama-4-Maverick-17B-128E-Instruct",
                Multimodal = false, Provider = "Groq", CtxWindow = "128k tokens"
            },
        };

        private const string KCustomOption = "Custom…";
        private int _selectedIndex = -1;

        private void OnEnable()
        {
            _apiKey = serializedObject.FindProperty("apiKey");
            _endpointUrl = serializedObject.FindProperty("endpointUrl");

            _model = serializedObject.FindProperty("model");
            _supportsVision = serializedObject.FindProperty("supportsVision");

            _temperature = serializedObject.FindProperty("temperature");
            _topP = serializedObject.FindProperty("topP");
            _repetitionPenalty = serializedObject.FindProperty("repetitionPenalty");
            _maxCompletionTokens = serializedObject.FindProperty("maxCompletionTokens");

            _inlineRemoteImages = serializedObject.FindProperty("inlineRemoteImages");
            _resolveRemoteRedirects = serializedObject.FindProperty("resolveRemoteRedirects");
            _maxInlineBytes = serializedObject.FindProperty("maxInlineBytes");

            _selectedIndex = ComputePopupIndexFromModel(_model?.stringValue);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Llama API Provider", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            DrawApiKeyRow(_apiKey, "API Key", "https://llama.developer.meta.com/api-keys/");
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_endpointUrl, new GUIContent("Endpoint URL",
                "Full chat completions endpoint, e.g. https://api.llama.com/v1/chat/completions"));
            EditorGUILayout.Space();

            // Build labels
            var popupLabels = new string[KModels.Length + 1];
            for (var i = 0; i < KModels.Length; i++) popupLabels[i] = KModels[i].Display;
            popupLabels[^1] = KCustomOption;

            // Make sure _selectedIndex is valid (do not recompute every frame from _model, only if out of range)
            if (_selectedIndex < 0 || _selectedIndex > KModels.Length)
                _selectedIndex = ComputePopupIndexFromModel(_model.stringValue);

            bool isCustom = (_selectedIndex == KModels.Length);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Model Selection");

                if (!isCustom)
                {
                    // Full-width popup when not custom
                    var newIndex = EditorGUILayout.Popup(_selectedIndex, popupLabels, GUILayout.ExpandWidth(true));
                    if (newIndex != _selectedIndex)
                    {
                        _selectedIndex = newIndex;
                        if (_selectedIndex >= 0 && _selectedIndex < KModels.Length)
                        {
                            _model.stringValue = KModels[_selectedIndex].ID;
                            _supportsVision.boolValue = KModels[_selectedIndex].Multimodal;
                        }

                        isCustom = (_selectedIndex == KModels.Length);
                    }
                }
                else
                {
                    // When custom: narrow popup + inline custom text field
                    var newIndex = EditorGUILayout.Popup(_selectedIndex, popupLabels, GUILayout.MaxWidth(280f));
                    if (newIndex != _selectedIndex)
                    {
                        _selectedIndex = newIndex;
                        isCustom = (_selectedIndex == KModels.Length);
                        if (!isCustom && _selectedIndex >= 0 && _selectedIndex < KModels.Length)
                        {
                            _model.stringValue = KModels[_selectedIndex].ID;
                            _supportsVision.boolValue = KModels[_selectedIndex].Multimodal;
                        }
                    }

                    GUILayout.Space(6);
                    _model.stringValue = EditorGUILayout.TextField(_model.stringValue ?? string.Empty,
                        GUILayout.ExpandWidth(true));
                }
            }

            DrawModelInfoHelpBox();
            EditorGUILayout.Space();

            _defaultsOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_defaultsOpen, "Chat / Vision Settings");
            if (_defaultsOpen)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    // Chat settings
                    EditorGUILayout.PropertyField(_temperature, new GUIContent("Temperature"));
                    EditorGUILayout.PropertyField(_topP, new GUIContent("Top P"));
                    EditorGUILayout.PropertyField(_repetitionPenalty, new GUIContent("Repetition Penalty"));
                    EditorGUILayout.PropertyField(_maxCompletionTokens, new GUIContent("Max Completion Tokens"));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Image Settings", EditorStyles.boldLabel);

                    EditorGUILayout.PropertyField(_supportsVision, new GUIContent("Supports Vision"));
                    // Image settings
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

        private void DrawModelInfoHelpBox()
        {
            if (_selectedIndex >= 0 && _selectedIndex < KModels.Length)
            {
                var m = KModels[_selectedIndex];
                var modalities = m.Multimodal ? "Input: Text, Image • Output: Text" : "Input: Text • Output: Text";
                EditorGUILayout.HelpBox(
                    $"{m.ID}\nProvider: {m.Provider} • Context: {m.CtxWindow}\n{modalities}",
                    MessageType.Info
                );
            }
            else
            {
                var current = string.IsNullOrEmpty(_model.stringValue) ? "(empty)" : _model.stringValue;
                EditorGUILayout.HelpBox(
                    $"Model ID: {current}\n(This is a custom value. Ensure it matches the provider’s catalog.)",
                    MessageType.None
                );
            }
        }

        private static int ComputePopupIndexFromModel(string currentId)
        {
            if (string.IsNullOrEmpty(currentId))
                return KModels.Length; // Custom

            for (var i = 0; i < KModels.Length; i++)
                if (KModels[i].ID == currentId)
                    return i;

            return KModels.Length; // not found => Custom
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
