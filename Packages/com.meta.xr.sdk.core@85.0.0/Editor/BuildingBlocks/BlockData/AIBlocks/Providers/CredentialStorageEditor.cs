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
using System.Text.RegularExpressions;
using Meta.XR.Telemetry;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CustomEditor(typeof(CredentialStorage))]
    public class CredentialStorageEditor : UnityEditor.Editor
    {
        private readonly Dictionary<string, bool> _assetsFoldout = new();
        private readonly Dictionary<string, List<Object>> _cachedAssets = new();
        private readonly Dictionary<string, ProviderValidationHelper.ValidationResult> _validationResults = new();
        private readonly Dictionary<string, bool> _isTesting = new();
        private readonly Dictionary<string, UnityWebRequest> _activeRequests = new();
        private readonly Dictionary<string, UnityWebRequestAsyncOperation> _activeOps = new();
        private readonly Dictionary<string, EditorApplication.CallbackFunction> _updateTicks = new();
        private const float LabelWidth = 128f;
        private bool _hasTriedAutoPopulate;

        private readonly struct LabelWidthScope : System.IDisposable
        {
            private readonly float _prev;

            public LabelWidthScope(float width)
            {
                _prev = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = width;
            }

            public void Dispose() => EditorGUIUtility.labelWidth = _prev;
        }

        private void OnEnable()
        {
            _hasTriedAutoPopulate = false;
        }

        public override void OnInspectorGUI()
        {
            if (!_hasTriedAutoPopulate)
            {
                _hasTriedAutoPopulate = true;
                EditorApplication.delayCall += () =>
                {
                    if (target)
                    {
                        CredentialAutoRegistrar.AutoPopulateKeys(target as CredentialStorage, silent: true);
                    }
                };
            }

            EditorGUILayout.HelpBox(
                "Store API keys per provider here. When a provider asset does NOT override, " +
                "its API key is populated from this storage.",
                MessageType.Info);

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Registered Providers", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh Assets Cache", GUILayout.Width(150)))
                {
                    _cachedAssets.Clear();

                    var keysToReset = new List<string>(_validationResults.Keys);
                    foreach (var key in keysToReset)
                    {
                        _validationResults[key] = new ProviderValidationHelper.ValidationResult
                        {
                            State = ProviderValidationHelper.ValidationState.NotChecked,
                            Message = ""
                        };
                    }

                    ClearAllEditorPrefsCache();
                    Repaint();
                }
            }

            EditorGUILayout.Space();

            serializedObject.Update();
            var entriesProp = serializedObject.FindProperty("entries");
            if (entriesProp != null)
            {
                for (var i = 0; i < entriesProp.arraySize; i++)
                {
                    var element = entriesProp.GetArrayElementAtIndex(i);
                    var pidProp = element.FindPropertyRelative("providerId");
                    var keyProp = element.FindPropertyRelative("apiKey");

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(4);
                            using (new EditorGUILayout.VerticalScope())
                            using (new LabelWidthScope(LabelWidth))
                            {
                                EditorGUILayout.Space(6);

                                using (new EditorGUI.DisabledScope(true))
                                {
                                    EditorGUILayout.TextField(new GUIContent("Provider ID"), pidProp.stringValue);
                                }

                                GUILayout.Space(6);
                                EditorGUI.BeginChangeCheck();

                                var newKey = EditorGUILayout.TextField(new GUIContent("API Key"), keyProp.stringValue);
                                if (EditorGUI.EndChangeCheck())
                                {
                                    keyProp.stringValue = newKey;
                                }

                                GUILayout.Space(8);

                                var providerId = pidProp.stringValue;
                                _assetsFoldout.TryAdd(providerId, true);

                                if (!_cachedAssets.ContainsKey(providerId))
                                {
                                    _cachedAssets[providerId] = CredentialEditorUtil.FindProviderAssets(providerId);
                                }

                                var assets = _cachedAssets[providerId];
                                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2);
                                var labelRect = new Rect(rowRect.x, rowRect.y, LabelWidth - 4f, rowRect.height);
                                var isHover = rowRect.Contains(Event.current.mousePosition);

                                if (isHover)
                                {
                                    var hover = EditorGUIUtility.isProSkin
                                        ? new Color(1f, 1f, 1f, 0.06f)
                                        : new Color(0f, 0f, 0f, 0.06f);
                                    EditorGUI.DrawRect(rowRect, hover);
                                    EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);
                                }

                                var assetsLabelStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                                EditorGUI.LabelField(labelRect, $"Assets ({assets.Count})", assetsLabelStyle);

                                if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
                                {
                                    _assetsFoldout[providerId] = !_assetsFoldout[providerId];
                                    GUI.changed = true;
                                    Event.current.Use();
                                }

                                if (_assetsFoldout[providerId])
                                {
                                    GUILayout.Space(6);
                                    if (assets.Count == 0)
                                    {
                                        using (new EditorGUILayout.HorizontalScope())
                                        {
                                            GUILayout.Space(LabelWidth);
                                            EditorGUILayout.LabelField("No assets found for this provider.");
                                        }
                                    }
                                    else
                                    {
                                        foreach (var obj in assets)
                                        {
                                            GUILayout.Space(2);
                                            using (new EditorGUILayout.HorizontalScope())
                                            {
                                                GUILayout.Space(LabelWidth);
                                                using (new EditorGUI.DisabledScope(true))
                                                {
                                                    EditorGUILayout.ObjectField(obj, typeof(Object), false);
                                                }

                                                var provider = obj as AIProviderBase;
                                                var usesOwnKey = false;

                                                if (provider is IUsesCredential { OverrideApiKey: true })
                                                {
                                                    usesOwnKey = true;
                                                    var style = new GUIStyle(EditorStyles.miniLabel)
                                                    {
                                                        normal = { textColor = new Color(0.8f, 0.6f, 0.2f) },
                                                        fontStyle = FontStyle.Italic
                                                    };
                                                    GUILayout.Label(
                                                        new GUIContent("own key",
                                                            "This provider has 'Override API Key' enabled and uses its own stored key"),
                                                        style, GUILayout.Width(50));
                                                }

                                                GUILayout.Space(4);

                                                var testKey = $"{providerId}_{obj.GetInstanceID()}";
                                                _isTesting.TryAdd(testKey, false);

                                                if (!_validationResults.ContainsKey(testKey))
                                                {
                                                    if (provider is IUsesCredential credProv)
                                                    {
                                                        var config = credProv.GetTestConfig();
                                                        var apiKeyToCheck = usesOwnKey
                                                            ? GetApiKeyForProvider(provider)
                                                            : keyProp.stringValue;

                                                        if (!string.IsNullOrEmpty(config.Endpoint) &&
                                                            !string.IsNullOrEmpty(config.Model) &&
                                                            ProviderValidationHelper.TryGetCachedValidation(
                                                                config.Endpoint, apiKeyToCheck, config.Model,
                                                                config.ProviderId, out var cached))
                                                        {
                                                            _validationResults[testKey] = cached;
                                                        }
                                                        else
                                                        {
                                                            _validationResults.TryAdd(testKey,
                                                                new ProviderValidationHelper.ValidationResult());
                                                        }
                                                    }
                                                    else
                                                    {
                                                        _validationResults.TryAdd(testKey,
                                                            new ProviderValidationHelper.ValidationResult());
                                                    }
                                                }

                                                using (new EditorGUI.DisabledScope(_isTesting[testKey]))
                                                {
                                                    var buttonStyle = new GUIStyle(GUI.skin.button)
                                                    {
                                                        padding = new RectOffset(6, 6, 2, 2),
                                                        margin = new RectOffset(4, 4, 2, 2)
                                                    };

                                                    if (GUILayout.Button(
                                                            new GUIContent("Test Connection", "Test connection with these credentials"),
                                                            buttonStyle,
                                                            GUILayout.Height(18),
                                                            GUILayout.ExpandWidth(false)))
                                                    {
                                                        var apiKeyToUse = usesOwnKey
                                                            ? GetApiKeyForProvider(provider)
                                                            : keyProp.stringValue;
                                                        TestProviderConnection(provider, apiKeyToUse, testKey);
                                                    }
                                                }

                                                GUILayout.Space(2);

                                                var badgeColor =
                                                    ProviderValidationHelper.GetBadgeColor(_validationResults[testKey].State);
                                                ProviderValidationHelper.DrawBadge(badgeColor,
                                                    new GUIContent("", _validationResults[testKey].Message));
                                            }
                                        }
                                    }
                                }

                                GUILayout.Space(6);
                            }
                            GUILayout.Space(4);
                        }
                    }

                    EditorGUILayout.Space(8);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void OnDisable()
        {
            foreach (var kvp in _activeRequests)
            {
                if (kvp.Value == null)
                {
                    continue;
                }

                try
                {
                    kvp.Value.Abort();
                    kvp.Value.Dispose();
                }
                catch
                {
                    // ignored
                }
            }

            foreach (var kvp in _updateTicks)
            {
                if (kvp.Value != null)
                {
                    EditorApplication.update -= kvp.Value;
                }
            }

            _activeRequests.Clear();
            _activeOps.Clear();
            _updateTicks.Clear();
        }

        /// <summary>
        /// Clears all AI Blocks EditorPrefs cache including validation results and model lists.
        /// </summary>
        private static void ClearAllEditorPrefsCache()
        {
            var cleared = 0;

            var providerTypes = TypeCache.GetTypesDerivedFrom<AIProviderBase>();
            foreach (var t in providerTypes)
            {
                if (t.IsAbstract || !typeof(IUsesCredential).IsAssignableFrom(t))
                    continue;

                var guids = AssetDatabase.FindAssets($"t:{t.Name}");
                if (guids == null || guids.Length == 0)
                    continue;

                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var provider = AssetDatabase.LoadAssetAtPath(path, t) as AIProviderBase;
                    if (!provider || provider is not IUsesCredential credentialProvider)
                        continue;

                    var config = credentialProvider.GetTestConfig();
                    if (string.IsNullOrEmpty(config.Endpoint) || string.IsNullOrEmpty(config.Model))
                        continue;

                    var apiKey = GetApiKeyForProvider(provider);

                    ProviderValidationHelper.ClearValidationCache(
                        config.Endpoint,
                        apiKey,
                        config.Model,
                        config.ProviderId);
                    cleared++;
                }
            }

            var knownProviderIds = new[]
            {
                "OpenAI",
                "LlamaAPI",
                "ElevenLabs",
                "HuggingFace",
                "Anthropic",
                "GoogleAI"
            };

            foreach (var providerId in knownProviderIds)
            {
                var modelCacheKey = $"AIBlocks_ModelCache_{providerId}";
                var timestampKey = $"AIBlocks_ModelCacheTime_{providerId}";

                if (EditorPrefs.HasKey(modelCacheKey))
                {
                    EditorPrefs.DeleteKey(modelCacheKey);
                    cleared++;
                }

                if (EditorPrefs.HasKey(timestampKey))
                {
                    EditorPrefs.DeleteKey(timestampKey);
                    cleared++;
                }
            }

            var ollamaCacheKeys = new[]
            {
                "aiblocks.ollama.cache",
                "aiblocks.ollama.stamp",
                "aiblocks.ollama.endpoint"
            };

            foreach (var key in ollamaCacheKeys)
            {
                if (EditorPrefs.HasKey(key))
                {
                    EditorPrefs.DeleteKey(key);
                    cleared++;
                }
            }

            var elevenLabsKeys = new[]
            {
                "aiblocks.elevenlabs.voices",
                "aiblocks.elevenlabs.voices.time"
            };

            foreach (var key in elevenLabsKeys)
            {
                if (EditorPrefs.HasKey(key))
                {
                    EditorPrefs.DeleteKey(key);
                    cleared++;
                }
            }

            Debug.Log($"[CredentialStorage] Cleared {cleared} AI Blocks EditorPrefs cache entries (validation results and model lists)");
        }

        private static string GetApiKeyForProvider(AIProviderBase provider)
        {
            if (!provider)
            {
                return string.Empty;
            }

            var providerType = provider.GetType();
            var apiKeyField = providerType.GetField("apiKey",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            if (apiKeyField == null)
            {
                return string.Empty;
            }

            var apiKey = apiKeyField.GetValue(provider) as string;
            return apiKey ?? string.Empty;
        }

        private void TestProviderConnection(AIProviderBase provider, string apiKey, string testKey)
        {
            if (!provider)
            {
                return;
            }

            if (provider is not IUsesCredential credentialProvider)
            {
                IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "ai-credential-test-provider-interface-error",
                    $"[CredentialStorage] {provider.GetType().Name} does not implement IUsesCredential. Cannot test connection.");
                _validationResults[testKey] = new ProviderValidationHelper.ValidationResult
                {
                    State = ProviderValidationHelper.ValidationState.NetworkError,
                    Message = "Provider does not support connection testing."
                };
                return;
            }

            var config = credentialProvider.GetTestConfig();

            if (string.IsNullOrEmpty(config.Endpoint))
            {
                IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "ai-credential-test-no-endpoint",
                    $"[CredentialStorage] {provider.GetType().Name} has no endpoint configured. Cannot test connection.");
                _validationResults[testKey] = new ProviderValidationHelper.ValidationResult
                {
                    State = ProviderValidationHelper.ValidationState.NetworkError,
                    Message = "No endpoint configured on this provider asset."
                };
                return;
            }

            if (string.IsNullOrEmpty(config.Model))
            {
                IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "ai-credential-test-no-model",
                    $"[CredentialStorage] {provider.GetType().Name} has no model configured. Cannot test connection.");
                _validationResults[testKey] = new ProviderValidationHelper.ValidationResult
                {
                    State = ProviderValidationHelper.ValidationState.NotFound,
                    Message = "No model configured on this provider asset."
                };
                return;
            }

            _isTesting[testKey] = true;
            Repaint();

            ProviderValidationHelper.TestConnection(
                config.Endpoint,
                apiKey,
                config.Model,
                config.ProviderId,
                result =>
                {
                    _validationResults[testKey] = result;
                    _isTesting[testKey] = false;
                    Repaint();
                },
                (req, op, tick) =>
                {
                    _activeRequests[testKey] = req;
                    _activeOps[testKey] = op;
                    _updateTicks[testKey] = tick;
                },
                () =>
                {
                    if (_activeRequests.TryGetValue(testKey, out var req))
                    {
                        if (req != null)
                        {
                            try
                            {
                                req.Abort();
                                req.Dispose();
                            }
                            catch
                            {
                                // ignored
                            }
                        }

                        _activeRequests[testKey] = null;
                    }

                    if (!_updateTicks.TryGetValue(testKey, out var tick))
                    {
                        return;
                    }

                    if (tick != null)
                    {
                        EditorApplication.update -= tick;
                    }

                    _updateTicks[testKey] = null;
                }
            );
        }
    }

    internal static class CredentialAutoRegistrar
    {
        private static double _nextScanAt = -1;
        private const double DebounceSeconds = 0.5;

        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.delayCall += ScanOnce;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.projectChanged += OnProjectChanged;
        }

        private static void OnProjectChanged()
        {
            _nextScanAt = EditorApplication.timeSinceStartup + DebounceSeconds;
            EditorApplication.update -= DebouncedUpdate;
            EditorApplication.update += DebouncedUpdate;
        }

        private static void DebouncedUpdate()
        {
            if (_nextScanAt < 0 || EditorApplication.timeSinceStartup < _nextScanAt)
            {
                return;
            }

            EditorApplication.update -= DebouncedUpdate;
            _nextScanAt = -1;
            ScanOnce();
        }

        [MenuItem("Meta/Tools/AI/Credentials/Auto-Register Providers Now")]
        private static void ManualScan() => ScanOnce();

        /// <summary>
        /// Scans all provider assets in the project and autopopulates credentials from the first
        /// non-overridden asset of each provider type that has a non-empty API key.
        /// </summary>
        /// <param name="storage">The credential storage to populate.</param>
        /// <param name="silent">If true, only logs when credentials are actually populated.</param>
        public static void AutoPopulateKeys(CredentialStorage storage, bool silent = false)
        {
            if (!storage)
            {
                if (!silent)
                {
                    IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "credential-storage-auto-populate-failed",
                        "[CredentialStorage] Cannot auto-populate: storage is null.");
                }

                return;
            }

            var providerTypes = TypeCache.GetTypesDerivedFrom<AIProviderBase>();
            var populated = 0;

            Undo.RecordObject(storage, "Auto-Populate Credentials");

            foreach (var t in providerTypes)
            {
                if (t.IsAbstract || !typeof(IUsesCredential).IsAssignableFrom(t))
                {
                    continue;
                }

                var guids = AssetDatabase.FindAssets($"t:{t.Name}");
                if (guids == null || guids.Length == 0)
                {
                    continue;
                }

                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var provider = AssetDatabase.LoadAssetAtPath(path, t) as AIProviderBase;
                    if (!provider || provider is not IUsesCredential cred)
                    {
                        continue;
                    }

                    if (cred.OverrideApiKey)
                    {
                        continue;
                    }

                    var providerId = CredentialEditorUtil.GetProviderId(provider);
                    if (string.IsNullOrEmpty(providerId))
                    {
                        continue;
                    }

                    var apiKeyField = t.GetField("apiKey",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);

                    if (apiKeyField == null)
                    {
                        continue;
                    }

                    var apiKey = apiKeyField.GetValue(provider) as string;
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        continue;
                    }

                    if (storage.TryGetEntry(providerId, out var entry))
                    {
                        if (string.IsNullOrEmpty(entry.apiKey))
                        {
                            entry.apiKey = apiKey;
                            populated++;
                            if (!silent)
                            {
                                Debug.Log(
                                    $"[CredentialStorage] Auto-populated '{providerId}' from asset: {provider.name}");
                            }
                        }
                    }
                    else
                    {
                        storage.AddProviderIfMissing(providerId);
                        if (storage.TryGetEntry(providerId, out var newEntry))
                        {
                            newEntry.apiKey = apiKey;
                            populated++;
                            if (!silent)
                            {
                                Debug.Log(
                                    $"[CredentialStorage] Auto-populated '{providerId}' from asset: {provider.name}");
                            }
                        }
                    }

                    break;
                }
            }

            if (populated > 0)
            {
                EditorUtility.SetDirty(storage);
                if (!silent)
                {
                    Debug.Log($"[CredentialStorage] Auto-populated {populated} credential(s).");
                }
            }
            else if (!silent)
            {
                Debug.Log(
                    "[CredentialStorage] No credentials to auto-populate. Either all entries already have keys, or no provider assets with API keys were found.");
            }
        }

        private static void ScanOnce()
        {
            var storage = CredentialEditorUtil.StorageOrNull;
            if (!storage)
            {
                return;
            }

            var providerTypes = TypeCache.GetTypesDerivedFrom<IUsesCredential>();

            var added = 0;
            Undo.RecordObject(storage, "Auto-Register Providers");

            foreach (var t in providerTypes)
            {
                if (t.IsAbstract)
                {
                    continue;
                }

                var guids = AssetDatabase.FindAssets($"t:{t.Name}");
                if (guids == null || guids.Length == 0)
                {
                    continue;
                }

                var typeName = t.Name;
                var providerId = Regex.Replace(typeName, "Provider$", "").Trim();

                if (storage.TryGetEntry(providerId, out _))
                {
                    continue;
                }

                storage.AddProviderIfMissing(providerId);
                added++;
            }

            if (added <= 0)
            {
                return;
            }

            EditorUtility.SetDirty(storage);
            Debug.Log($"[CredentialStorage] Auto-registered {added} provider(s).");
        }
    }
}
