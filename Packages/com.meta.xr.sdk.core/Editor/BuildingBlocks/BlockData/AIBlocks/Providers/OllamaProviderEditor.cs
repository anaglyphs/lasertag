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
using UnityEngine.Networking;
using UnityEditor;
using UnityEngine;
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks.Providers
{
    [Serializable]
    internal class OllamaTagsResponse
    {
        public List<OllamaModelTag> models;
    }

    [Serializable]
    internal class OllamaModelTag
    {
        public string name;
        public OllamaModelDetails details;
    }

    [Serializable]
    internal class OllamaModelDetails
    {
        public string family;
        public string parameterSize;
        public string quantizationLevel;
    }

    [CustomEditor(typeof(OllamaProvider))]
    public class OllamaProviderEditor : UnityEditor.Editor
    {
        private const string KEditorPrefCache = "OllamaProviderEditor.ModelCache";
        private const string KEditorPrefStamp = "OllamaProviderEditor.ModelCacheTime";
        private const string KEditorPrefEndpoint = "OllamaProviderEditor.EndpointFallback";
        private const double KCacheSeconds = 60.0;

        private SerializedProperty _propModel;
        private SerializedProperty _propEndpoint;

        private bool _aboutOpen = true;
        private bool _isFetching;
        private string _fetchError;
        private string _lastFetchUrl;
        private int _lastFetchCount;

        private readonly List<string> _all = new();
        private int _popupIndex = -1; // -1 => custom
        private const string KCustomLabel = "Custom (type below)";

        private string _scheme = "http";
        private string _host = "localhost";
        private int _port = 11434;
        private string _path = "/api/generate";

        void OnEnable()
        {
            _propModel = serializedObject.FindProperty("model") ?? serializedObject.FindProperty("Model");
            _propEndpoint = serializedObject.FindProperty("endpointPath")
                            ?? serializedObject.FindProperty("EndpointPath")
                            ?? serializedObject.FindProperty("endpoint")
                            ?? serializedObject.FindProperty("Endpoint");

            const string defaultEndpoint = "http://localhost:11434/api/generate";
            if (_propEndpoint == null)
            {
                if (string.IsNullOrEmpty(EditorPrefs.GetString(KEditorPrefEndpoint, string.Empty)))
                    EditorPrefs.SetString(KEditorPrefEndpoint, defaultEndpoint);
                ParseEndpoint(EditorPrefs.GetString(KEditorPrefEndpoint, defaultEndpoint));
            }
            else
            {
                if (string.IsNullOrEmpty(_propEndpoint.stringValue))
                {
                    _propEndpoint.stringValue = defaultEndpoint;
                    serializedObject.ApplyModifiedProperties();
                }

                ParseEndpoint(_propEndpoint.stringValue);
            }

            LoadCache();
            SnapPopupToCurrent();

            if (ShouldRefresh()) FetchModels();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("Ollama Provider", EditorStyles.boldLabel);

            _aboutOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_aboutOpen, "Setup");
            if (_aboutOpen)
            {
                EditorGUILayout.HelpBox(
                    "Models are fetched live from your Ollama daemon via HTTP GET /api/tags.\n" +
                    "Install / run models:\n" +
                    "  • ollama pull <model>\n" +
                    "  • ollama run <model>\n" +
                    "  • ollama list\n\n" +
                    "Set Host/Port/Path or paste a full Model Endpoint URL (e.g., http://192.168.1.10:12345/api/generate).",
                    MessageType.Info);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            using (new EditorGUILayout.VerticalScope("box"))
            {
                // --- Connection fields ---
                EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);

                string urlBefore = GetEndpointString();
                string urlAfter = EditorGUILayout.TextField(
                    new GUIContent("Model Endpoint", "Full URL, e.g. http://host:11434/api/generate"),
                    urlBefore
                );
                if (urlAfter != urlBefore)
                {
                    if (ParseEndpoint(urlAfter)) SetEndpointString(BuildEndpoint());
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    string hostBefore = _host;
                    string hostAfter = EditorGUILayout.TextField(
                        new GUIContent("Host", "Hostname or IP"), hostBefore);
                    if (hostAfter != hostBefore)
                    {
                        _host = string.IsNullOrWhiteSpace(hostAfter) ? "localhost" : hostAfter.Trim();
                        SetEndpointString(BuildEndpoint());
                    }

                    int portBefore = _port;
                    int portAfter = EditorGUILayout.IntField(
                        new GUIContent("Port", "TCP port (default 11434)"), portBefore);
                    if (portAfter != portBefore)
                    {
                        _port = Mathf.Clamp(portAfter, 1, 65535);
                        SetEndpointString(BuildEndpoint());
                    }

                    string pathBefore = _path;
                    string pathAfter = EditorGUILayout.TextField(
                        new GUIContent("Path", "API path (default /api/generate)"), pathBefore);
                    if (pathAfter != pathBefore)
                    {
                        _path = SanitizePath(pathAfter);
                        SetEndpointString(BuildEndpoint());
                    }
                }

                EditorGUILayout.Space(6);

                // --- Refresh row ---
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(_isFetching))
                    {
                        if (GUILayout.Button(new GUIContent("Refresh Models", "GET /api/tags"), GUILayout.Width(130)))
                        {
                            FetchModels();
                        }
                    }
                }

                if (_isFetching)
                {
                    EditorGUILayout.HelpBox("Fetching models…", MessageType.Info);
                }
                else if (!string.IsNullOrEmpty(_fetchError))
                {
                    EditorGUILayout.HelpBox(_fetchError, MessageType.Warning);
                }
                else if (!string.IsNullOrEmpty(_lastFetchUrl))
                {
                    EditorGUILayout.HelpBox($"Loaded {_lastFetchCount} tag(s) from {_lastFetchUrl}", MessageType.None);
                }

                // --- Model selection ---
                var options = new List<string>(_all.Count + 1) { KCustomLabel };
                options.AddRange(_all);

                var newIdx = EditorGUILayout.Popup(
                    new GUIContent("Model", "Pick from local Ollama tags, or choose Custom."),
                    Mathf.Max(0, _popupIndex + 1),
                    options.ToArray()
                ) - 1;

                if (newIdx != _popupIndex)
                {
                    _popupIndex = newIdx;
                    if (_popupIndex >= 0 && _popupIndex < _all.Count && _propModel != null)
                        _propModel.stringValue = _all[_popupIndex];
                }

                // Show Model Name text field only when Custom selected
                if (_popupIndex < 0)
                {
                    var before = _propModel != null ? _propModel.stringValue : string.Empty;
                    var after = EditorGUILayout.TextField(
                        new GUIContent("Model Name", "Type a model tag (e.g., llava:latest, gemma3)"), before);
                    if (_propModel != null && after != before)
                    {
                        _propModel.stringValue = after;
                        SnapPopupToCurrent();
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static string SanitizePath(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "/api/generate";
            var t = p.Trim();
            if (!t.StartsWith("/")) t = "/" + t;
            return t;
        }

        private string BuildEndpoint()
        {
            return $"{_scheme}://{_host}:{_port}{SanitizePath(_path)}";
        }

        private bool ParseEndpoint(string endpoint)
        {
            try
            {
                if (!string.IsNullOrEmpty(endpoint) && Uri.TryCreate(endpoint, UriKind.Absolute, out var abs))
                {
                    _scheme = string.IsNullOrEmpty(abs.Scheme) ? "http" : abs.Scheme;
                    _host = string.IsNullOrEmpty(abs.Host) ? "localhost" : abs.Host;
                    _port = abs.IsDefaultPort ? 11434 : abs.Port;
                    _path = SanitizePath(string.IsNullOrEmpty(abs.AbsolutePath) ? "/api/generate" : abs.AbsolutePath);
                    return true;
                }
            }
            catch
            {
            }

            _scheme = "http";
            _host = "localhost";
            _port = 11434;
            _path = "/api/generate";
            return false;
        }

        private string GetEndpointString()
        {
            if (_propEndpoint != null) return _propEndpoint.stringValue;
            return EditorPrefs.GetString(KEditorPrefEndpoint, "http://localhost:11434/api/generate");
        }

        private void SetEndpointString(string value)
        {
            ParseEndpoint(value);
            if (_propEndpoint != null) _propEndpoint.stringValue = BuildEndpoint();
            else EditorPrefs.SetString(KEditorPrefEndpoint, BuildEndpoint());
        }

        private bool ShouldRefresh()
        {
            if (_all.Count == 0) return true;
            double last = EditorPrefs.GetFloat(KEditorPrefStamp, 0f);
            return (EditorApplication.timeSinceStartup - last) > KCacheSeconds;
        }

        private void FetchModels()
        {
            _isFetching = true;
            _fetchError = string.Empty;
            _lastFetchUrl = null;
            _lastFetchCount = 0;
            Repaint();

            var baseUrl = $"{_scheme}://{_host}:{_port}";
            var url = $"{baseUrl}/api/tags";
            _lastFetchUrl = url;

            var req = UnityWebRequest.Get(url);
            req.timeout = 5;

            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        _fetchError = $"Failed to fetch {url}\n{req.error}";
                        return;
                    }

                    var text = req.downloadHandler.text;
                    var parsed = JsonUtility.FromJson<OllamaTagsResponse>(text);

                    _all.Clear();
                    if (parsed?.models != null)
                    {
                        foreach (var modelTag in parsed.models)
                            if (!string.IsNullOrEmpty(modelTag?.name))
                                _all.Add(modelTag.name);
                        _all.Sort(StringComparer.OrdinalIgnoreCase);
                    }

                    _lastFetchCount = _all.Count;

                    SaveCache();
                    SnapPopupToCurrent();
                }
                catch (Exception e)
                {
                    _fetchError = $"Error parsing /api/tags: {e.Message}";
                }
                finally
                {
                    _isFetching = false;
                    Repaint();
                    req.Dispose();
                }
            };
        }

        private void SnapPopupToCurrent()
        {
            _popupIndex = -1;
            if (_propModel == null) return;

            var current = _propModel.stringValue ?? string.Empty;
            if (string.IsNullOrEmpty(current)) return;

            for (var i = 0; i < _all.Count; i++)
            {
                if (string.Equals(_all[i], current, StringComparison.OrdinalIgnoreCase))
                {
                    _popupIndex = i;
                    break;
                }
            }
        }

        [Serializable]
        private class StringListWrapper
        {
            public string[] items;
        }

        private void LoadCache()
        {
            _all.Clear();
            var json = EditorPrefs.GetString(KEditorPrefCache, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var wrap = JsonUtility.FromJson<StringListWrapper>(json);
                if (wrap?.items != null) _all.AddRange(wrap.items);
            }
            catch
            {
            }
        }

        private void SaveCache()
        {
            var wrap = new StringListWrapper { items = _all.ToArray() };
            EditorPrefs.SetString(KEditorPrefCache, JsonUtility.ToJson(wrap));
            EditorPrefs.SetFloat(KEditorPrefStamp, (float)EditorApplication.timeSinceStartup);
        }
    }
}
