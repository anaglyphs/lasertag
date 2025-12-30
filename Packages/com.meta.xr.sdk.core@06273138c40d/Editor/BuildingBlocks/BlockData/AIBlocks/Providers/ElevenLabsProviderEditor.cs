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
using System.Threading.Tasks;
using UnityEngine.Networking;
using UnityEditor;
using UnityEngine;
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CustomEditor(typeof(ElevenLabsProvider))]
    public class ElevenLabsProviderEditor : UnityEditor.Editor
    {
        // Top section
        private SerializedProperty _apiKey;
        private SerializedProperty _endpoint;
        private SerializedProperty _model;

        // STT & TTS foldouts
        private bool _sttOpen;
        private bool _ttsOpen;

        // STT specific
        private SerializedProperty _sttLanguage;
        private SerializedProperty _sttIncludeAudioEvents;

        // TTS voice
        private SerializedProperty _voiceId;

        // Editor-only cache in EditorPrefs (no provider changes needed)
        private string _assetGuid;
        private const string ModelsKeyPrefix = "EL_MODELS_";
        private const string VoicesPackedKeyPrefix = "EL_VOICES_PACKED_";

        // NEW: packed rich model metadata cache
        private const string ModelsPackedKeyPrefix = "EL_MODELS_PACKED_";

        // UI state
        private int _modelIndex = -1;
        private int _voiceIndex = -1;
        private string[] _modelOptions = Array.Empty<string>();

        // Voices: display options + rich metadata
        private string[] _voiceOptions = Array.Empty<string>();
        private List<VoiceMeta> _voiceMeta = new();
        private List<ElModelMeta> _modelMeta = new();

        private struct ModelInfo
        {
            public string ID; // API model id
            public string Category; // TTS / STT / Music
            public string Notes; // key capabilities: languages, latency, char limits
        }

        private static readonly ModelInfo[] ELModelCatalog =
        {
            // TTS (flagship)
            new ModelInfo
                { ID = "eleven_v3", Category = "TTS", Notes = "Most expressive; ~70+ languages; ~3,000 chars" },
            new ModelInfo
            {
                ID = "eleven_multilingual_v2", Category = "TTS", Notes = "High quality; 29 languages; ~10,000 chars"
            },
            new ModelInfo
            {
                ID = "eleven_flash_v2_5", Category = "TTS", Notes = "Ultra-fast (~75ms†); 32 languages; ~40,000 chars"
            },
            new ModelInfo
                { ID = "eleven_flash_v2", Category = "TTS", Notes = "Ultra-fast (~75ms†); English; ~30,000 chars" },
            new ModelInfo
            {
                ID = "eleven_turbo_v2_5", Category = "TTS",
                Notes = "Balanced quality/latency (~250–300ms†); 32 languages; ~40,000 chars"
            },
            new ModelInfo
            {
                ID = "eleven_turbo_v2", Category = "TTS", Notes = "Balanced quality/latency; English; ~30,000 chars"
            },

            // Voice design / speech-to-speech
            new ModelInfo
            {
                ID = "eleven_multilingual_sts_v2", Category = "STS", Notes = "Multilingual speech-to-speech; many langs"
            },
            new ModelInfo
                { ID = "eleven_multilingual_ttv_v2", Category = "TTV", Notes = "Multilingual text-to-voice design" },
            new ModelInfo { ID = "eleven_english_sts_v2", Category = "STS", Notes = "English speech-to-speech" },

            // STT
            new ModelInfo
                { ID = "scribe_v1", Category = "STT", Notes = "Transcription; ~99 languages; timestamps, diarization" },
            new ModelInfo
            {
                ID = "scribe_v1_experimental", Category = "STT",
                Notes = "Experimental STT; improved multilingual, fewer hallucinations"
            },

            // Music
            new ModelInfo
            {
                ID = "eleven_music", Category = "Music", Notes = "Studio-grade music generation; vocals or instrumental"
            },
        };

        private static int FindElModelIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < ELModelCatalog.Length; i++)
                if (ELModelCatalog[i].ID == id)
                    return i;
            return -1;
        }

        private static readonly string[] DefaultModelList =
        {
            "eleven_multilingual_v2",
            "eleven_turbo_v2_5",
            "eleven_flash_v2_5",
            "scribe_v1", // <- ensure present by default
            "scribe_v1_experimental",
            "Custom…"
        };

        // Minimal default voices (will be replaced after fetch)
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
            _apiKey = serializedObject.FindProperty("apiKey");
            _endpoint = serializedObject.FindProperty("endpoint");
            _model = serializedObject.FindProperty("model");
            _voiceId = serializedObject.FindProperty("voiceId");

            _sttLanguage = serializedObject.FindProperty("sttLanguage");
            _sttIncludeAudioEvents = serializedObject.FindProperty("sttIncludeAudioEvents");

            var path = AssetDatabase.GetAssetPath(target);
            _assetGuid = AssetDatabase.AssetPathToGUID(path);

            _modelOptions = LoadListFromPrefs(ModelsKeyPrefix + _assetGuid, DefaultModelList);
            _modelOptions = EnsureContainsModel(_modelOptions, "scribe_v1");

            // Load voices
            LoadVoicesPackedFromPrefs(VoicesPackedKeyPrefix + _assetGuid, DefaultVoices, out _voiceMeta,
                out _voiceOptions);

            // Load model metadata (if any cached)
            LoadModelsPackedFromPrefs(ModelsPackedKeyPrefix + _assetGuid, out _modelMeta);

            _modelIndex = IndexOfOrCustom(_modelOptions, _model?.stringValue);
            _voiceIndex = IndexOfOrCustomId(_voiceOptions, _voiceId?.stringValue);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("ElevenLabs Provider", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            DrawApiKeyRow(_apiKey, "API Key", "https://elevenlabs.io/app/developers/api-keys");
            EditorGUILayout.Space();
            SafeProp(_endpoint, new GUIContent("Endpoint", "e.g. https://api.elevenlabs.io"));
            EditorGUILayout.Space();
            DrawModelPickerInline(); // includes inline custom + sets _model
            DrawSelectedModelInfoBox(); // NEW: note below the field with provider info

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh Models", GUILayout.Width(130)))
                {
                    FetchModelsAsync(); // safe async
                }
            }

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
                            _modelOptions = EnsureContainsModel(_modelOptions, "scribe_v1");
                            _modelIndex = IndexOfOrCustom(_modelOptions, "scribe_v1");
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
                    DrawVoiceDropdown(); // sets _voiceId + shows custom field

                    // Extra details panel (for selected non-Custom voice)
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

                                // Show Preview only when we have a URL
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
                                    FetchVoicesAsync(); // safe async
                                }
                            }
                        }
                    }
                    else
                    {
                        // No details (e.g., "Custom…"): still offer Refresh in the same position line
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

            if (_modelOptions == null || _modelOptions.Length == 0)
                _modelOptions = DefaultModelList;

            // Ensure scribe_v1 is always present (even after cached loads)
            _modelOptions = EnsureContainsModel(_modelOptions, "scribe_v1");

            _modelIndex = Mathf.Clamp(_modelIndex < 0 ? _modelOptions.Length - 1 : _modelIndex, 0,
                _modelOptions.Length - 1);
            bool isCustom = IsCustomIndex(_modelOptions, _modelIndex);

            using (new EditorGUILayout.HorizontalScope())
            {
                // Prefix label in front of the dropdown (consistent with other providers)
                EditorGUILayout.PrefixLabel("Model Selection");

                if (!isCustom)
                {
                    // Full-width popup when NOT custom
                    int newIndex = EditorGUILayout.Popup(_modelIndex, _modelOptions, GUILayout.ExpandWidth(true));
                    if (newIndex != _modelIndex)
                    {
                        _modelIndex = newIndex;
                        var selected = _modelOptions[Mathf.Clamp(_modelIndex, 0, _modelOptions.Length - 1)];
                        if (!IsCustom(selected))
                            _model.stringValue = selected;
                    }
                }
                else
                {
                    // When custom: narrow popup + inline text field that takes remaining width
                    int newIndex = EditorGUILayout.Popup(_modelIndex, _modelOptions, GUILayout.MaxWidth(280f));
                    if (newIndex != _modelIndex)
                    {
                        _modelIndex = newIndex;
                        isCustom = IsCustomIndex(_modelOptions, _modelIndex);
                        if (!isCustom)
                        {
                            var selected = _modelOptions[Mathf.Clamp(_modelIndex, 0, _modelOptions.Length - 1)];
                            if (!IsCustom(selected))
                                _model.stringValue = selected;
                        }
                    }

                    GUILayout.Space(6);
                    var val = EditorGUILayout.TextField(_model.stringValue ?? string.Empty,
                        GUILayout.ExpandWidth(true));
                    if (val != _model.stringValue) _model.stringValue = val;
                }
            }
        }

        private void DrawSelectedModelInfoBox()
        {
            var id = _model?.stringValue ?? string.Empty;
            if (string.IsNullOrEmpty(id))
            {
                EditorGUILayout.HelpBox(
                    "Model ID: (empty)\n(Enter or select a model. Click “Refresh Models” to fetch from your ElevenLabs account.)",
                    MessageType.None);
                return;
            }

            var meta = FindModelMeta(id);
            if (meta == null)
            {
                // Fallback to small catalog hints or plain custom note
                var idx = FindElModelIndex(id);
                if (idx >= 0)
                {
                    var m = ELModelCatalog[idx];
                    EditorGUILayout.HelpBox($"{id}\nCategory: {m.Category}\n{m.Notes}", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"Model ID: {id}\n(Custom value. Click “Refresh Models” to pull description & capabilities.)",
                        MessageType.None);
                }

                return;
            }

            // Build concise, readable lines from metadata
            string modalities =
                (meta.can_do_text_to_speech == true ? "TTS" : null) +
                ((meta.can_do_voice_conversion == true
                    ? (string.IsNullOrEmpty((meta.can_do_text_to_speech ?? false) ? "x" : null) ? "" : ", ") +
                      "Voice Conversion"
                    : ""));

            if (string.IsNullOrEmpty(modalities)) modalities = "—";

            var languages = (meta.languages != null && meta.languages.Count > 0)
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

            int newIndex = EditorGUILayout.Popup(_voiceIndex, _voiceOptions, GUILayout.ExpandWidth(true));
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
            if (current != null && IsCustom(current.voiceId))
            {
                EditorGUI.indentLevel++;
                var val = EditorGUILayout.TextField(new GUIContent("Custom Voice Id"), _voiceId.stringValue,
                    GUILayout.ExpandWidth(true));
                if (val != _voiceId.stringValue) _voiceId.stringValue = val;
                EditorGUI.indentLevel--;
            }
        }

        private async void FetchModelsAsync()
        {
            var key = _apiKey?.stringValue ?? string.Empty;
            var baseUrl = (_endpoint?.stringValue ?? "https://api.elevenlabs.io").TrimEnd('/');

            if (string.IsNullOrWhiteSpace(key))
            {
                EditorUtility.DisplayDialog("ElevenLabs", "Please set your API Key first.", "OK");
                return;
            }

            var url = baseUrl + "/v1/models";
            try
            {
                EditorUtility.DisplayProgressBar("ElevenLabs", "Fetching models…", 0.3f);
                using var req = UnityWebRequest.Get(url);
                req.SetRequestHeader("xi-api-key", key);

                var op = req.SendWebRequest();
                while (!op.isDone) await Task.Yield(); // poll instead of awaiting the op

                if (req.result != UnityWebRequest.Result.Success)
                    throw new Exception(req.error + "\n" + req.downloadHandler?.text);

                var json = req.downloadHandler.text;

                // Update simple list (IDs) for the dropdown
                var ids = ParseModelIds(json);
                AddIfMissing(ids, "scribe_v1");
                EnsureCustomSuffix(ids);
                _modelOptions = ids.ToArray();
                SaveListToPrefs(ModelsKeyPrefix + _assetGuid, _modelOptions);
                _modelIndex = IndexOfOrCustom(_modelOptions, _model?.stringValue);

                // NEW: Parse and cache rich metadata for info box
                ParseModels(json, out var metas);
                _modelMeta = metas ?? new List<ElModelMeta>();
                SaveModelsPackedToPrefs(ModelsPackedKeyPrefix + _assetGuid, _modelMeta);

                EditorApplication.delayCall += () =>
                {
                    if (this == null || target == null) return;
                    Repaint();
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ElevenLabs Editor] Failed to fetch models: {ex.Message}");
                EditorUtility.DisplayDialog("ElevenLabs", "Failed to fetch models. Check Console for details.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private async void FetchVoicesAsync()
        {
            var key = _apiKey?.stringValue ?? string.Empty;
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
                while (!op.isDone) await Task.Yield();

                if (req.result != UnityWebRequest.Result.Success)
                    throw new Exception(req.error + "\n" + req.downloadHandler?.text);

                ParseVoices(req.downloadHandler.text, out var metas);

                if (metas.Count == 0) metas = new List<VoiceMeta>(DefaultVoices);
                else if (!IsCustom(metas[^1].voiceId))
                    metas.Add(new VoiceMeta { voiceId = "Custom…", name = "Custom…", labels = "", description = "" });

                _voiceMeta = metas;
                _voiceOptions = BuildVoiceDisplay(_voiceMeta);
                SaveVoicesPackedToPrefs(VoicesPackedKeyPrefix + _assetGuid, _voiceMeta);

                _voiceIndex = IndexOfOrCustomId(_voiceOptions, _voiceId?.stringValue);

                EditorApplication.delayCall += () =>
                {
                    if (this == null || target == null) return;
                    Repaint();
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ElevenLabs Editor] Failed to fetch voices: {ex.Message}");
                EditorUtility.DisplayDialog("ElevenLabs", "Failed to fetch voices. Check Console for details.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
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

            if (list.Count == 0) list.AddRange(DefaultModelList);
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

                int? max_free = ExtractIntNullable(json, "\"max_characters_request_free_user\"", i);
                int? max_sub = ExtractIntNullable(json, "\"max_characters_request_subscribed_user\"", i);
                int? max_len = ExtractIntNullable(json, "\"maximum_text_length_per_request\"", i);

                // languages array: [{"language_id":"xx","name":"..."}]
                var langs = ExtractLanguages(json, i);

                // model_rates.character_cost_multiplier (optional)
                double? char_mult = ExtractDoubleNullable(json, "\"character_cost_multiplier\"", i);

                var cgIdx = json.IndexOf("\"concurrency_group\"", i, StringComparison.Ordinal);
                var concurrency = (cgIdx >= 0) ? ExtractStringValue(json, cgIdx) : null;

                if (!string.IsNullOrEmpty(model_id))
                {
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
                        token_cost_factor = token_cost_factor ?? char_mult, // prefer top-level; fall back to rates
                        description = description,
                        requires_alpha_access = requires_alpha,
                        max_characters_request_free_user = max_free,
                        max_characters_request_subscribed_user = max_sub,
                        maximum_text_length_per_request = max_len,
                        languages = langs,
                        concurrency_group = concurrency
                    });
                }
            }
        }

        private static List<string> ExtractLanguages(string json, int startIdx)
        {
            var list = new List<string>();
            var langsIdx = json.IndexOf("\"languages\"", startIdx, StringComparison.Ordinal);
            if (langsIdx < 0) return list;

            int arrStart = json.IndexOf('[', langsIdx);
            if (arrStart < 0) return list;
            int depth = 0;
            int arrEnd = -1;
            for (int j = arrStart; j < json.Length; j++)
            {
                if (json[j] == '[') depth++;
                else if (json[j] == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        arrEnd = j;
                        break;
                    }
                }
            }

            if (arrEnd < 0) return list;
            var inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            // collect language "name" fields
            int k = 0;
            while (k < inner.Length)
            {
                var nameIdx = inner.IndexOf("\"name\"", k, StringComparison.Ordinal);
                if (nameIdx < 0) break;
                var name = ExtractStringValue(inner, nameIdx);
                if (!string.IsNullOrEmpty(name)) list.Add(name);
                k = nameIdx + 6;
            }

            return list;
        }

        private static bool? ExtractBoolNullable(string json, string key, int start)
        {
            var idx = json.IndexOf(key, start, StringComparison.Ordinal);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            var span = json.Substring(colon + 1).TrimStart();
            if (span.StartsWith("true")) return true;
            if (span.StartsWith("false")) return false;
            return null;
        }

        private static int? ExtractIntNullable(string json, string key, int start)
        {
            var idx = json.IndexOf(key, start, StringComparison.Ordinal);
            if (idx < 0) return null;
            var colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            int p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\n')) p++;
            int q = p;
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
            int p = colon + 1;
            while (p < json.Length && (json[p] == ' ' || json[p] == '\n')) p++;
            int q = p;
            while (q < json.Length && ("0123456789+-.eE".IndexOf(json[q]) >= 0)) q++;
            if (double.TryParse(json.Substring(p, q - p), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double v))
                return v;
            return null;
        }

        private static void ParseVoices(string json, out List<VoiceMeta> metas)
        {
            metas = new List<VoiceMeta>(64);
            if (string.IsNullOrEmpty(json))
            {
                Array.Empty<string>();
                return;
            }

            int i = 0;
            while (i < json.Length)
            {
                var vidIdx = json.IndexOf("\"voice_id\"", i, StringComparison.Ordinal);
                if (vidIdx < 0) break;

                var voiceId = ExtractStringValue(json, vidIdx);
                i = vidIdx + 9;

                // name (optional)
                var nameIdx = json.IndexOf("\"name\"", i, StringComparison.Ordinal);
                var name = (nameIdx >= 0) ? ExtractStringValue(json, nameIdx) : null;

                // description (optional)
                var descIdx = json.IndexOf("\"description\"", i, StringComparison.Ordinal);
                var desc = (descIdx >= 0) ? ExtractStringValue(json, descIdx) : null;

                // labels (optional object)
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
                            var inner = json.Substring(braceStart + 1, braceEnd - braceStart - 1);
                            labelsFlat = FlattenLabels(inner);
                        }
                    }
                }

                // preview_url (optional)
                var prevIdx = json.IndexOf("\"preview_url\"", i, StringComparison.Ordinal);
                var preview = (prevIdx >= 0) ? ExtractStringValue(json, prevIdx) : null;

                if (!string.IsNullOrEmpty(voiceId))
                {
                    metas.Add(new VoiceMeta
                    {
                        voiceId = voiceId,
                        name = name,
                        labels = labelsFlat,
                        description = desc,
                        previewUrl = preview
                    });
                }
            }

            BuildVoiceDisplay(metas);
        }

        private static string[] BuildVoiceDisplay(List<VoiceMeta> metas)
        {
            var arr = new string[metas.Count];
            for (int k = 0; k < metas.Count; k++)
            {
                var m = metas[k];
                if (IsCustom(m.voiceId))
                {
                    arr[k] = "Custom…";
                }
                else
                {
                    var title = string.IsNullOrEmpty(m.name) ? m.voiceId : $"{m.name} ({m.voiceId})";
                    arr[k] = title;
                }
            }

            return arr;
        }

        private static string ExtractStringValue(string json, int keyIdx)
        {
            int colon = json.IndexOf(':', keyIdx);
            if (colon < 0) return null;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return null;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return null;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        private static int FindMatchingBrace(string s, int openIdx)
        {
            int depth = 0;
            for (int j = openIdx; j < s.Length; j++)
            {
                if (s[j] == '{') depth++;
                else if (s[j] == '}')
                {
                    depth--;
                    if (depth == 0) return j;
                }
            }

            return -1;
        }

        private static string FlattenLabels(string inner)
        {
            var cleaned = inner.Replace("\"", "").Trim();
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", "");
            cleaned = cleaned.Replace(":", "=");
            return cleaned;
        }

        [Serializable]
        private class Wrapper
        {
            public string[] items;
        }

        private static void SaveListToPrefs(string key, string[] items)
        {
            try
            {
                var w = new Wrapper { items = items ?? Array.Empty<string>() };
                var json = JsonUtility.ToJson(w);
                EditorPrefs.SetString(key, json);
            }
            catch
            {
                // ignored
            }
        }

        private static string[] LoadListFromPrefs(string key, string[] fallback)
        {
            try
            {
                var json = EditorPrefs.GetString(key, "");
                if (string.IsNullOrEmpty(json)) return fallback;
                return JsonUtility.FromJson<Wrapper>(json)?.items ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static void SaveVoicesPackedToPrefs(string key, List<VoiceMeta> list)
        {
            try
            {
                var packed = new string[list.Count];
                for (int i = 0; i < list.Count; i++)
                    packed[i] = list[i].Pack();
                SaveListToPrefs(key, packed);
            }
            catch
            {
                // ignored
            }
        }

        private static void LoadVoicesPackedFromPrefs(string key, List<VoiceMeta> defaults, out List<VoiceMeta> metas,
            out string[] displays)
        {
            var packed = LoadListFromPrefs(key, null);
            if (packed == null || packed.Length == 0)
            {
                metas = new List<VoiceMeta>(defaults);
            }
            else
            {
                metas = new List<VoiceMeta>(packed.Length);
                foreach (var p in packed)
                {
                    var m = VoiceMeta.Unpack(p);
                    if (m != null) metas.Add(m);
                }

                if (metas.Count == 0) metas = new List<VoiceMeta>(defaults);
            }

            displays = BuildVoiceDisplay(metas);
        }

        private static void SaveModelsPackedToPrefs(string key, List<ElModelMeta> list)
        {
            try
            {
                var packed = new string[list.Count];
                for (int i = 0; i < list.Count; i++)
                    packed[i] = list[i].Pack();
                SaveListToPrefs(key, packed);
            }
            catch
            {
            }
        }

        private static void LoadModelsPackedFromPrefs(string key, out List<ElModelMeta> metas)
        {
            metas = new List<ElModelMeta>();
            var packed = LoadListFromPrefs(key, null);
            if (packed == null || packed.Length == 0) return;

            foreach (var p in packed)
            {
                var m = ElModelMeta.Unpack(p);
                if (m != null) metas.Add(m);
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
                    if (arr[i] == value)
                        return i;
            }

            for (int i = 0; i < arr.Length; i++)
                if (IsCustom(arr[i]))
                    return i;
            return 0;
        }

        private static int IndexOfOrCustomId(string[] displays, string id)
        {
            if (displays == null || displays.Length == 0) return -1;
            if (!string.IsNullOrEmpty(id))
            {
                for (int i = 0; i < displays.Length; i++)
                {
                    if (displays[i] == id) return i;
                    if (displays[i].EndsWith($"({id})", StringComparison.Ordinal)) return i;
                }
            }

            for (int i = 0; i < displays.Length; i++)
                if (IsCustom(displays[i]))
                    return i;
            return 0;
        }

        private VoiceMeta GetSelectedVoiceMeta()
        {
            if (_voiceMeta == null || _voiceMeta.Count == 0) return null;
            var idx = Mathf.Clamp(_voiceIndex, 0, _voiceMeta.Count - 1);
            return _voiceMeta[idx];
        }

        private static void SafeProp(SerializedProperty prop, string label)
        {
            if (prop == null)
            {
                EditorGUILayout.HelpBox($"Missing '{label}' property on provider.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.PropertyField(prop, new GUIContent(label));
            }
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

        private static void EnsureCustomSuffix(List<string> list)
        {
            if (list == null) return;
            if (list.Count == 0 || list[^1] != "Custom…")
            {
                list.Add("Custom…");
            }
        }

        private static void AddIfMissing(List<string> list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value)) return;
            if (list.Contains(value))
            {
                return;
            }

            // Insert before "Custom…" if present, otherwise append
            var customIdx = list.IndexOf("Custom…");
            if (customIdx >= 0) list.Insert(customIdx, value);
            else list.Add(value);
        }

        private static string[] EnsureContainsModel(string[] arr, string modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return arr ?? Array.Empty<string>();
            if (arr == null || arr.Length == 0)
                return new[] { modelName, "Custom…" };

            // already present?
            foreach (var t in arr)
                if (t == modelName)
                {
                    return arr;
                }

            // Insert before Custom… if present
            var list = new List<string>(arr);
            int customIdx = list.IndexOf("Custom…");
            if (customIdx >= 0) list.Insert(customIdx, modelName);
            else list.Add(modelName);
            return list.ToArray();
        }

        [Serializable]
        private class VoiceMeta
        {
            public string voiceId;
            public string name;
            public string labels; // flattened "k=v,k2=v2"
            public string description;
            public string previewUrl;

            public string Pack()
            {
                return Escape(voiceId) + "|" + Escape(name) + "|" + Escape(labels) + "|" + Escape(description) + "|" +
                       Escape(previewUrl);
            }

            public static VoiceMeta Unpack(string s)
            {
                if (string.IsNullOrEmpty(s)) return null;
                var parts = s.Split(new[] { '|' }, 5);
                if (parts.Length < 5) return null;
                return new VoiceMeta
                {
                    voiceId = Unescape(parts[0]),
                    name = Unescape(parts[1]),
                    labels = Unescape(parts[2]),
                    description = Unescape(parts[3]),
                    previewUrl = Unescape(parts[4])
                };
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

            public string Pack()
            {
                // Pipe-delimited, languages as ';' list
                var langs = (languages == null || languages.Count == 0) ? "" : string.Join(";", languages);
                return E(model_id) + "|" + E(name) + "|" + E(B(can_be_finetuned)) + "|" + E(B(can_do_text_to_speech)) +
                       "|" +
                       E(B(can_do_voice_conversion)) + "|" + E(B(can_use_style)) + "|" + E(B(can_use_speaker_boost)) +
                       "|" +
                       E(B(serves_pro_voices)) + "|" + E(D(token_cost_factor)) + "|" + E(description) + "|" +
                       E(B(requires_alpha_access)) + "|" + E(I(max_characters_request_free_user)) + "|" +
                       E(I(max_characters_request_subscribed_user)) + "|" + E(I(maximum_text_length_per_request)) +
                       "|" +
                       E(langs) + "|" + E(concurrency_group);
            }

            public static ElModelMeta Unpack(string s)
            {
                if (string.IsNullOrEmpty(s)) return null;
                var p = s.Split(new[] { '|' }, 16);
                if (p.Length < 16) return null;
                var langs = U(p[14]);
                var langList = string.IsNullOrEmpty(langs) ? new List<string>() : new List<string>(langs.Split(';'));
                return new ElModelMeta
                {
                    model_id = U(p[0]),
                    name = U(p[1]),
                    can_be_finetuned = UB(p[2]),
                    can_do_text_to_speech = UB(p[3]),
                    can_do_voice_conversion = UB(p[4]),
                    can_use_style = UB(p[5]),
                    can_use_speaker_boost = UB(p[6]),
                    serves_pro_voices = UB(p[7]),
                    token_cost_factor = UD(p[8]),
                    description = U(p[9]),
                    requires_alpha_access = UB(p[10]),
                    max_characters_request_free_user = UI(p[11]),
                    max_characters_request_subscribed_user = UI(p[12]),
                    maximum_text_length_per_request = UI(p[13]),
                    languages = langList,
                    concurrency_group = U(p[15])
                };
            }

            private static string E(string v) => string.IsNullOrEmpty(v) ? "" : v.Replace("|", "%7C");
            private static string U(string v) => string.IsNullOrEmpty(v) ? "" : v.Replace("%7C", "|");
            private static string B(bool? v) => v.HasValue ? (v.Value ? "1" : "0") : "";
            private static bool? UB(string v) => string.IsNullOrEmpty(v) ? (bool?)null : v == "1";
            private static string I(int? v) => v.HasValue ? v.Value.ToString() : "";
            private static int? UI(string v) => int.TryParse(v, out var n) ? n : (int?)null;

            private static string D(double? v) => v.HasValue
                ? v.Value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)
                : "";

            private static double? UD(string v) => double.TryParse(v, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? d
                : (double?)null;
        }

        private ElModelMeta FindModelMeta(string id)
        {
            if (string.IsNullOrEmpty(id) || _modelMeta == null || _modelMeta.Count == 0) return null;
            for (int k = 0; k < _modelMeta.Count; k++)
                if (string.Equals(_modelMeta[k].model_id, id, StringComparison.Ordinal))
                    return _modelMeta[k];
            return null;
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
