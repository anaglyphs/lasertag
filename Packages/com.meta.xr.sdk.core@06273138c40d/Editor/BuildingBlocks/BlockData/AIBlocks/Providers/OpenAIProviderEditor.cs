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
    public sealed class OpenAIProviderEditor : UnityEditor.Editor
    {
        private const string CustomOption = "Custom …";

        private struct ModelInfo
        {
            public string Display; // human friendly
            public string ID; // API id
            public string Category; // Frontier / Open-weight / Specialized / Realtime & audio / ChatGPT
            public string Context; // e.g., "128k tokens" (when known) or "-"
            public bool Multimodal; // accepts images/audio (true) vs text-only (false)
        }

        private static readonly ModelInfo[] Catalog =
        {
            // Frontier / general chat
            new ModelInfo
            {
                Display = "GPT-5", ID = "gpt-5", Category = "Frontier", Context = "128k (varies)", Multimodal = true
            },
            new ModelInfo
            {
                Display = "GPT-5 mini", ID = "gpt-5-mini", Category = "Frontier", Context = "128k (varies)",
                Multimodal = true
            },
            new ModelInfo
                { Display = "GPT-5 nano", ID = "gpt-5-nano", Category = "Frontier", Context = "-", Multimodal = true },
            new ModelInfo
                { Display = "GPT-4.1", ID = "gpt-4.1", Category = "Frontier", Context = "128k", Multimodal = false },
            new ModelInfo
            {
                Display = "GPT-4.1 mini", ID = "gpt-4.1-mini", Category = "Frontier", Context = "128k",
                Multimodal = false
            },
            new ModelInfo
            {
                Display = "GPT-4.1 nano", ID = "gpt-4.1-nano", Category = "Frontier", Context = "-", Multimodal = false
            },
            new ModelInfo
                { Display = "GPT-4o", ID = "gpt-4o", Category = "Frontier", Context = "128k", Multimodal = true },
            new ModelInfo
            {
                Display = "GPT-4o mini", ID = "gpt-4o-mini", Category = "Frontier", Context = "128k", Multimodal = true
            },

            // Specialized
            new ModelInfo
            {
                Display = "o3 deep research", ID = "o3-deep-research", Category = "Specialized", Context = "-",
                Multimodal = false
            },
            new ModelInfo
            {
                Display = "o4 mini deep research", ID = "o4-mini-deep-research", Category = "Specialized",
                Context = "-", Multimodal = false
            },

            // Audio / Realtime / TTS / STT
            new ModelInfo
            {
                Display = "GPT-4o Transcribe", ID = "gpt-4o-transcribe", Category = "Audio/STT", Context = "-",
                Multimodal = true
            },
            new ModelInfo
            {
                Display = "GPT-4o mini Transcribe", ID = "gpt-4o-mini-transcribe", Category = "Audio/STT",
                Context = "-", Multimodal = true
            },
            new ModelInfo
            {
                Display = "GPT-4o mini TTS", ID = "gpt-4o-mini-tts", Category = "Audio/TTS", Context = "-",
                Multimodal = true
            },

            // Legacy/others (short list)
            new ModelInfo
                { Display = "Whisper", ID = "whisper-1", Category = "Audio/STT", Context = "-", Multimodal = true },
            new ModelInfo { Display = "TTS-1", ID = "tts-1", Category = "Audio/TTS", Context = "-", Multimodal = true },
            new ModelInfo
                { Display = "TTS-1 HD", ID = "tts-1-hd", Category = "Audio/TTS", Context = "-", Multimodal = true },
        };

        private static string[] BuildModelList()
        {
            var arr = new string[Catalog.Length + 1];
            for (int i = 0; i < Catalog.Length; i++) arr[i] = Catalog[i].ID;
            arr[arr.Length - 1] = CustomOption;
            return arr;
        }

        private static readonly string[] Models = BuildModelList();

        private static readonly string[] Voices =
        {
            "alloy", "ash", "ballad", "coral", "echo", "fable", "nova", "onyx", "sage", "shimmer", "verse",
            CustomOption
        };

        // General
        private SerializedProperty _apiKey, _endpoint, _model;

        // Chat
        private SerializedProperty _supportsVision, _inlineRemoteImages, _visionDetail;

        // STT
        private SerializedProperty _sttLanguage,
            _sttResponseFormat,
            _sttTemperature,
            _sttStream,
            _sttIncludeLogprobs,
            _sttTimestampGranularity,
            _sttPrompt,
            _sttChunkingStrategy;

        // TTS
        private SerializedProperty _ttsVoice, _ttsResponseFormat, _ttsSpeed, _ttsStreamFormat, _ttsInstructions;

        private bool _showChat, _showSTT, _showTTS;

        void OnEnable()
        {
            _apiKey = Find("apiKey");
            _endpoint = Find("apiRoot");
            _model = Find("model");

            _supportsVision = Find("supportsVision");
            _inlineRemoteImages = Find("inlineRemoteImages");
            _visionDetail = Find("resolveRemoteRedirects");

            _sttLanguage = Find("sttLanguage");
            _sttResponseFormat = Find("sttResponseFormat");
            _sttTemperature = Find("sttTemperature");
            _sttStream = Find("sttStream");
            _sttIncludeLogprobs = Find("sttIncludeLogprobs");
            _sttTimestampGranularity = Find("sttTimestampGranularity");
            _sttPrompt = Find("sttPrompt");
            _sttChunkingStrategy = Find("sttChunkingStrategy");

            _ttsVoice = Find("ttsVoice");
            _ttsResponseFormat = Find("ttsOutputFormat");
            _ttsSpeed = Find("ttsSpeed");
            _ttsStreamFormat = Find("ttsStreamFormat");
            _ttsInstructions = Find("ttsInstructions");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("OpenAI Provider", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            DrawApiKeyRow(_apiKey, "API Key", "https://platform.openai.com/api-keys");
            EditorGUILayout.Space();
            Prop(_endpoint, "Endpoint");
            EditorGUILayout.Space();

            DrawModelPickerInlineWithInfoAndAutoVision(); // <-- auto-toggles Supports Vision

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

            _showSTT = EditorGUILayout.BeginFoldoutHeaderGroup(_showSTT, "Speech-to-Text (STT)");
            if (_showSTT)
            {
                Indent(() =>
                {
                    WideText(_sttLanguage, "Language (ISO, optional)");
                    EditorGUILayout.Space();

                    WideText(_sttResponseFormat, "Response Format (json/text/srt/verbose_json/vtt)");
                    EditorGUILayout.Space();

                    EditorGUILayout.PropertyField(_sttTemperature, new GUIContent("Temperature"));

                    Prop(_sttStream, "Stream");
                    Prop(_sttIncludeLogprobs, "Include Logprobs (4o-transcribe json only)");

                    WideText(_sttTimestampGranularity, "Timestamp Granularity (segment/word; verbose_json)");
                    WideTextArea(_sttPrompt, "Prompt (optional)");
                    WideText(_sttChunkingStrategy, "Chunking Strategy (leave empty or 'auto')");
                });
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            EditorGUILayout.Space();

            _showTTS = EditorGUILayout.BeginFoldoutHeaderGroup(_showTTS, "Text-to-Speech (TTS)");
            if (_showTTS)
            {
                Indent(() =>
                {
                    DrawVoicePickerInline();
                    EditorGUILayout.Space();
                    WideText(_ttsResponseFormat, "Response Format (mp3/opus/aac/flac/wav/pcm)");
                    EditorGUILayout.Space();
                    EditorGUILayout.Slider(_ttsSpeed, 0.25f, 4f, new GUIContent("Speed"));
                    EditorGUILayout.Space();
                    WideText(_ttsStreamFormat, "Stream Format (audio/sse)");
                    WideTextArea(_ttsInstructions, "Instructions (tone/style)");
                });
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }

        void DrawModelPickerInlineWithInfoAndAutoVision()
        {
            var curr = _model?.stringValue ?? string.Empty;
            var idx = IndexOfOrCustom(curr, Models);
            bool isCustom = IsCustomIndex(idx, Models);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Model Selection");

                if (!isCustom)
                {
                    // Full-width popup when NOT custom
                    int newIdx = EditorGUILayout.Popup(idx, Models, GUILayout.ExpandWidth(true));
                    if (newIdx != idx && _model != null)
                    {
                        _model.stringValue = Models[newIdx] == CustomOption ? "" : Models[newIdx];
                        idx = newIdx;
                        isCustom = IsCustomIndex(idx, Models);
                        // Auto-toggle Supports Vision for known catalog entries
                        SyncSupportsVisionFromModelId(_model.stringValue);
                    }
                }
                else
                {
                    // When custom: narrow popup + inline text field that takes remaining width
                    int newIdx = EditorGUILayout.Popup(idx, Models, GUILayout.MaxWidth(260f));
                    if (newIdx != idx && _model != null)
                    {
                        _model.stringValue =
                            Models[newIdx] == CustomOption ? (_model.stringValue ?? "") : Models[newIdx];
                        idx = newIdx;
                        isCustom = IsCustomIndex(idx, Models);
                        if (!isCustom)
                        {
                            // Switched away from Custom => sync vision from selected known model
                            SyncSupportsVisionFromModelId(_model.stringValue);
                        }
                    }

                    GUILayout.Space(6);
                    var before = _model.stringValue ?? string.Empty;
                    var after = EditorGUILayout.TextField(before, GUILayout.ExpandWidth(true));
                    if (!ReferenceEquals(before, after))
                    {
                        _model.stringValue = after;
                        // If the typed custom value matches a known ID, auto-toggle Supports Vision
                        SyncSupportsVisionFromModelId(after);
                    }
                }
            }

            DrawOpenAIModelInfo(_model.stringValue);
        }

        void DrawOpenAIModelInfo(string modelId)
        {
            var i = FindOpenAIModel(modelId);
            if (i >= 0)
            {
                var m = Catalog[i];
                var modalities = m.Multimodal
                    ? "Input: Text, Image/Audio • Output: Text/Audio"
                    : "Input: Text • Output: Text";
                EditorGUILayout.HelpBox(
                    $"{m.ID}\nCategory: {m.Category} • Context: {m.Context}\n{modalities}",
                    MessageType.Info
                );
            }
            else
            {
                var current = string.IsNullOrEmpty(modelId) ? "(empty)" : modelId;
                EditorGUILayout.HelpBox(
                    $"Model ID: {current}\n(This is a custom value. Ensure it exists in your provider account.)",
                    MessageType.None
                );
            }
        }

        void DrawVoicePickerInline()
        {
            var curr = _ttsVoice?.stringValue ?? string.Empty;
            var idx = IndexOfOrCustom(curr, Voices);
            bool isCustom = IsCustomIndex(idx, Voices);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(
                    new GUIContent("Your Voices", "The voices you added to your ElevenLabs account"));

                if (!isCustom)
                {
                    int newIdx = EditorGUILayout.Popup(idx, Voices, GUILayout.ExpandWidth(true));
                    if (newIdx != idx && _ttsVoice != null)
                    {
                        _ttsVoice.stringValue = Voices[newIdx] == CustomOption ? "" : Voices[newIdx];
                    }
                }
                else
                {
                    int newIdx = EditorGUILayout.Popup(idx, Voices, GUILayout.MaxWidth(220f));
                    if (newIdx != idx && _ttsVoice != null)
                    {
                        _ttsVoice.stringValue = Voices[newIdx] == CustomOption
                            ? (_ttsVoice.stringValue ?? "")
                            : Voices[newIdx];
                        isCustom = IsCustomIndex(newIdx, Voices);
                    }

                    GUILayout.Space(6);
                    _ttsVoice.stringValue = EditorGUILayout.TextField(_ttsVoice.stringValue ?? string.Empty,
                        GUILayout.ExpandWidth(true));
                }
            }
        }

        void SyncSupportsVisionFromModelId(string modelId)
        {
            if (_supportsVision == null) return;
            int ci = FindOpenAIModel(modelId);
            if (ci >= 0)
            {
                _supportsVision.boolValue = Catalog[ci].Multimodal;
            }
            // If unknown custom, do not toggle — leave user choice intact.
        }

        int FindOpenAIModel(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < Catalog.Length; i++)
                if (Catalog[i].ID == id)
                    return i;
            return -1;
        }

        private SerializedProperty Find(string name)
        {
            try
            {
                return serializedObject.FindProperty(name);
            }
            catch
            {
                return null;
            }
        }

        static int IndexOfOrCustom(string value, string[] arr)
        {
            if (string.IsNullOrEmpty(value)) return arr.Length - 1; // default to Custom slot
            for (int i = 0; i < arr.Length - 1; i++)
                if (arr[i] == value)
                    return i;
            return arr.Length - 1; // not found => Custom
        }

        static bool IsCustomIndex(int idx, string[] arr) => idx >= 0 && idx == arr.Length - 1;

        static void Indent(System.Action draw)
        {
            using (new EditorGUI.IndentLevelScope()) draw?.Invoke();
        }

        static void Prop(SerializedProperty p, string label)
        {
            if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label));
        }

        static void WideText(SerializedProperty p, string label)
        {
            if (p == null) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                var rect = GUILayoutUtility.GetRect(360f, EditorGUIUtility.singleLineHeight,
                    GUILayout.ExpandWidth(true));
                p.stringValue = EditorGUI.TextField(rect, p.stringValue ?? string.Empty);
            }
        }

        static void WideTextArea(SerializedProperty p, string label, float minH = 60f)
        {
            if (p == null) return;
            EditorGUILayout.LabelField(label);
            var rect = GUILayoutUtility.GetRect(360f, minH, GUILayout.ExpandWidth(true));
            p.stringValue = EditorGUI.TextArea(rect, p.stringValue ?? string.Empty);
        }

        private static void DrawApiKeyRow(SerializedProperty apiProp, string label, string getKeyUrl,
            float buttonWidth = 95f)
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
