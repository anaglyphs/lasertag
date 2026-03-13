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

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System;
using System.Threading.Tasks;
using Meta.XR.Telemetry;
using UnityEngine.Networking;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CustomEditor(typeof(ElevenLabsProvider))]
    public sealed class ElevenLabsProviderEditor : AIProviderEditorBase
    {
        private SerializedProperty _endpoint;
        private SerializedProperty _model;

        private bool _sttOpen;
        private bool _ttsOpen;

        private SerializedProperty _sttLanguage;
        private SerializedProperty _sttIncludeAudioEvents;
        private SerializedProperty _voiceId;

        private const string VoicesPackedKeyPrefix = "EL_VOICES_PACKED_";

        private int _modelIndex = -1;
        private int _voiceIndex = -1;
        private string[] _voiceOptions = Array.Empty<string>();

        private List<VoiceMeta> _voiceMeta = new();
        private List<ElModelMeta> _modelMeta = new();

        private static readonly List<VoiceMeta> DefaultVoices = new()
        {
            new VoiceMeta()
            {
                voiceId = "21m00Tcm4TlvDq8ikWAM", name = "Rachel", labels = "gender=female, accent=american",
                description = ""
            },

            new VoiceMeta() { voiceId = "Custom…", name = "Custom…", labels = "", description = "" }
        };

        private void OnEnable()
        {
            InitializeCredentialStorage(nameof(ElevenLabsProvider.apiKey));

            _endpoint = serializedObject.FindProperty(nameof(ElevenLabsProvider.endpoint));
            _model = serializedObject.FindProperty(nameof(ElevenLabsProvider.model));
            _sttLanguage = serializedObject.FindProperty(nameof(ElevenLabsProvider.sttLanguage));
            _sttIncludeAudioEvents = serializedObject.FindProperty(nameof(ElevenLabsProvider.sttIncludeAudioEvents));
            _voiceId = serializedObject.FindProperty(nameof(ElevenLabsProvider.voiceId));

            _voiceOptions = DefaultVoices.ConvertAll(v => v.name).ToArray();

            _voiceIndex = IndexOfOrCustomId(_voiceOptions, _voiceId?.stringValue);

            InitializeModelCache("ElevenLabs", FetchModels);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("ElevenLabs Provider", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            var provider = target as ElevenLabsProvider;
            if (provider is IUsesCredential credProvider)
            {
                var config = credProvider.GetTestConfig();
                TryLoadCachedValidation(config.Endpoint, config.Model, config.ProviderId);
            }

            DrawApiKeyField("API Key", "https://elevenlabs.io/app/developers/api-keys",
                drawExtraTopRight: () => DrawTestConnectionButton());
            EditorGUILayout.Space();
            SafeProp(_endpoint, new GUIContent("Endpoint", "e.g. https://api.elevenlabs.io"));
            EditorGUILayout.Space();
            DrawModelPickerInline();
            DrawSelectedModelInfoBox();

            EditorGUILayout.Space();
            _sttOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_sttOpen, "Speech-to-Text (STT)");
            if (_sttOpen)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.HelpBox(
                        "For transcription with ElevenLabs, use the model: scribe_v1.\n" +
                        "Click the button below to set this asset's Model to scribe_v1.",
                        MessageType.Info);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("Set model to scribe_v1", GUILayout.Width(180)))
                        {
                            _model.stringValue = "scribe_v1";
                        }
                    }
                }

                EditorGUILayout.LabelField("Speech Settings", EditorStyles.boldLabel);
                if (_sttLanguage != null)
                    EditorGUILayout.PropertyField(_sttLanguage, new GUIContent("Force Language (ISO)"));
                if (_sttIncludeAudioEvents != null)
                    EditorGUILayout.PropertyField(_sttIncludeAudioEvents, new GUIContent("Include Audio Events"));
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space();
            _ttsOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_ttsOpen, "Text-to-Speech (TTS)");
            if (_ttsOpen)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    GUILayout.Label("Voice", EditorStyles.label);
                    DrawVoiceDropdown();

                    var meta = GetSelectedVoiceMeta();
                    if (meta != null && !IsCustom(meta.voiceId))
                    {
                        EditorGUILayout.Space(4);
                        using (new EditorGUILayout.VerticalScope("box"))
                        {
                            EditorGUILayout.LabelField("Selected Voice Details", EditorStyles.boldLabel);
                            InfoRow("Voice ID", meta.voiceId);
                            InfoRow("Name", string.IsNullOrEmpty(meta.name) ? "—" : meta.name);
                            InfoRow("Labels", string.IsNullOrEmpty(meta.labels) ? "—" : meta.labels);
                            InfoRow("Description", string.IsNullOrEmpty(meta.description) ? "—" : meta.description);

                            EditorGUILayout.Space(4);

                            using (new EditorGUILayout.HorizontalScope())
                            {
                                GUILayout.FlexibleSpace();

                                if (!string.IsNullOrEmpty(meta.previewUrl))
                                {
                                    if (GUILayout.Button("Preview", GUILayout.Width(90)))
                                    {
                                        Application.OpenURL(meta.previewUrl);
                                    }

                                    GUILayout.Space(6);
                                }

                                if (GUILayout.Button("Refresh Voices", GUILayout.Width(130)))
                                {
                                    FetchVoicesAsync();
                                }
                            }
                        }
                    }
                    else
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Refresh Voices", GUILayout.Width(130)))
                            {
                                FetchVoicesAsync();
                            }
                        }
                    }
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawModelPickerInline()
        {
            if (_model == null)
            {
                EditorGUILayout.HelpBox("Missing 'model' field on provider.", MessageType.Warning);
                return;
            }

            string[] fallbackModels = { "scribe_v1", "Custom…" };
            var availableModels = FetchedModels is { Count: > 0 } ? FetchedModels.ToArray() : fallbackModels;

            availableModels = EnsureContainsModel(availableModels, "scribe_v1");

            _modelIndex = IndexOfOrCustom(availableModels, _model?.stringValue);
            _modelIndex = Mathf.Clamp(_modelIndex, 0, availableModels.Length - 1);
            var isCustom = IsCustomIndex(availableModels, _modelIndex);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Model Selection");

                if (!isCustom)
                {
                    var newIndex = EditorGUILayout.Popup(_modelIndex, availableModels, GUILayout.ExpandWidth(true));
                    if (newIndex != _modelIndex)
                    {
                        _modelIndex = newIndex;
                        var selected = availableModels[Mathf.Clamp(_modelIndex, 0, availableModels.Length - 1)];
                        if (!IsCustom(selected))
                        {
                            _model.stringValue = selected;
                        }
                    }
                }
                else
                {
                    var newIndex = EditorGUILayout.Popup(_modelIndex, availableModels, GUILayout.MaxWidth(180f));
                    if (newIndex != _modelIndex)
                    {
                        _modelIndex = newIndex;
                        isCustom = IsCustomIndex(availableModels, _modelIndex);
                        if (!isCustom)
                        {
                            var selected = availableModels[Mathf.Clamp(_modelIndex, 0, availableModels.Length - 1)];
                            if (!IsCustom(selected))
                            {
                                _model.stringValue = selected;
                            }
                        }
                    }

                    GUILayout.Space(6);
                    var val = EditorGUILayout.TextField(_model.stringValue ?? string.Empty,
                        GUILayout.ExpandWidth(true));
                    if (val != _model.stringValue) _model.stringValue = val;
                }

                if (GUILayout.Button(new GUIContent("↻", "Fetch available models from ElevenLabs API"),
                        GUILayout.Width(25)))
                {
                    FetchModels();
                }
            }
        }

        private void DrawSelectedModelInfoBox()
        {
            var id = _model?.stringValue ?? string.Empty;
            if (string.IsNullOrEmpty(id))
            {
                EditorGUILayout.HelpBox(
                    "Model ID: (empty)\n(Enter or select a model. Click refresh to fetch from your ElevenLabs account.)",
                    MessageType.None);
                return;
            }

            var meta = FindModelMeta(id);
            if (meta == null)
            {
                EditorGUILayout.HelpBox(
                    $"Model ID: {id}\n(Custom value or not yet fetched. Click refresh to pull description & capabilities.)",
                    MessageType.None);
                return;
            }

            var modalities =
                (meta.can_do_text_to_speech == true ? "TTS" : null) +
                (meta.can_do_voice_conversion == true
                    ? (string.IsNullOrEmpty((meta.can_do_text_to_speech ?? false) ? "x" : null) ? "" : ", ") +
                      "Voice Conversion"
                    : "");

            if (string.IsNullOrEmpty(modalities))
            {
                modalities = "—";
            }

            var languages = meta.languages is { Count: > 0 }
                ? string.Join(", ", meta.languages)
                : "—";

            var maxChars = meta.maximum_text_length_per_request.HasValue
                ? $"{meta.maximum_text_length_per_request.Value:n0}"
                : "—";

            var concurrency = string.IsNullOrEmpty(meta.concurrency_group) ? "—" : meta.concurrency_group;

            var tokenCost = meta.token_cost_factor.HasValue ? $"{meta.token_cost_factor.Value:0.###}×" : "—";

            var desc = string.IsNullOrEmpty(meta.description) ? "—" : meta.description;

            EditorGUILayout.HelpBox(
                $"{meta.model_id} — {(string.IsNullOrEmpty(meta.name) ? "Unnamed" : meta.name)}\n" +
                $"Capabilities: {modalities}\n" +
                $"Languages: {languages}\n" +
                $"Max text per request: {maxChars}\n" +
                $"Concurrency group: {concurrency} • Cost factor: {tokenCost}\n" +
                $"Notes: {desc}",
                MessageType.Info
            );
        }

        private void DrawVoiceDropdown()
        {
            if (_voiceId == null)
            {
                EditorGUILayout.HelpBox("Missing 'voiceId' field on provider.", MessageType.Warning);
                return;
            }

            if (_voiceOptions == null || _voiceOptions.Length == 0)
            {
                _voiceMeta = new List<VoiceMeta>(DefaultVoices);
                _voiceOptions = BuildVoiceDisplay(_voiceMeta);
            }

            _voiceIndex = Mathf.Clamp(_voiceIndex < 0 ? _voiceOptions.Length - 1 : _voiceIndex, 0,
                _voiceOptions.Length - 1);

            var newIndex = EditorGUILayout.Popup(_voiceIndex, _voiceOptions, GUILayout.ExpandWidth(true));
            if (newIndex != _voiceIndex)
            {
                _voiceIndex = newIndex;

                var meta = GetSelectedVoiceMeta();
                if (meta != null && !IsCustom(meta.voiceId))
                {
                    _voiceId.stringValue = meta.voiceId;
                }
            }

            var current = GetSelectedVoiceMeta();
            if (current == null || !IsCustom(current.voiceId))
            {
                return;
            }

            EditorGUI.indentLevel++;
            var val = EditorGUILayout.TextField(new GUIContent("Custom Voice Id"), _voiceId.stringValue,
                GUILayout.ExpandWidth(true));
            if (val != _voiceId.stringValue) _voiceId.stringValue = val;
            EditorGUI.indentLevel--;
        }

        private void FetchModels()
        {
            var key = ApiKeyProperty?.stringValue ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                EditorUtility.DisplayDialog("ElevenLabs", "Please set your API Key first.", "OK");
                return;
            }

            FetchModelsFromAPIWithCache("https://api.elevenlabs.io/v1", "ElevenLabs",
                req => req.SetRequestHeader("xi-api-key", key),
                json =>
                {
                    var ids = ParseModelIds(json);
                    AddIfMissing(ids, "scribe_v1");

                    ParseModels(json, out var metas);
                    _modelMeta = metas ?? new List<ElModelMeta>();

                    return ids;
                });
        }

        private async void FetchVoicesAsync()
        {
            var key = ApiKeyProperty?.stringValue ?? string.Empty;
            var baseUrl = (_endpoint?.stringValue ?? "https://api.elevenlabs.io").TrimEnd('/');

            if (string.IsNullOrWhiteSpace(key))
            {
                EditorUtility.DisplayDialog("ElevenLabs", "Please set your API Key first.", "OK");
                return;
            }

            var url = baseUrl + "/v1/voices";
            try
            {
                EditorUtility.DisplayProgressBar("ElevenLabs", "Fetching voices…", 0.3f);
                using var req = UnityWebRequest.Get(url);
                req.SetRequestHeader("xi-api-key", key);

                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    await Task.Yield();
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception(req.error + "\n" + req.downloadHandler?.text);
                }

                ParseVoices(req.downloadHandler.text, out var metas);

                if (metas.Count == 0)
                {
                    metas = new List<VoiceMeta>(DefaultVoices);
                }
                else if (!IsCustom(metas[^1].voiceId))
                {
                    metas.Add(new VoiceMeta { voiceId = "Custom…", name = "Custom…", labels = "", description = "" });
                }

                _voiceMeta = metas;
                _voiceOptions = BuildVoiceDisplay(_voiceMeta);
                var assetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(target));
                SaveVoicesPackedToPrefs(VoicesPackedKeyPrefix + assetGuid, _voiceMeta);

                _voiceIndex = IndexOfOrCustomId(_voiceOptions, _voiceId?.stringValue);

                EditorApplication.delayCall += () =>
                {
                    if (this == null || target == null)
                    {
                        return;
                    }
                    Repaint();
                };
            }
            catch (Exception ex)
            {
                IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "elevenlabs-fetch-voices-failed",
                    $"[ElevenLabs] Failed to fetch voices: {ex.Message}");
                EditorUtility.DisplayDialog("ElevenLabs", "Failed to fetch voices. Check Console for details.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void SaveVoicesPackedToPrefs(string key, List<VoiceMeta> list)
        {
            try
            {
                EditorPrefs.SetString(key, string.Join("\n", list.ConvertAll(v => v.Pack())));
            }
            catch
            {
                // ignored
            }
        }

        private static void AddIfMissing(List<string> list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value)) return;
            if (list.Contains(value))
            {
                return;
            }

            var customIdx = list.IndexOf("Custom…");
            if (customIdx >= 0) list.Insert(customIdx, value);
            else list.Add(value);
        }

        private static void EnsureCustomSuffix(List<string> list)
        {
            if (list == null) return;
            if (list.Count == 0 || list[^1] != "Custom…")
            {
                list.Add("Custom…");
            }
        }

        private static void InfoRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth - 4));
                EditorGUILayout.SelectableLabel(value ?? "—", GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private static bool IsCustom(string s) =>
            !string.IsNullOrEmpty(s) && s.Equals("Custom…", StringComparison.Ordinal);

        private static bool IsCustomIndex(string[] arr, int idx) =>
            arr != null && idx >= 0 && idx < arr.Length && IsCustom(arr[idx]);

        private static int IndexOfOrCustom(string[] arr, string value)
        {
            if (arr == null || arr.Length == 0) return -1;
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == value)
                    {
                        return i;
                    }
                }
            }

            for (int i = 0; i < arr.Length; i++)
            {
                if (IsCustom(arr[i]))
                {
                    return i;
                }
            }
            return 0;
        }

        private static int IndexOfOrCustomId(string[] displays, string id)
        {
            if (displays == null || displays.Length == 0) return -1;
            if (!string.IsNullOrEmpty(id))
            {
                for (var i = 0; i < displays.Length; i++)
                {
                    if (displays[i] == id) return i;
                    if (displays[i].EndsWith($"({id})", StringComparison.Ordinal)) return i;
                }
            }

            for (int i = 0; i < displays.Length; i++)
            {
                if (IsCustom(displays[i]))
                {
                    return i;
                }
            }
            return 0;
        }

        private VoiceMeta GetSelectedVoiceMeta()
        {
            if (_voiceMeta == null || _voiceMeta.Count == 0) return null;
            var idx = Mathf.Clamp(_voiceIndex, 0, _voiceMeta.Count - 1);
            return _voiceMeta[idx];
        }

        private static void SafeProp(SerializedProperty prop, GUIContent label)
        {
            if (prop == null)
            {
                EditorGUILayout.HelpBox($"Missing '{label.text}' property on provider.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.PropertyField(prop, label);
            }
        }


        private static string[] EnsureContainsModel(string[] arr, string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return arr ?? Array.Empty<string>();
            if (arr == null || arr.Length == 0)
                return new[] { modelName, "Custom…" };

            foreach (var t in arr)
                if (t == modelName)
                {
                    return arr;
                }

            var list = new List<string>(arr);
            var customIdx = list.IndexOf("Custom…");
            if (customIdx >= 0) list.Insert(customIdx, modelName);
            else list.Add(modelName);
            return list.ToArray();
        }

        [Serializable]
        private class VoiceMeta
        {
            public string voiceId;
            public string name;
            public string labels;
            public string description;
            public string previewUrl;

            public string Pack()
            {
                return Escape(voiceId) + "|" + Escape(name) + "|" + Escape(labels) + "|" + Escape(description) + "|" +
                       Escape(previewUrl);
            }

            private static string Escape(string v) => string.IsNullOrEmpty(v) ? "" : v.Replace("|", "%7C");
            private static string Unescape(string v) => string.IsNullOrEmpty(v) ? "" : v.Replace("%7C", "|");
        }

        [Serializable]
        private class ElModelMeta
        {
            public string model_id;
            public string name;
            public bool? can_be_finetuned;
            public bool? can_do_text_to_speech;
            public bool? can_do_voice_conversion;
            public bool? can_use_style;
            public bool? can_use_speaker_boost;
            public bool? serves_pro_voices;
            public double? token_cost_factor;
            public string description;
            public bool? requires_alpha_access;
            public int? max_characters_request_free_user;
            public int? max_characters_request_subscribed_user;
            public int? maximum_text_length_per_request;
            public List<string> languages;
            public string concurrency_group;
        }

        private ElModelMeta FindModelMeta(string id)
        {
            if (string.IsNullOrEmpty(id) || _modelMeta == null || _modelMeta.Count == 0) return null;
            for (int k = 0; k < _modelMeta.Count; k++)
                if (string.Equals(_modelMeta[k].model_id, id, StringComparison.Ordinal))
                    return _modelMeta[k];
            return null;
        }

        private static string[] BuildVoiceDisplay(List<VoiceMeta> metas)
        {
            if (metas == null || metas.Count == 0) return Array.Empty<string>();
            var arr = new string[metas.Count];
            for (int i = 0; i < metas.Count; i++)
            {
                var m = metas[i];
                if (IsCustom(m.voiceId))
                {
                    arr[i] = "Custom…";
                }
                else
                {
                    var display = string.IsNullOrEmpty(m.name) ? m.voiceId : m.name;
                    if (!string.IsNullOrEmpty(m.labels))
                    {
                        display += $" ({m.labels})";
                    }

                    arr[i] = display;
                }
            }

            return arr;
        }

        private static List<string> ParseModelIds(string json)
        {
            var list = new List<string>(32);
            if (string.IsNullOrEmpty(json)) return list;

            int i = 0;
            while (i < json.Length)
            {
                var keyIdx = json.IndexOf("\"model_id\"", i, StringComparison.Ordinal);
                if (keyIdx < 0) break;
                var colon = json.IndexOf(':', keyIdx);
                var quote1 = json.IndexOf('"', colon + 1);
                var quote2 = json.IndexOf('"', quote1 + 1);
                if (colon < 0 || quote1 < 0 || quote2 < 0) break;

                var id = json.Substring(quote1 + 1, quote2 - quote1 - 1).Trim();
                if (!string.IsNullOrEmpty(id) && !list.Contains(id)) list.Add(id);
                i = quote2 + 1;
            }

            if (list.Count == 0) list.AddRange(new[] { "scribe_v1", "Custom…" });
            return list;
        }

        private static void ParseModels(string json, out List<ElModelMeta> metas)
        {
            metas = new List<ElModelMeta>(64);
            if (string.IsNullOrEmpty(json)) return;

            int i = 0;
            while (i < json.Length)
            {
                var midIdx = json.IndexOf("\"model_id\"", i, StringComparison.Ordinal);
                if (midIdx < 0) break;

                var model_id = ExtractStringValue(json, midIdx);
                i = midIdx + 9;

                var nameIdx = json.IndexOf("\"name\"", i, StringComparison.Ordinal);
                var name = (nameIdx >= 0) ? ExtractStringValue(json, nameIdx) : null;

                bool? can_be_finetuned = ExtractBoolNullable(json, "\"can_be_finetuned\"", i);
                bool? can_do_tts = ExtractBoolNullable(json, "\"can_do_text_to_speech\"", i);
                bool? can_do_vc = ExtractBoolNullable(json, "\"can_do_voice_conversion\"", i);
                bool? can_use_style = ExtractBoolNullable(json, "\"can_use_style\"", i);
                bool? can_use_boost = ExtractBoolNullable(json, "\"can_use_speaker_boost\"", i);
                bool? serves_pro = ExtractBoolNullable(json, "\"serves_pro_voices\"", i);

                double? token_cost_factor = ExtractDoubleNullable(json, "\"token_cost_factor\"", i);

                var descIdx = json.IndexOf("\"description\"", i, StringComparison.Ordinal);
                var description = (descIdx >= 0) ? ExtractStringValue(json, descIdx) : null;

                bool? requires_alpha = ExtractBoolNullable(json, "\"requires_alpha_access\"", i);

                int? max_chars_free = ExtractIntNullable(json, "\"max_characters_request_free_user\"", i);
                int? max_chars_subscribed = ExtractIntNullable(json, "\"max_characters_request_subscribed_user\"", i);
                int? max_text_length = ExtractIntNullable(json, "\"maximum_text_length_per_request\"", i);

                var langsIdx = json.IndexOf("\"languages\"", i, StringComparison.Ordinal);
                List<string> languages = new List<string>();
                if (langsIdx >= 0)
                {
                    var bracketStart = json.IndexOf('[', langsIdx);
                    var bracketEnd = json.IndexOf(']', bracketStart + 1);
                    if (bracketStart >= 0 && bracketEnd > bracketStart)
                    {
                        var langsBlock = json.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                        int p = 0;
                        while (p < langsBlock.Length)
                        {
                            var q1 = langsBlock.IndexOf('"', p);
                            if (q1 < 0) break;
                            var q2 = langsBlock.IndexOf('"', q1 + 1);
                            if (q2 < 0) break;
                            var lang = langsBlock.Substring(q1 + 1, q2 - q1 - 1).Trim();
                            if (!string.IsNullOrEmpty(lang)) languages.Add(lang);
                            p = q2 + 1;
                        }
                    }
                }

                var concurrIdx = json.IndexOf("\"concurrency_group\"", i, StringComparison.Ordinal);
                var concurrency_group = (concurrIdx >= 0) ? ExtractStringValue(json, concurrIdx) : null;

                metas.Add(new ElModelMeta
                {
                    model_id = model_id,
                    name = name,
                    can_be_finetuned = can_be_finetuned,
                    can_do_text_to_speech = can_do_tts,
                    can_do_voice_conversion = can_do_vc,
                    can_use_style = can_use_style,
                    can_use_speaker_boost = can_use_boost,
                    serves_pro_voices = serves_pro,
                    token_cost_factor = token_cost_factor,
                    description = description,
                    requires_alpha_access = requires_alpha,
                    max_characters_request_free_user = max_chars_free,
                    max_characters_request_subscribed_user = max_chars_subscribed,
                    maximum_text_length_per_request = max_text_length,
                    languages = languages,
                    concurrency_group = concurrency_group
                });
            }
        }

        private static void ParseVoices(string json, out List<VoiceMeta> metas)
        {
            metas = new List<VoiceMeta>(64);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            var i = 0;
            while (i < json.Length)
            {
                var vidIdx = json.IndexOf("\"voice_id\"", i, StringComparison.Ordinal);
                if (vidIdx < 0) break;

                var voiceId = ExtractStringValue(json, vidIdx);
                i = vidIdx + 9;

                var nameIdx = json.IndexOf("\"name\"", i, StringComparison.Ordinal);
                var name = (nameIdx >= 0) ? ExtractStringValue(json, nameIdx) : null;

                var descIdx = json.IndexOf("\"description\"", i, StringComparison.Ordinal);
                var desc = (descIdx >= 0) ? ExtractStringValue(json, descIdx) : null;

                var labelsIdx = json.IndexOf("\"labels\"", i, StringComparison.Ordinal);
                string labelsFlat = null;
                if (labelsIdx >= 0)
                {
                    int braceStart = json.IndexOf('{', labelsIdx);
                    if (braceStart > 0)
                    {
                        int braceEnd = FindMatchingBrace(json, braceStart);
                        if (braceEnd > braceStart)
                        {
                            var labelsBlock = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                            var parts = new List<string>();
                            int p = 0;
                            while (p < labelsBlock.Length)
                            {
                                var keyIdx = labelsBlock.IndexOf('"', p);
                                if (keyIdx < 0) break;
                                var keyEnd = labelsBlock.IndexOf('"', keyIdx + 1);
                                if (keyEnd < 0) break;
                                var key = labelsBlock.Substring(keyIdx + 1, keyEnd - keyIdx - 1);

                                var colonIdx = labelsBlock.IndexOf(':', keyEnd);
                                if (colonIdx < 0) break;
                                var valIdx = labelsBlock.IndexOf('"', colonIdx);
                                if (valIdx < 0) break;
                                var valEnd = labelsBlock.IndexOf('"', valIdx + 1);
                                if (valEnd < 0) break;
                                var val = labelsBlock.Substring(valIdx + 1, valEnd - valIdx - 1);

                                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                                {
                                    parts.Add($"{key}={val}");
                                }

                                p = valEnd + 1;
                            }

                            labelsFlat = string.Join(", ", parts);
                        }
                    }
                }

                var prevIdx = json.IndexOf("\"preview_url\"", i, StringComparison.Ordinal);
                var previewUrl = (prevIdx >= 0) ? ExtractStringValue(json, prevIdx) : null;

                metas.Add(new VoiceMeta
                {
                    voiceId = voiceId,
                    name = name,
                    labels = labelsFlat ?? "",
                    description = desc ?? "",
                    previewUrl = previewUrl ?? ""
                });
            }
        }

        private static string ExtractStringValue(string json, int keyIdx)
        {
            var colon = json.IndexOf(':', keyIdx);
            if (colon < 0) return null;
            var quote1 = json.IndexOf('"', colon + 1);
            if (quote1 < 0) return null;
            var quote2 = json.IndexOf('"', quote1 + 1);
            if (quote2 < 0) return null;
            return json.Substring(quote1 + 1, quote2 - quote1 - 1).Trim();
        }

        private static bool? ExtractBoolNullable(string json, string key, int start)
        {
            var idx = json.IndexOf(key, start, StringComparison.Ordinal);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            var p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\n')) p++;
            if (p + 4 <= json.Length && json.Substring(p, 4) == "true") return true;
            if (p + 5 <= json.Length && json.Substring(p, 5) == "false") return false;
            return null;
        }

        private static int? ExtractIntNullable(string json, string key, int start)
        {
            var idx = json.IndexOf(key, start, StringComparison.Ordinal);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            var p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\n')) p++;
            var q = p;
            while (q < json.Length && (char.IsDigit(json[q]) || json[q] == '-')) q++;
            if (int.TryParse(json.Substring(p, q - p), out int v)) return v;
            return null;
        }

        private static double? ExtractDoubleNullable(string json, string key, int start)
        {
            var idx = json.IndexOf(key, start, StringComparison.Ordinal);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            var p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\n')) p++;
            var q = p;
            while (q < json.Length && ("0123456789+-.eE".IndexOf(json[q]) >= 0)) q++;
            if (double.TryParse(json.Substring(p, q - p), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                return v;
            return null;
        }

        private static int FindMatchingBrace(string json, int openIdx)
        {
            var depth = 1;
            for (var i = openIdx + 1; i < json.Length; i++)
            {
                switch (json[i])
                {
                    case '{':
                        depth++;
                        break;
                    case '}':
                    {
                        depth--;
                        if (depth == 0) return i;
                        break;
                    }
                }
            }

            return -1;
        }

        private void OnDisable()
        {
            CleanupValidationRequest();
        }

        protected override void OnTestConnection()
        {
            var provider = target as ElevenLabsProvider;
            if (provider is not IUsesCredential credentialProvider)
            {
                return;
            }

            var config = credentialProvider.GetTestConfig();
            TestConnection(config.Endpoint, config.Model, config.ProviderId);
        }
    }
}
