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
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CustomEditor(typeof(HuggingFaceProvider))]
    public class HuggingFaceProviderEditor : UnityEditor.Editor
    {
        private SerializedProperty _apiKey;
        private SerializedProperty _modelId;
        private SerializedProperty _endpoint;
        private SerializedProperty _supportsVision;
        private SerializedProperty _inlineRemoteImages;
        private SerializedProperty _resolveRemoteRedirects;
        private SerializedProperty _maxInlineBytes;

        private bool _visionOpen;

        private enum HealthState
        {
            Unknown,
            Healthy,
            LoadingOrCold, // 503 from HF when spinning up
            ReachableBadRequest, // 4xx like 400/404/422 (infra up, payload wrong)
            AuthError, // 401/403
            RateLimited, // 429
            ServerError, // 5xx (non-503)
            NetworkError // connection/transport issue
        }

        private enum TokenState
        {
            NotChecked,
            Valid,
            Invalid,
            NetworkError
        }

        private HealthState _healthState = HealthState.Unknown;
        private string _healthDetails = "";
        private DateTime _healthCheckedAt = DateTime.MinValue;
        private bool _isChecking;
        private TokenState _tokenState = TokenState.NotChecked;
        private string _tokenDetails = "";
        private bool _isValidatingToken;

        // Minimal public sample image (tiny, stable) for object-detection probe
        private const string kTestImageUrl =
            "https://huggingface.co/datasets/huggingface/documentation-images/resolve/main/coco_sample.png";

        // Small solid color textures for badges
        private Texture2D _badgeHealthy, _badgeWarn, _badgeError, _badgeNeutral;

        // Active request plumbing (Editor-safe, no async/await)
        private UnityWebRequest _activeRequest;
        private UnityWebRequestAsyncOperation _activeOp;
        private EditorApplication.CallbackFunction _updateTick;

        private void OnEnable()
        {
            _apiKey = serializedObject.FindProperty("apiKey");
            _modelId = serializedObject.FindProperty("modelId");
            _endpoint = serializedObject.FindProperty("endpoint");

            _supportsVision = serializedObject.FindProperty("supportsVision");
            _inlineRemoteImages = serializedObject.FindProperty("inlineRemoteImages");
            _resolveRemoteRedirects = serializedObject.FindProperty("resolveRemoteRedirects");
            _maxInlineBytes = serializedObject.FindProperty("maxInlineBytes");
        }

        private void OnDisable()
        {
            if (_updateTick != null) EditorApplication.update -= _updateTick;
            _updateTick = null;

            if (_activeRequest != null)
            {
                try
                {
                    _activeRequest.Abort();
                }
                catch
                {
                    // ignored
                }

                _activeRequest.Dispose();
                _activeRequest = null;
            }

            DestroyImmediate(_badgeHealthy);
            DestroyImmediate(_badgeWarn);
            DestroyImmediate(_badgeError);
            DestroyImmediate(_badgeNeutral);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("HuggingFace Inference Provider", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_apiKey,
                    new GUIContent("API Key", "Your provider API token (HF/Groq/etc)."));

                GUILayout.Space(6);
                string apiKeyVal = _apiKey?.stringValue?.Trim();
                if (string.IsNullOrEmpty(apiKeyVal))
                {
                    using (new EditorGUI.DisabledScope(_isValidatingToken))
                    {
                        if (GUILayout.Button(new GUIContent("Get Token…", "Create a new Hugging Face access token"),
                                GUILayout.Width(95)))
                        {
                            Application.OpenURL("https://huggingface.co/settings/tokens");
                        }
                    }
                }
                else
                {
                    using (new EditorGUI.DisabledScope(_isValidatingToken))
                    {
                        if (GUILayout.Button(new GUIContent("Validate", "Validate your Hugging Face token"),
                                GUILayout.Width(70)))
                        {
                            // Apply edits first so we read the latest value
                            serializedObject.ApplyModifiedPropertiesWithoutUndo();
                            BeginValidateToken(apiKeyVal);
                        }
                    }

                    // Small token badge with tooltip
                    var (tokColor, tokTip) = GetTokenBadgeAndTooltip();
                    var content = new GUIContent("", tokTip);
                    DrawBadge(tokColor, content);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(_endpoint, new GUIContent("Endpoint",
                "Examples:\n- HF Router: https://router.huggingface.co/hf-inference/models/<modelId>\n- OpenAI-style: https://api.groq.com/openai/v1/chat/completions"));

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_modelId, new GUIContent("Model ID",
                    "e.g. meta-llama/Llama-3.2-11B-Vision-Instruct, facebook/detr-resnet-101, llama-3.3-70b-versatile"));

                GUILayout.Space(6);

                using (new EditorGUI.DisabledScope(_isChecking))
                {
                    if (GUILayout.Button(new GUIContent("Check", "Probe the endpoint for this model"),
                            GUILayout.Width(60)))
                    {
                        // Apply edits first so we read the latest values
                        serializedObject.ApplyModifiedPropertiesWithoutUndo();
                        BeginHealthCheck();
                    }
                }

                // Low-key badge w/ tooltip
                var (badgeColor, healthTip) = GetHealthBadgeAndTooltip();
                var badgeContent = new GUIContent("", healthTip);
                DrawBadge(badgeColor, badgeContent);
            }

            EditorGUILayout.Space();
            _visionOpen = EditorGUILayout.BeginFoldoutHeaderGroup(_visionOpen, "Chat / Vision Settings");
            if (_visionOpen)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.PropertyField(_supportsVision, new GUIContent("Supports Vision",
                        "Enable if your model expects/accepts images (e.g., DETR, VLM)."));
                    EditorGUILayout.PropertyField(_inlineRemoteImages, new GUIContent("Inline Remote Images",
                        "If ON, http(s) image URLs are fetched locally and embedded as data URIs."));
                    using (new EditorGUI.DisabledScope(_inlineRemoteImages.boolValue))
                    {
                        EditorGUILayout.PropertyField(_resolveRemoteRedirects, new GUIContent("Resolve Redirects",
                            "If NOT inlining, resolve redirects and send the final URL."));
                    }

                    EditorGUILayout.PropertyField(_maxInlineBytes, new GUIContent("Max Inline Bytes",
                        "Maximum bytes to download per image when inlining."));
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }

        private void BeginHealthCheck()
        {
            string endpoint = _endpoint?.stringValue?.Trim();
            string modelId = _modelId?.stringValue?.Trim();
            string apiKey = _apiKey?.stringValue?.Trim();
            bool supportsVision = _supportsVision != null && _supportsVision.boolValue;

            _isChecking = true;
            _healthDetails = "";
            Repaint();

            if (string.IsNullOrEmpty(endpoint))
            {
                _healthState = HealthState.NetworkError;
                _healthDetails = "No endpoint configured.";
                _isChecking = false;
                _healthCheckedAt = DateTime.UtcNow;
                Repaint();
                return;
            }

            bool isOpenAIStyle = endpoint.Contains("/v1/chat/completions") || endpoint.EndsWith("/chat/completions");
            bool isHfRouter = endpoint.Contains("router.huggingface.co") && endpoint.Contains("/hf-inference/models/");

            string jsonBody;
            bool addWaitHeader = false;

            if (isOpenAIStyle)
            {
                var safeModel = string.IsNullOrEmpty(modelId) ? "" : modelId.Replace("\"", "\\\"");
                jsonBody =
                    "{\n" +
                    $"  \"model\": \"{safeModel}\",\n" +
                    "  \"messages\": [{\"role\":\"user\",\"content\":\"ping\"}],\n" +
                    "  \"max_tokens\": 1\n" +
                    "}";
            }
            else if (isHfRouter && supportsVision)
            {
                jsonBody = "{\n" + $"  \"inputs\": \"{kTestImageUrl}\"\n" + "}";
                addWaitHeader = true; // x-wait-for-model: true
            }
            else
            {
                jsonBody = "{}"; // generic probe
            }

            var req = BuildPostJson(endpoint, jsonBody, apiKey, addWaitHeader);
            StartRequest(req, (ok, code, text, err) =>
            {
                _healthCheckedAt = DateTime.UtcNow;
                _healthDetails =
                    $"HTTP {(int)code} — {code}\n{(string.IsNullOrWhiteSpace(text) ? "" : TrimForInspector(text))}";

                if (!ok)
                {
                    _healthState = HealthState.NetworkError;
                }
                else
                {
                    int ic = (int)code;
                    if (ic == 200) _healthState = HealthState.Healthy;
                    else if (ic == 503) _healthState = HealthState.LoadingOrCold;
                    else if (ic == 401 || ic == 403) _healthState = HealthState.AuthError;
                    else if (ic == 429) _healthState = HealthState.RateLimited;
                    else if (ic >= 500) _healthState = HealthState.ServerError;
                    else if (ic >= 400 && ic < 500) _healthState = HealthState.ReachableBadRequest;
                    else _healthState = HealthState.Unknown;
                }

                _isChecking = false;
                Repaint();
            });
        }

        private void BeginValidateToken(string apiKeyVal)
        {
            _isValidatingToken = true;
            _tokenDetails = "";
            Repaint();

            var req = BuildGet("https://huggingface.co/api/whoami-v2", apiKeyVal);
            StartRequest(req, (ok, code, text, err) =>
            {
                if (!ok)
                {
                    _tokenState = TokenState.NetworkError;
                    _tokenDetails = "Network error while validating token.";
                }
                else
                {
                    switch ((int)code)
                    {
                        case 200:
                            _tokenState = TokenState.Valid;
                            _tokenDetails = ""; // could parse org/user if desired
                            break;
                        case 401:
                        case 403:
                            _tokenState = TokenState.Invalid;
                            _tokenDetails = TrimForInspector(text);
                            break;
                        default:
                            _tokenState = TokenState.NetworkError;
                            _tokenDetails = $"Unexpected response {code}\n" + TrimForInspector(text);
                            break;
                    }
                }

                _isValidatingToken = false;
                Repaint();
            });
        }

        private static UnityWebRequest BuildPostJson(string url, string json, string bearerToken, bool addWaitHeader)
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var bodyRaw = Encoding.UTF8.GetBytes(json ?? "{}");

            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrEmpty(bearerToken))
            {
                req.SetRequestHeader("Authorization", "Bearer " + bearerToken);
            }

            if (addWaitHeader)
            {
                req.SetRequestHeader("x-wait-for-model", "true");
            }

            return req;
        }

        private UnityWebRequest BuildGet(string url, string bearerToken)
        {
            var req = UnityWebRequest.Get(url);
            req.downloadHandler = new DownloadHandlerBuffer();
            if (!string.IsNullOrEmpty(bearerToken))
                req.SetRequestHeader("Authorization", "Bearer " + bearerToken);
            return req;
        }

        private void StartRequest(UnityWebRequest req, Action<bool, long, string, string> onDone)
        {
            // Cancel any previous in-flight request
            if (_updateTick != null) EditorApplication.update -= _updateTick;
            _updateTick = null;

            if (_activeRequest != null)
            {
                try
                {
                    _activeRequest.Abort();
                }
                catch
                {
                    // ignored
                }

                _activeRequest.Dispose();
                _activeRequest = null;
            }

            _activeRequest = req;
            _activeOp = req.SendWebRequest();

            _updateTick = () =>
            {
                if (_activeOp == null || !_activeOp.isDone) return;

                EditorApplication.update -= _updateTick;
                _updateTick = null;

                bool networkError = req.result == UnityWebRequest.Result.ConnectionError;
                bool protocolError = req.result == UnityWebRequest.Result.ProtocolError;
                bool dataProcessingError = req.result == UnityWebRequest.Result.DataProcessingError;
                string text = req.downloadHandler != null ? req.downloadHandler.text : "";
                long code = req.responseCode;

                // ok = got *some* response (even 4xx/5xx) → infra reachable.
                bool ok = !networkError && !dataProcessingError;

                try
                {
                    onDone?.Invoke(ok, code, text, protocolError ? "http_error" : null);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                req.Dispose();
                _activeRequest = null;
                _activeOp = null;

                Repaint();
            };

            EditorApplication.update += _updateTick;
            Repaint(); // so the "Checking…" disable state is reflected immediately
        }

        private (Color, string) GetHealthBadgeAndTooltip()
        {
            string when = _healthCheckedAt == DateTime.MinValue
                ? ""
                : $" • Checked {GetRelativeTime(_healthCheckedAt)}";
            string detail = string.IsNullOrEmpty(_healthDetails) ? "" : ("\n" + _healthDetails);

            switch (_healthState)
            {
                case HealthState.Healthy:
                    return (new Color(0.25f, 0.75f, 0.35f), $"200: Healthy{when}"); // green
                case HealthState.LoadingOrCold:
                    return (new Color(1.00f, 0.70f, 0.15f), $"Loading / cold (503){when}{detail}"); // amber
                case HealthState.ReachableBadRequest:
                    return (new Color(1.00f, 0.85f, 0.20f),
                        $"404: Reachable endpoint but invalid model id{when}"); // yellow
                case HealthState.AuthError:
                    return (new Color(0.85f, 0.25f, 0.25f), $"Auth error (401/403){when}{detail}"); // red
                case HealthState.RateLimited:
                    return (new Color(1.00f, 0.70f, 0.15f), $"Rate limited (429){when}{detail}"); // amber
                case HealthState.ServerError:
                    return (new Color(0.85f, 0.25f, 0.25f), $"Server error (5xx){when}{detail}"); // red
                case HealthState.NetworkError:
                    return (new Color(0.85f, 0.25f, 0.25f), $"Network error{when}{detail}"); // red
                case HealthState.Unknown:
                default:
                    return (new Color(0.55f, 0.60f, 0.70f), "Status unknown. Click Check."); // gray/blue
            }
        }

        private (Color, string) GetTokenBadgeAndTooltip()
        {
            switch (_tokenState)
            {
                case TokenState.Valid:
                    return (new Color(0.25f, 0.75f, 0.35f),
                        "Access token is a valid Hugging Face access token."); // green
                case TokenState.Invalid:
                    return (new Color(1.00f, 0.85f, 0.20f),
                        "Token appears invalid for the Hugging Face provider. But it still might be a valid inference " +
                        "provider token (e.g., Groq, Nebius AI, etc.) Please check manually to make sure");
                case TokenState.NetworkError:
                    return (new Color(1.00f, 0.70f, 0.15f),
                        $"Token check failed (network).\n{_tokenDetails}"); // amber
                case TokenState.NotChecked:
                default:
                    return (new Color(0.55f, 0.60f, 0.70f),
                        "Token not validated yet. Tip: only Hugging Face tokens can be validated via whoami-v2; " +
                        "provider-specific tokens may not validate here."); // gray/blue
            }
        }

        private void DrawBadge(Color color, GUIContent content)
        {
            var rect = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
            rect.y += 4f; // vertical alignment to match field row

            if (Event.current.type == EventType.Repaint)
            {
                // Use Handles to draw a circle
                Handles.BeginGUI();
                Color prev = Handles.color;
                Handles.color = color;
                var center = rect.center;
                float radius = rect.width * 0.5f;
                Handles.DrawSolidDisc(center, Vector3.forward, radius);
                Handles.color = prev;
                Handles.EndGUI();
            }

            // Tooltip hit area (invisible)
            if (!string.IsNullOrEmpty(content.tooltip))
            {
                GUI.Label(rect, new GUIContent(string.Empty, content.tooltip), GUIStyle.none);
            }
        }

        private static Texture2D MakeTex(Color c)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, c);
            tex.Apply();
            return tex;
        }

        private static string GetRelativeTime(DateTime t)
        {
            var span = DateTime.UtcNow - t.ToUniversalTime();
            if (span.TotalSeconds < 60) return $"{Mathf.RoundToInt((float)span.TotalSeconds)}s ago";
            if (span.TotalMinutes < 60) return $"{Mathf.RoundToInt((float)span.TotalMinutes)}m ago";
            return span.TotalHours < 24
                ? $"{Mathf.RoundToInt((float)span.TotalHours)}h ago"
                : t.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        private static string TrimForInspector(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            const int max = 900;
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "\n…(truncated)…";
        }
    }
}
