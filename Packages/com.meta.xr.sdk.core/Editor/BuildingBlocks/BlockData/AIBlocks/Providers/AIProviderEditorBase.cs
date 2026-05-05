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

using System;
using System.Collections.Generic;
using Meta.XR.Telemetry;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// Base class for AI provider editors that handles common credential storage logic.
    /// Subclasses can optionally call InitializeCredentialStorage in OnEnable and
    /// use DrawApiKeyField to render the API key field with credential storage integration.
    /// </summary>
    public abstract class AIProviderEditorBase : UnityEditor.Editor
    {
        private IUsesCredential CredentialProvider { get; set; }
        private AIProviderBase ProviderBase { get; set; }
        protected SerializedProperty ApiKeyProperty { get; private set; }

        private bool _isTesting;
        private UnityWebRequest _activeRequest;
        protected UnityWebRequestAsyncOperation ActiveOp;
        private EditorApplication.CallbackFunction _updateTick;
        private ProviderValidationHelper.ValidationResult _validationResult;
        private bool _hasLoadedCache;
        private string _lastConfigSnapshot;

        [Serializable]
        protected class ModelsResponse
        {
            public List<ModelData> data;
        }

        [Serializable]
        protected class ModelData
        {
            public string id;
            public string created;
            public string owned_by;
        }

        protected List<string> FetchedModels;
        protected Dictionary<string, ModelData> FetchedModelData;
        protected bool IsFetchingModels;
        protected string FetchError;
        protected const string CustomOption = "Custom …";

        private const string CacheKeyPrefix = "AIBlocks_ModelCache_";
        private const string CacheTimestampPrefix = "AIBlocks_ModelCacheTime_";
        private const double CacheExpirationSeconds = 3600.0; // 1 hour

        /// <summary>
        /// Initializes credential storage integration. Call this in OnEnable() with the name
        /// of the apiKey serialized field.
        /// </summary>
        /// <param name="apiKeyPropertyName">Name of the SerializedProperty for the API key (e.g., "apiKey").</param>
        protected void InitializeCredentialStorage(string apiKeyPropertyName)
        {
            ApiKeyProperty = serializedObject.FindProperty(apiKeyPropertyName);
            CredentialProvider = target as IUsesCredential;
            ProviderBase = target as AIProviderBase;

            if (CredentialProvider == null || !ProviderBase)
            {
                return;
            }

            var storage = CredentialEditorUtil.StorageOrNull;
            if (storage)
            {
                var providerId = CredentialEditorUtil.GetProviderId(ProviderBase);
                CredentialEditorUtil.EnsureProviderEntry(storage, providerId);
            }

            if (CredentialProvider.OverrideApiKey)
            {
                return;
            }

            if (!storage)
            {
                return;
            }

            var storageKey = CredentialEditorUtil.GetStorageKey(ProviderBase);
            var assetKey = ApiKeyProperty?.stringValue ?? string.Empty;

            if (string.IsNullOrEmpty(storageKey) && !string.IsNullOrEmpty(assetKey))
            {
                var providerId = CredentialEditorUtil.GetProviderId(ProviderBase);
                CredentialEditorUtil.SeedKeyIfEmpty(storage, providerId, assetKey);
            }
            else if (!string.IsNullOrEmpty(storageKey))
            {
                if (ApiKeyProperty == null || ApiKeyProperty.stringValue == storageKey)
                {
                    return;
                }

                ApiKeyProperty.stringValue = storageKey;
                serializedObject.ApplyModifiedProperties();
            }
        }

        /// <summary>
        /// Draws the API key field with credential storage integration.
        /// Handles override toggle, credential storage button, and conditional field rendering.
        /// </summary>
        /// <param name="label">Label for the API key field.</param>
        /// <param name="getKeyUrl">Optional URL to open when "Get Key..." button is clicked.</param>
        /// <param name="drawExtraTopRight">Optional callback to draw extra content in the top-right section (next to Open Credential Storage button).</param>
        /// <param name="drawExtraRightSide">Optional callback to draw extra content on the right side of the API key field.</param>
        protected void DrawApiKeyField(string label = "API Key", string getKeyUrl = null,
            Action drawExtraTopRight = null, Action drawExtraRightSide = null)
        {
            if (CredentialProvider != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var labelWidth = EditorGUIUtility.labelWidth;
                    var newOverride = EditorGUILayout.ToggleLeft(new GUIContent("Override API Key per asset"),
                        CredentialProvider.OverrideApiKey, GUILayout.Width(labelWidth));
                    if (newOverride != CredentialProvider.OverrideApiKey)
                    {
                        CredentialProvider.OverrideApiKey = newOverride;
                        EditorUtility.SetDirty(target);
                    }

                    if (GUILayout.Button("Open Credential Storage", GUILayout.MinWidth(140),
                            GUILayout.ExpandWidth(true)))
                    {
                        CredentialEditorUtil.PingStorageAsset();
                    }

                    if (drawExtraTopRight != null)
                    {
                        GUILayout.Space(4);
                        drawExtraTopRight.Invoke();
                    }
                }

                EditorGUILayout.Space(6);
            }

            if (CredentialProvider == null || CredentialProvider.OverrideApiKey)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(ApiKeyProperty, new GUIContent(label));

                    var localVal = ApiKeyProperty?.stringValue?.Trim();
                    if (string.IsNullOrEmpty(localVal) && !string.IsNullOrEmpty(getKeyUrl))
                    {
                        if (GUILayout.Button("Get Key…", GUILayout.Width(90)))
                        {
                            Application.OpenURL(getKeyUrl);
                        }
                    }

                    drawExtraRightSide?.Invoke();
                }

                return;
            }

            var storage = CredentialEditorUtil.StorageOrNull;
            if (!storage)
            {
                EditorGUILayout.HelpBox("No CredentialStorage asset found. Create one to centralize API keys.",
                    MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(ApiKeyProperty, new GUIContent(label));

                    var localVal = ApiKeyProperty?.stringValue?.Trim();
                    if (string.IsNullOrEmpty(localVal) && !string.IsNullOrEmpty(getKeyUrl))
                    {
                        if (GUILayout.Button("Get Key…", GUILayout.Width(90)))
                        {
                            Application.OpenURL(getKeyUrl);
                        }
                    }

                    drawExtraRightSide?.Invoke();
                }

                return;
            }

            var storageKey = ProviderBase ? CredentialEditorUtil.GetStorageKey(ProviderBase) : string.Empty;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent(label, "Managed by CredentialStorage"));
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(storageKey ?? string.Empty);
                }

                drawExtraRightSide?.Invoke();
            }
        }

        /// <summary>
        /// Returns the index of value in array, or the last index (assumed to be "Custom") if not found.
        /// </summary>
        private static int IndexOfOrCustom(string value, string[] array)
        {
            if (array == null || array.Length == 0)
            {
                return -1;
            }

            if (string.IsNullOrEmpty(value))
            {
                return array.Length - 1;
            }

            for (var i = 0; i < array.Length - 1; i++)
            {
                if (array[i] == value) return i;
            }

            return array.Length - 1;
        }

        /// <summary>
        /// Returns true if the index points to the last item in the array (assumed to be "Custom").
        /// </summary>
        private static bool IsCustomIndex(int index, string[] array)
        {
            return array != null && index >= 0 && index == array.Length - 1;
        }

        /// <summary>
        /// Draws a property field only if it's not null. Safe null check wrapper.
        /// </summary>
        protected static void Prop(SerializedProperty p, string label, string tooltip = null)
        {
            if (p != null)
            {
                EditorGUILayout.PropertyField(p, new GUIContent(label, tooltip ?? string.Empty));
            }
        }

        /// <summary>
        /// Draws a wide text field with a prefix label.
        /// </summary>
        protected static void WideText(SerializedProperty p, string label)
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

        /// <summary>
        /// Draws a wide text area with a label.
        /// </summary>
        protected static void WideTextArea(SerializedProperty p, string label, float minHeight = 60f)
        {
            if (p == null)
            {
                return;
            }

            EditorGUILayout.LabelField(label);
            var rect = GUILayoutUtility.GetRect(360f, minHeight, GUILayout.ExpandWidth(true));
            p.stringValue = EditorGUI.TextArea(rect, p.stringValue ?? string.Empty);
        }

        /// <summary>
        /// Draws content with increased indent level.
        /// </summary>
        protected static void Indent(Action draw)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                draw?.Invoke();
            }
        }

        /// <summary>
        /// Cleans up active web requests and editor callbacks. Call this in OnDisable().
        /// </summary>
        protected void CleanupValidationRequest()
        {
            ProviderValidationHelper.CleanupRequest(ref _activeRequest, ref _updateTick);
        }

        /// <summary>
        /// Tests the connection to a provider endpoint. Subclasses should call this with appropriate parameters.
        /// </summary>
        /// <param name="endpoint">The API endpoint URL.</param>
        /// <param name="model">The model identifier to test with.</param>
        /// <param name="providerId">The provider ID (e.g., "OpenAI", "HuggingFace").</param>
        /// <param name="defaultEndpoint">Optional default endpoint to use if endpoint parameter is empty.</param>
        protected void TestConnection(string endpoint, string model, string providerId, string defaultEndpoint = null)
        {
            var apiKey = ApiKeyProperty?.stringValue?.Trim();

            if (string.IsNullOrEmpty(endpoint) && !string.IsNullOrEmpty(defaultEndpoint))
            {
                endpoint = defaultEndpoint;
            }

            _isTesting = true;
            Repaint();

            ProviderValidationHelper.TestConnection(endpoint, apiKey, model, providerId, result =>
                {
                    _validationResult = result;
                    _isTesting = false;
                    Repaint();
                },
                (req, op, tick) =>
                {
                    _activeRequest = req;
                    ActiveOp = op;
                    _updateTick = tick;
                },
                () => ProviderValidationHelper.CleanupRequest(ref _activeRequest, ref _updateTick)
            );
        }

        /// <summary>
        /// Creates a snapshot string of the current configuration for change detection.
        /// </summary>
        private static string CreateConfigSnapshot(string endpoint, string model, string providerId, string apiKey)
        {
            return $"{endpoint}|{model}|{providerId}|{apiKey?.GetHashCode() ?? 0}";
        }

        /// <summary>
        /// Loads cached validation result if available. Should be called once per inspector before drawing validation UI.
        /// Also checks if configuration has changed and clears cache if necessary.
        /// </summary>
        /// <param name="endpoint">The API endpoint URL.</param>
        /// <param name="model">The model identifier.</param>
        /// <param name="providerId">The provider ID.</param>
        protected void TryLoadCachedValidation(string endpoint, string model, string providerId)
        {
            var apiKey = ApiKeyProperty?.stringValue?.Trim();
            var currentSnapshot = CreateConfigSnapshot(endpoint, model, providerId, apiKey);

            if (!string.IsNullOrEmpty(_lastConfigSnapshot) && _lastConfigSnapshot != currentSnapshot)
            {
                ProviderValidationHelper.ClearValidationCache(endpoint, apiKey, model, providerId);
                _validationResult = new ProviderValidationHelper.ValidationResult
                { State = ProviderValidationHelper.ValidationState.NotChecked };
                _hasLoadedCache = false;
            }

            _lastConfigSnapshot = currentSnapshot;

            if (_hasLoadedCache)
            {
                return;
            }

            _hasLoadedCache = true;

            if (ProviderValidationHelper.TryGetCachedValidation(endpoint, apiKey, model, providerId, out var cached))
            {
                _validationResult = cached;
            }
        }

        /// <summary>
        /// Draws a "Test Connection" button and validation badge, typically used in drawExtraTopRight callback.
        /// </summary>
        protected void DrawTestConnectionButton(string tooltip = "Test connection with the configured model")
        {
            using (new EditorGUI.DisabledScope(_isTesting))
            {
                if (GUILayout.Button(new GUIContent("Test Connection", tooltip),
                        GUILayout.MinWidth(100), GUILayout.ExpandWidth(true)))
                {
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    OnTestConnection();
                }
            }

            var badgeColor = ProviderValidationHelper.GetBadgeColor(_validationResult.State);
            ProviderValidationHelper.DrawBadge(badgeColor, new GUIContent("", _validationResult.Message));
        }

        /// <summary>
        /// Called when the Test Connection button is clicked. Subclasses should override this
        /// to provide provider-specific endpoint, model, and provider type parameters.
        /// </summary>
        protected virtual void OnTestConnection()
        {
            // Subclasses should override this method
        }

        /// <summary>
        /// Draws a status box showing fetch progress and model information.
        /// Use this for providers that fetch models dynamically from APIs.
        /// </summary>
        /// <param name="isFetching">Whether models are currently being fetched.</param>
        /// <param name="fetchError">Error message if fetch failed, or null/empty if no error.</param>
        /// <param name="hasFetchedModels">Whether models have been successfully fetched.</param>
        /// <param name="modelId">The currently selected model ID.</param>
        /// <param name="modelInfo">Optional additional info about the model to display.</param>
        protected static void DrawModelStatusBox(bool isFetching, string fetchError, bool hasFetchedModels,
            string modelId, string modelInfo = null)
        {
            if (isFetching)
            {
                EditorGUILayout.HelpBox("Fetching models from API...", MessageType.Info);
                return;
            }

            if (!string.IsNullOrEmpty(fetchError))
            {
                EditorGUILayout.HelpBox($"Failed to fetch models: {fetchError}\nClick refresh button to retry.",
                    MessageType.Warning);
                return;
            }

            if (string.IsNullOrEmpty(modelId))
            {
                EditorGUILayout.HelpBox("Model ID: (empty)\nPlease select or enter a model ID.", MessageType.Warning);
                return;
            }

            if (!string.IsNullOrEmpty(modelInfo))
            {
                EditorGUILayout.HelpBox(modelInfo, MessageType.Info);
            }
            else if (hasFetchedModels)
            {
                EditorGUILayout.HelpBox($"Model ID: {modelId}\n(Custom model or not in fetched list)",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox($"Model ID: {modelId}\n(Click refresh to fetch available models)",
                    MessageType.Info);
            }
        }

        /// <summary>
        /// Draws a generic dropdown picker with custom option support.
        /// Automatically handles switching between dropdown and text field for custom values.
        /// </summary>
        /// <param name="property">The serialized property to edit.</param>
        /// <param name="options">Array of options to show in the dropdown (last item should be "Custom …").</param>
        /// <param name="label">Label for the field.</param>
        /// <param name="tooltip">Optional tooltip text.</param>
        /// <param name="onRefresh">Optional callback for refresh button. If null, no refresh button is shown.</param>
        /// <param name="isRefreshing">If true and onRefresh is provided, disables the refresh button.</param>
        protected void DrawDropdownPickerWithCustom(SerializedProperty property, string[] options, string label,
            string tooltip = null, Action onRefresh = null, bool isRefreshing = false)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent(label, tooltip ?? string.Empty));

                var curr = property?.stringValue ?? string.Empty;
                var idx = IndexOfOrCustom(curr, options);
                var isCustom = IsCustomIndex(idx, options);

                if (!isCustom)
                {
                    var newIdx = EditorGUILayout.Popup(idx, options, GUILayout.ExpandWidth(true));
                    if (newIdx != idx && property != null)
                    {
                        property.stringValue = options[newIdx] == CustomOption ? "" : options[newIdx];
                    }
                }
                else
                {
                    var newIdx = EditorGUILayout.Popup(idx, options,
                        GUILayout.MaxWidth(onRefresh != null ? 155f : 180f));
                    if (newIdx != idx && property != null)
                    {
                        property.stringValue = options[newIdx] == CustomOption
                            ? (property.stringValue ?? "")
                            : options[newIdx];
                    }

                    GUILayout.Space(6);
                    if (property != null)
                    {
                        property.stringValue = EditorGUILayout.TextField(property.stringValue ?? string.Empty,
                            GUILayout.ExpandWidth(true));
                    }
                }

                if (onRefresh == null)
                {
                    return;
                }

                using (new EditorGUI.DisabledScope(isRefreshing))
                {
                    if (GUILayout.Button(new GUIContent("↻", "Refresh available options"), GUILayout.Width(25)))
                    {
                        onRefresh.Invoke();
                    }
                }
            }
        }

        /// <summary>
        /// Draws a vision settings foldout with common properties.
        /// </summary>
        /// <param name="isOpen">Foldout state reference.</param>
        /// <param name="supportsVision">Supports vision property (can be null to skip).</param>
        /// <param name="inlineRemoteImages">Inline remote images property (can be null to skip).</param>
        /// <param name="resolveRemoteRedirects">Resolve redirects property (can be null to skip).</param>
        /// <param name="maxInlineBytes">Max inline bytes property (can be null to skip).</param>
        /// <param name="showSupportsVision">Whether to show the "Supports Vision" checkbox.</param>
        protected static void DrawVisionSettingsFoldout(ref bool isOpen, SerializedProperty supportsVision,
            SerializedProperty inlineRemoteImages, SerializedProperty resolveRemoteRedirects,
            SerializedProperty maxInlineBytes, bool showSupportsVision = true)
        {
            isOpen = EditorGUILayout.BeginFoldoutHeaderGroup(isOpen, "Chat / Vision Settings");
            if (isOpen)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    if (showSupportsVision && supportsVision != null)
                    {
                        EditorGUILayout.PropertyField(supportsVision,
                            new GUIContent("Supports Vision", "Enable if your model expects/accepts images."));
                    }

                    if (inlineRemoteImages != null)
                    {
                        EditorGUILayout.PropertyField(inlineRemoteImages, new GUIContent("Inline Remote Images",
                            "If ON, http(s) image URLs are fetched locally and sent as base64."));
                    }

                    if (resolveRemoteRedirects != null)
                    {
                        using (new EditorGUI.DisabledScope(inlineRemoteImages?.boolValue ?? false))
                        {
                            EditorGUILayout.PropertyField(resolveRemoteRedirects, new GUIContent("Resolve Redirects",
                                "If NOT inlining, resolve redirects locally and send the final URL."));
                        }
                    }

                    if (maxInlineBytes != null)
                    {
                        EditorGUILayout.PropertyField(maxInlineBytes, new GUIContent("Max Inline Bytes",
                            "Maximum bytes to download per image when inlining."));
                    }
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>
        /// Gets the cache key for storing models for a specific provider.
        /// </summary>
        /// <param name="providerId">Provider identifier (e.g., "OpenAI", "LlamaAPI").</param>
        /// <returns>EditorPrefs key for model cache.</returns>
        private static string GetModelCacheKey(string providerId)
        {
            return $"{CacheKeyPrefix}{providerId}";
        }

        /// <summary>
        /// Gets the cache timestamp key for a specific provider.
        /// </summary>
        /// <param name="providerId">Provider identifier (e.g., "OpenAI", "LlamaAPI").</param>
        /// <returns>EditorPrefs key for cache timestamp.</returns>
        private static string GetCacheTimestampKey(string providerId)
        {
            return $"{CacheTimestampPrefix}{providerId}";
        }

        /// <summary>
        /// Checks if the cached models are still valid (not expired).
        /// </summary>
        /// <param name="providerId">Provider identifier.</param>
        /// <returns>True if cache exists and is still valid.</returns>
        private static bool IsCacheValid(string providerId)
        {
            var timestampKey = GetCacheTimestampKey(providerId);
            if (!EditorPrefs.HasKey(timestampKey))
            {
                return false;
            }

            var lastFetchTime = EditorPrefs.GetFloat(timestampKey, 0f);
            var timeSinceLastFetch = EditorApplication.timeSinceStartup - lastFetchTime;
            return timeSinceLastFetch < CacheExpirationSeconds;
        }

        /// <summary>
        /// Saves fetched models to EditorPrefs cache.
        /// </summary>
        /// <param name="providerId">Provider identifier.</param>
        /// <param name="models">List of model IDs to cache.</param>
        /// <param name="modelData">Dictionary of model metadata to cache.</param>
        protected static void SaveModelsToCache(string providerId, List<string> models,
            Dictionary<string, ModelData> modelData)
        {
            if (models == null || models.Count == 0)
            {
                return;
            }

            try
            {
                var cacheData = new CachedModelsData
                {
                    models = models.ToArray(),
                    modelMetadata = new List<ModelData>()
                };

                if (modelData != null)
                {
                    foreach (var kvp in modelData)
                    {
                        cacheData.modelMetadata.Add(kvp.Value);
                    }
                }

                var json = JsonUtility.ToJson(cacheData);
                EditorPrefs.SetString(GetModelCacheKey(providerId), json);
                EditorPrefs.SetFloat(GetCacheTimestampKey(providerId), (float)EditorApplication.timeSinceStartup);

                Debug.Log($"[{providerId}] Saved {models.Count} models to cache");
            }
            catch (Exception e)
            {
                IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "ai-provider-cache-save-failed",
                    $"[{providerId}] Failed to save models to cache: {e.Message}");
            }
        }

        /// <summary>
        /// Loads cached models from EditorPrefs.
        /// </summary>
        /// <param name="providerId">Provider identifier.</param>
        /// <param name="models">Output list of model IDs.</param>
        /// <param name="modelData">Output dictionary of model metadata.</param>
        /// <returns>True if cache was successfully loaded.</returns>
        protected static bool TryLoadModelsFromCache(string providerId, out List<string> models,
            out Dictionary<string, ModelData> modelData)
        {
            models = null;
            modelData = null;

            try
            {
                var cacheKey = GetModelCacheKey(providerId);
                if (!EditorPrefs.HasKey(cacheKey))
                {
                    return false;
                }

                var json = EditorPrefs.GetString(cacheKey, string.Empty);
                if (string.IsNullOrEmpty(json))
                {
                    return false;
                }

                var cacheData = JsonUtility.FromJson<CachedModelsData>(json);
                if (cacheData?.models == null || cacheData.models.Length == 0)
                {
                    return false;
                }

                models = new List<string>(cacheData.models);
                modelData = new Dictionary<string, ModelData>();

                if (cacheData.modelMetadata != null)
                {
                    foreach (var meta in cacheData.modelMetadata)
                    {
                        if (!string.IsNullOrEmpty(meta.id))
                        {
                            modelData[meta.id] = meta;
                        }
                    }
                }

                if (!models.Contains(CustomOption))
                {
                    models.Add(CustomOption);
                }

                Debug.Log($"[{providerId}] Loaded {models.Count} models from cache");
                return true;
            }
            catch (Exception e)
            {
                IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "ai-provider-cache-load-failed",
                    $"[{providerId}] Failed to load models from cache: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads cached models on first inspector open if cache is valid, or automatically fetches if API key is available.
        /// Call this in OnEnable() after InitializeCredentialStorage().
        /// </summary>
        /// <param name="providerId">Provider identifier.</param>
        /// <param name="fetchModelsCallback">Callback to fetch models if cache is expired or doesn't exist.</param>
        protected void InitializeModelCache(string providerId, Action fetchModelsCallback)
        {
            if (IsCacheValid(providerId))
            {
                if (TryLoadModelsFromCache(providerId, out var models, out var modelData))
                {
                    FetchedModels = models;
                    FetchedModelData = modelData;
                    return;
                }
            }

            // Cache expired or missing - don't auto-fetch, let user manually refresh
            Debug.Log($"[{providerId}] No valid cache found. Click the refresh button to fetch models.");
        }

        /// <summary>
        /// Flexible version that supports custom authentication headers and response parsers.
        /// </summary>
        /// <param name="baseUrl">The base URL for the models endpoint (will append "/models").</param>
        /// <param name="providerName">Provider name for logging and caching (e.g., "OpenAI", "LlamaAPI", "ElevenLabs").</param>
        /// <param name="configureRequest">Action to configure the request (e.g., add custom headers).</param>
        /// <param name="parseResponse">Function to parse the JSON response and return a list of model IDs.</param>
        protected void FetchModelsFromAPIWithCache(string baseUrl, string providerName,
            Action<UnityWebRequest> configureRequest, Func<string, List<string>> parseResponse)
        {
            if (IsFetchingModels)
            {
                IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "ai-provider-concurrent-fetch",
                    $"[{providerName}] Already fetching models, ignoring concurrent request.");
                return;
            }

            IsFetchingModels = true;
            FetchError = null;
            Repaint();

            var url = $"{baseUrl.TrimEnd('/')}/models";
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 30
            };

            configureRequest?.Invoke(req);

            var op = req.SendWebRequest();

            EditorApplication.CallbackFunction updateTick = null;
            updateTick = () =>
            {
                if (!op.isDone)
                {
                    return;
                }

                EditorApplication.update -= updateTick;

                try
                {
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        var responseText = req.downloadHandler.text;
                        var models = parseResponse(responseText);

                        if (models is { Count: > 0 })
                        {
                            FetchedModels = new List<string>(models) { CustomOption };
                            SaveModelsToCache(providerName, FetchedModels, null);
                            FetchError = null;
                        }
                        else
                        {
                            FetchError = "No models found in API response";
                        }
                    }
                    else
                    {
                        var responseText = req.downloadHandler?.text ?? "";
                        FetchError = $"HTTP {req.responseCode}: {req.error}";
                        IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "ai-provider-fetch-models-failed",
                            $"[{providerName}] Failed to fetch models: {FetchError}\nResponse body: {responseText}");
                    }
                }
                catch (Exception e)
                {
                    FetchError = $"Parse error: {e.Message}";
                    IssueTracker.TrackError(IssueTracker.SDK.BuildingBlocks, "ai-provider-parse-models-failed",
                        $"[{providerName}] Error parsing models response: {e}");
                }
                finally
                {
                    IsFetchingModels = false;
                    req.Dispose();
                    Repaint();
                }
            };

            EditorApplication.update += updateTick;
        }

        /// <summary>
        /// Fetches models using Bearer authentication.
        /// </summary>
        /// <param name="endpointProperty">The serialized property containing the endpoint URL.</param>
        /// <param name="defaultBaseUrl">The default base URL to use if the property is empty (e.g., "https://api.provider.com/v1").</param>
        /// <param name="providerName">Provider name for logging and caching.</param>
        protected void FetchModelsWithBearerAuth(SerializedProperty endpointProperty, string defaultBaseUrl,
            string providerName)
        {
            FetchModelsWithAuth(endpointProperty, defaultBaseUrl, providerName,
                (req, key) => req.SetRequestHeader("Authorization", $"Bearer {key}"));
        }

        /// <summary>
        /// Fetches models using custom authentication.
        /// </summary>
        /// <param name="endpointProperty">The serialized property containing the endpoint URL.</param>
        /// <param name="defaultBaseUrl">The default base URL to use if the property is empty (e.g., "https://api.provider.com/v1").</param>
        /// <param name="providerName">Provider name for logging and caching.</param>
        /// <param name="configureAuth">Action to configure authentication headers (receives request and API key).</param>
        private void FetchModelsWithAuth(SerializedProperty endpointProperty, string defaultBaseUrl,
            string providerName,
            Action<UnityWebRequest, string> configureAuth)
        {
            var apiKey = ApiKeyProperty?.stringValue?.Trim();
            if (string.IsNullOrEmpty(apiKey))
            {
                FetchError = "API key is required to fetch models";
                return;
            }

            var endpoint = endpointProperty?.stringValue?.Trim();
            string baseUrl;

            if (string.IsNullOrEmpty(endpoint))
            {
                baseUrl = defaultBaseUrl;
            }
            else
            {
                var uri = new Uri(endpoint);
                var pathSegments = uri.AbsolutePath.TrimEnd('/').Split('/');

                var versionPath = "/v1";
                for (var i = 0; i < pathSegments.Length; i++)
                {
                    if (string.IsNullOrEmpty(pathSegments[i]) || !pathSegments[i].StartsWith("v") ||
                        pathSegments[i].Length < 2 || !char.IsDigit(pathSegments[i][1]))
                    {
                        continue;
                    }

                    var pathList = new List<string>();
                    for (var j = 0; j <= i; j++)
                    {
                        if (!string.IsNullOrEmpty(pathSegments[j]))
                        {
                            pathList.Add(pathSegments[j]);
                        }
                    }

                    versionPath = "/" + string.Join("/", pathList.ToArray());
                    break;
                }

                var portPart = (uri.Port != 80 && uri.Port != 443) ? $":{uri.Port}" : "";
                baseUrl = $"{uri.Scheme}://{uri.Host}{portPart}{versionPath}";
            }

            FetchModelsFromAPIWithCache(baseUrl, providerName,
                req => configureAuth(req, apiKey),
                json =>
                {
                    var response = JsonUtility.FromJson<ModelsResponse>(json);
                    if (response is not { data: { Count: > 0 } })
                    {
                        return null;
                    }

                    var models = new List<string>();
                    response.data.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.Ordinal));
                    foreach (var model in response.data)
                    {
                        models.Add(model.id);
                    }

                    return models;
                });
        }

        [Serializable]
        private class CachedModelsData
        {
            public string[] models;
            public List<ModelData> modelMetadata;
        }
    }
}
