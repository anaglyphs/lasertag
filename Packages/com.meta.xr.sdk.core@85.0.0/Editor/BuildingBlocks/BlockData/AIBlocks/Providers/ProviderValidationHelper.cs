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
    /// <summary>
    /// Helper class for validating AI provider connections by testing actual API endpoints.
    /// Provides unified validation logic across all provider editors.
    /// </summary>
    public static class ProviderValidationHelper
    {
        public enum ValidationState
        {
            NotChecked,
            Success,
            AuthenticationError,
            NotFound,
            RateLimited,
            ServerError,
            NetworkError
        }

        public struct ValidationResult
        {
            public ValidationState State;
            public string Message;
            public int HttpCode;
        }

        private const string CacheKeyPrefix = "AIBlocks_ValidationCache_";

        /// <summary>
        /// Generates a unique cache key based on provider configuration.
        /// The key includes endpoint, model, and API key hash to detect configuration changes.
        /// </summary>
        private static string GetCacheKey(string endpoint, string apiKey, string model, string providerId)
        {
            var configHash = $"{endpoint}|{model}|{providerId}|{apiKey?.GetHashCode() ?? 0}".GetHashCode();
            return $"{CacheKeyPrefix}{configHash}";
        }

        /// <summary>
        /// Saves validation result to EditorPrefs cache.
        /// </summary>
        private static void SaveValidationCache(string cacheKey, ValidationResult result)
        {
            EditorPrefs.SetInt($"{cacheKey}_State", (int)result.State);
            EditorPrefs.SetString($"{cacheKey}_Message", result.Message ?? "");
            EditorPrefs.SetInt($"{cacheKey}_HttpCode", result.HttpCode);
        }

        /// <summary>
        /// Loads validation result from EditorPrefs cache.
        /// Returns null if no cached result exists.
        /// </summary>
        private static ValidationResult? LoadValidationCache(string cacheKey)
        {
            if (!EditorPrefs.HasKey($"{cacheKey}_State"))
            {
                return null;
            }

            return new ValidationResult
            {
                State = (ValidationState)EditorPrefs.GetInt($"{cacheKey}_State", (int)ValidationState.NotChecked),
                Message = EditorPrefs.GetString($"{cacheKey}_Message", ""),
                HttpCode = EditorPrefs.GetInt($"{cacheKey}_HttpCode", 0)
            };
        }

        /// <summary>
        /// Attempts to load cached validation result for the given configuration.
        /// Returns true if a cached result was found and loaded.
        /// </summary>
        public static bool TryGetCachedValidation(string endpoint, string apiKey, string model, string providerId,
            out ValidationResult result)
        {
            var cacheKey = GetCacheKey(endpoint, apiKey, model, providerId);
            var cached = LoadValidationCache(cacheKey);

            if (cached.HasValue)
            {
                result = cached.Value;
                return true;
            }

            result = new ValidationResult { State = ValidationState.NotChecked };
            return false;
        }

        /// <summary>
        /// Clears the cached validation result for the given configuration.
        /// </summary>
        public static void ClearValidationCache(string endpoint, string apiKey, string model, string providerId)
        {
            var cacheKey = GetCacheKey(endpoint, apiKey, model, providerId);
            EditorPrefs.DeleteKey($"{cacheKey}_State");
            EditorPrefs.DeleteKey($"{cacheKey}_Message");
            EditorPrefs.DeleteKey($"{cacheKey}_HttpCode");
        }

        /// <summary>
        /// Tests a provider connection by making an actual API call to the model endpoint.
        /// Uses ProviderId string for dynamic provider discovery without hardcoded enums.
        /// </summary>
        public static void TestConnection(
            string endpoint,
            string apiKey,
            string model,
            string providerId,
            Action<ValidationResult> onComplete,
            Action<UnityWebRequest, UnityWebRequestAsyncOperation, EditorApplication.CallbackFunction> saveState = null,
            Action cleanup = null)
        {
            cleanup?.Invoke();

            var result = new ValidationResult
            {
                State = ValidationState.NotChecked,
                Message = "",
                HttpCode = 0
            };

            if (string.IsNullOrEmpty(endpoint))
            {
                result.State = ValidationState.NetworkError;
                result.Message = "No endpoint configured.";
                onComplete?.Invoke(result);
                return;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                result.State = ValidationState.NotFound;
                result.Message = "✗ Missing API key";
                onComplete?.Invoke(result);
                return;
            }

            var isTranscriptionRequest = providerId == "OpenAI" && !string.IsNullOrEmpty(model) &&
                                         (model.ToLower().Contains("whisper") ||
                                          model.ToLower().Contains("transcribe"));

            var req = providerId switch
            {
                "OpenAI" => BuildOpenAIRequest(endpoint, apiKey, model),
                "HuggingFace" => BuildHuggingFaceRequest(endpoint, apiKey, model),
                "LlamaApi" => BuildLlamaApiRequest(endpoint, apiKey, model),
                "ElevenLabs" => BuildElevenLabsRequest(endpoint, apiKey, model),
                "Replicate" => BuildReplicateRequest(endpoint, apiKey, model),
                _ => null
            };

            if (req == null)
            {
                result.State = ValidationState.NetworkError;
                result.Message = $"Unsupported provider type: {providerId}";
                onComplete?.Invoke(result);
                return;
            }

            var op = req.SendWebRequest();

            EditorApplication.CallbackFunction updateTick = null;
            updateTick = () =>
            {
                try
                {
                    if (op is not { isDone: true })
                    {
                        return;
                    }

                    EditorApplication.update -= updateTick;

                    var networkError = req.result == UnityWebRequest.Result.ConnectionError;
                    var dataProcessingError = req.result == UnityWebRequest.Result.DataProcessingError;
                    var responseText = req.downloadHandler?.text ?? "";
                    var code = req.responseCode;

                    result.HttpCode = (int)code;

                    if (networkError || dataProcessingError)
                    {
                        result.State = ValidationState.NetworkError;
                        result.Message =
                            $"Network error: {req.error}\n\nTechnical details:\n• Result: {req.result}\n• Response Code: {code}";
                    }
                    else
                    {
                        var contentType = req.GetResponseHeader("Content-Type") ?? "";
                        var isBinaryResponse = contentType.Contains("audio") || contentType.Contains("octet-stream");

                        if (code == 200 && isBinaryResponse)
                        {
                            result.State = ValidationState.Success;
                            result.Message = "✓ Connection successful!\n" +
                                             "• API key is authorized\n" +
                                             "• Endpoint is reachable\n" +
                                             "• Model returned audio data (TTS working)";
                        }
                        else if (isTranscriptionRequest && code == 400)
                        {
                            result.State = ValidationState.Success;
                            result.Message = "✓ Connection validated!\n" +
                                             "• API key is authorized\n" +
                                             "• Endpoint is reachable\n" +
                                             "• Model exists (requires audio file at runtime)\n\n" +
                                             $"Note: 400 error expected without audio data";
                        }
                        else
                        {
                            ParseValidationResult(ref result, (int)code, responseText);
                        }
                    }

                    var cacheKey = GetCacheKey(endpoint, apiKey, model, providerId);
                    SaveValidationCache(cacheKey, result);

                    try
                    {
                        onComplete?.Invoke(result);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }

                    req.Dispose();
                    saveState?.Invoke(null, null, null);
                }
                catch (Exception ex)
                {
                    EditorApplication.update -= updateTick;
                    Debug.LogException(ex);

                    result.State = ValidationState.NetworkError;
                    result.Message = $"Validation error: {ex.Message}";

                    try
                    {
                        onComplete?.Invoke(result);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }

                    if (req != null)
                    {
                        try
                        {
                            req.Dispose();
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    saveState?.Invoke(null, null, null);
                }
            };

            saveState?.Invoke(req, op, updateTick);
            EditorApplication.update += updateTick;
        }

        public static void CleanupRequest(
            ref UnityWebRequest activeRequest,
            ref EditorApplication.CallbackFunction updateTick)
        {
            if (updateTick != null)
            {
                EditorApplication.update -= updateTick;
                updateTick = null;
            }

            if (activeRequest != null)
            {
                try
                {
                    activeRequest.Abort();
                }
                catch
                {
                    // ignored
                }

                activeRequest.Dispose();
                activeRequest = null;
            }
        }

        private static void ParseValidationResult(ref ValidationResult result, int code, string responseText)
        {
            var extractedError = ExtractErrorMessage(responseText);

            switch (code)
            {
                case 200:
                case 201:
                    result.State = ValidationState.Success;
                    result.Message = "✓ Connection successful!\n" +
                                     "• API key is authorized\n" +
                                     "• Endpoint is reachable\n" +
                                     "• Model is available and working";
                    break;

                case 401:
                    result.State = ValidationState.AuthenticationError;
                    result.Message = $"✗ Authentication Failed (HTTP 401)\n" +
                                     $"• Your API key is invalid or expired\n" +
                                     $"• Check that you've copied the key correctly\n" +
                                     $"• Verify the key is still active in your provider account" +
                                     (extractedError != null ? $"\n\nDetails: {extractedError}" : "");
                    break;

                case 403:
                    result.State = ValidationState.AuthenticationError;
                    result.Message = $"✗ Access Forbidden (HTTP 403)\n" +
                                     $"• Your API key lacks permissions for this operation\n" +
                                     $"• Check your account tier or subscription\n" +
                                     $"• Some models require specific access levels" +
                                     (extractedError != null ? $"\n\nDetails: {extractedError}" : "");
                    break;

                case 404:
                    if (extractedError != null && (extractedError.Contains("not a chat model") ||
                                                   extractedError.Contains("not supported in") ||
                                                   extractedError.Contains("Did you mean to use")))
                    {
                        result.State = ValidationState.NetworkError;
                        result.Message = $"⚠ Model Validation Skipped (HTTP 404)\n" +
                                         $"• This model uses a different API endpoint\n" +
                                         $"• The model exists but validation cannot test it\n" +
                                         $"• Your configuration should still work at runtime\n\n" +
                                         $"Details: {extractedError}";
                    }
                    else
                    {
                        result.State = ValidationState.NotFound;
                        result.Message = $"✗ Model Not Found (HTTP 404)\n" +
                                         $"• The model ID doesn't exist on this provider\n" +
                                         $"• Check for typos in the model name\n" +
                                         $"• Verify the model is available in your region" +
                                         (extractedError != null ? $"\n\nDetails: {extractedError}" : "");
                    }

                    break;

                case 429:
                    result.State = ValidationState.RateLimited;
                    result.Message = $"⚠ Rate Limited (HTTP 429)\n" +
                                     $"• Too many requests in a short time\n" +
                                     $"• Wait a few moments and try again\n" +
                                     $"• Consider upgrading your plan for higher limits" +
                                     (extractedError != null ? $"\n\nDetails: {extractedError}" : "");
                    break;

                case 503:
                    result.State = ValidationState.ServerError;
                    result.Message = $"⚠ Service Unavailable (HTTP 503)\n" +
                                     $"• The model may be loading or cold-starting\n" +
                                     $"• For HuggingFace: Model might be spinning up\n" +
                                     $"• Try again in 30-60 seconds" +
                                     (extractedError != null ? $"\n\nDetails: {extractedError}" : "");
                    break;

                case >= 500:
                    result.State = ValidationState.ServerError;
                    result.Message = $"✗ Server Error (HTTP {code})\n" +
                                     $"• Provider is experiencing technical issues\n" +
                                     $"• This is not an issue with your configuration\n" +
                                     $"• Check provider status page or try again later" +
                                     (extractedError != null ? $"\n\nDetails: {extractedError}" : "");
                    break;

                case 400:
                    if (extractedError != null && (extractedError.Contains("multipart/form-data") ||
                                                   extractedError.Contains("audio file") ||
                                                   extractedError.Contains("'file' is required")))
                    {
                        result.State = ValidationState.Success;
                        result.Message = "✓ Connection validated!\n" +
                                         "• API key is authorized\n" +
                                         "• Endpoint is reachable\n" +
                                         "• Model exists (requires audio file at runtime)\n\n" +
                                         $"Note: Validation sent test request without audio data";
                    }
                    else
                    {
                        result.State = ValidationState.NetworkError;
                        result.Message = $"✗ Bad Request (HTTP {code})\n" +
                                         $"• Check your endpoint URL is correct\n" +
                                         $"• Verify model ID format matches provider requirements\n" +
                                         $"• Some providers require specific endpoint formats" +
                                         (extractedError != null ? $"\n\nDetails: {extractedError}" : "");
                    }

                    break;

                case >= 400:
                    result.State = ValidationState.NetworkError;
                    result.Message = $"✗ Bad Request (HTTP {code})\n" +
                                     $"• Check your endpoint URL is correct\n" +
                                     $"• Verify model ID format matches provider requirements\n" +
                                     $"• Some providers require specific endpoint formats" +
                                     (extractedError != null ? $"\n\nDetails: {extractedError}" : "");
                    break;

                default:
                    result.State = ValidationState.NetworkError;
                    result.Message = $"✗ Unexpected Response (HTTP {code})\n" +
                                     "• Unknown status code received\n" +
                                     "• Check provider documentation" +
                                     (extractedError != null ? $"\n\nDetails: {extractedError}" : "");
                    break;
            }
        }

        [Serializable]
        private class ErrorResponse
        {
            public Error error;
            public string message;
            public string type;
        }

        [Serializable]
        private class Error
        {
            public string message;
            public string type;
        }

        private static string ExtractErrorMessage(string responseText)
        {
            if (string.IsNullOrEmpty(responseText)) return null;

            try
            {
                try
                {
                    var errorResponse = JsonUtility.FromJson<ErrorResponse>(responseText);
                    if (errorResponse != null)
                    {
                        if (errorResponse.error != null && !string.IsNullOrEmpty(errorResponse.error.message))
                        {
                            var errorType = errorResponse.error.type;
                            return !string.IsNullOrEmpty(errorType)
                                ? $"[{errorType}] {errorResponse.error.message}"
                                : errorResponse.error.message;
                        }

                        if (!string.IsNullOrEmpty(errorResponse.message))
                        {
                            var errorType = errorResponse.type;
                            return !string.IsNullOrEmpty(errorType)
                                ? $"[{errorType}] {errorResponse.message}"
                                : errorResponse.message;
                        }
                    }
                }
                catch
                {
                    // Fall through to manual parsing if JsonUtility fails
                }

                if (responseText.Contains("\"message\""))
                {
                    var msgStart = responseText.IndexOf("\"message\"", StringComparison.Ordinal);
                    if (msgStart >= 0)
                    {
                        msgStart = responseText.IndexOf(":", msgStart, StringComparison.Ordinal) + 1;
                        msgStart = responseText.IndexOf("\"", msgStart, StringComparison.Ordinal) + 1;
                        var msgEnd = responseText.IndexOf("\"", msgStart, StringComparison.Ordinal);
                        if (msgEnd > msgStart)
                        {
                            var message = responseText.Substring(msgStart, msgEnd - msgStart);

                            string errorType = null;
                            if (!responseText.Contains("\"type\""))
                            {
                                return message;
                            }

                            var typeStart = responseText.IndexOf("\"type\"", StringComparison.Ordinal);
                            if (typeStart < 0)
                            {
                                return message;
                            }

                            typeStart = responseText.IndexOf(":", typeStart, StringComparison.Ordinal) + 1;
                            typeStart = responseText.IndexOf("\"", typeStart, StringComparison.Ordinal) + 1;
                            var typeEnd = responseText.IndexOf("\"", typeStart, StringComparison.Ordinal);
                            if (typeEnd > typeStart)
                            {
                                errorType = responseText.Substring(typeStart, typeEnd - typeStart);
                            }

                            return errorType != null ? $"[{errorType}] {message}" : message;
                        }
                    }
                }

                if (responseText.Length > 200)
                {
                    return responseText.Substring(0, 200) + "...";
                }

                return responseText;
            }
            catch
            {
                if (responseText.Length > 200)
                {
                    return responseText.Substring(0, 200) + "...";
                }

                return responseText;
            }
        }

        private static UnityWebRequest BuildOpenAIRequest(string endpoint, string apiKey, string model)
        {
            endpoint = endpoint.TrimEnd('/');
            var v1Index = endpoint.LastIndexOf("/v1", StringComparison.Ordinal);
            if (v1Index >= 0)
            {
                endpoint = endpoint.Substring(0, v1Index + 3); // +3 to include "/v1"
            }
            else
            {
                endpoint = endpoint + "/v1";
            }

            var modelLower = model?.ToLower() ?? "";
            if (modelLower.Contains("tts"))
            {
                var url = $"{endpoint}/audio/speech";
                var jsonBody = "{\n" +
                               $"  \"model\": \"{EscapeJson(model)}\",\n" +
                               "  \"input\": \"test\",\n" +
                               "  \"voice\": \"alloy\"\n" +
                               "}";

                return BuildPostJsonRequest(url, jsonBody, apiKey);
            }

            if (modelLower.Contains("whisper") || modelLower.Contains("transcribe"))
            {
                var url = $"{endpoint}/audio/transcriptions";
                var jsonBody = "{\n" +
                               $"  \"model\": \"{EscapeJson(model)}\",\n" +
                               "  \"file\": \"test\"\n" +
                               "}";

                return BuildPostJsonRequest(url, jsonBody, apiKey);
            }

            var chatUrl = $"{endpoint}/chat/completions";
            var chatJsonBody = "{\n" +
                               $"  \"model\": \"{EscapeJson(model)}\",\n" +
                               "  \"messages\": [\n" +
                               "    {\"role\": \"user\", \"content\": \"test\"}\n" +
                               "  ],\n" +
                               "  \"max_completion_tokens\": 10\n" +
                               "}";

            return BuildPostJsonRequest(chatUrl, chatJsonBody, apiKey);
        }

        private static UnityWebRequest BuildHuggingFaceRequest(string endpoint, string apiKey, string model)
        {
            var isOpenAIStyle = endpoint.Contains("/v1/chat/completions") ||
                                endpoint.EndsWith("/chat/completions");

            if (isOpenAIStyle)
            {
                var jsonBody = "{\n" +
                               $"  \"model\": \"{EscapeJson(model)}\",\n" +
                               "  \"messages\": [\n" +
                               "    {\"role\": \"user\", \"content\": \"test\"}\n" +
                               "  ],\n" +
                               "  \"max_tokens\": 1\n" +
                               "}";

                return BuildPostJsonRequest(endpoint, jsonBody, apiKey);
            }

            var isDirectModelEndpoint = endpoint.Contains("/hf-inference/models/") ||
                                       (endpoint.Contains("/models/") && !endpoint.Contains("/v1/"));

            if (isDirectModelEndpoint)
            {
                var jsonBody = "{\n" +
                               "  \"inputs\": \"https://huggingface.co/datasets/huggingface/documentation-images/resolve/main/coco_sample.png\"\n" +
                               "}";
                var req = BuildPostJsonRequest(endpoint, jsonBody, apiKey);
                req.SetRequestHeader("x-wait-for-model", "true");
                return req;
            }

            var textJsonBody = "{\n" + "  \"inputs\": \"test\"\n" + "}";

            var textReq = BuildPostJsonRequest(endpoint, textJsonBody, apiKey);
            textReq.SetRequestHeader("x-wait-for-model", "true");
            return textReq;
        }

        private static UnityWebRequest BuildLlamaApiRequest(string endpoint, string apiKey, string model)
        {
            var jsonBody =
                $"{{\"model\":\"{EscapeJson(model)}\",\"messages\":[{{\"role\":\"user\",\"content\":\"test\"}}],\"max_tokens\":1}}";
            return BuildPostJsonRequest(endpoint, jsonBody, apiKey);
        }

        private static UnityWebRequest BuildElevenLabsRequest(string endpoint, string apiKey, string voiceId)
        {
            endpoint = endpoint.TrimEnd('/');

            if (!endpoint.Contains("/text-to-speech"))
            {
                if (!endpoint.EndsWith("/v1"))
                {
                    endpoint += "/v1";
                }

                endpoint += "/text-to-speech";
            }

            var url = $"{endpoint}/{voiceId}";
            var jsonBody = "{\"text\":\"test\",\"model_id\":\"eleven_monolingual_v1\"}";
            return BuildPostJsonRequest(url, jsonBody, apiKey, "xi-api-key");
        }

        private static UnityWebRequest BuildReplicateRequest(string endpoint, string apiKey, string model)
        {
            endpoint = endpoint.TrimEnd('/');

            var parts = model.Split(':');
            var url = parts.Length == 2
                ? $"{endpoint}/models/{parts[0]}/versions/{parts[1]}/predictions"
                : $"{endpoint}/models/{model}/predictions";

            var jsonBody = "{\"input\":{\"prompt\":\"test\"}}";
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrEmpty(apiKey))
            {
                req.SetRequestHeader("Authorization", $"Token {apiKey}");
            }

            req.SetRequestHeader("Prefer", "wait");

            return req;
        }

        private static UnityWebRequest BuildPostJsonRequest(string url, string json, string authValue,
            string authHeader = "Authorization")
        {
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            var bodyRaw = Encoding.UTF8.GetBytes(json ?? "{}");

            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            if (string.IsNullOrEmpty(authValue))
            {
                return req;
            }

            if (authHeader == "Authorization")
            {
                req.SetRequestHeader(authHeader, "Bearer " + authValue);
            }
            else
            {
                req.SetRequestHeader(authHeader, authValue);
            }

            return req;
        }

        private static string EscapeJson(string str)
        {
            return string.IsNullOrEmpty(str) ? str : str.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public static Color GetBadgeColor(ValidationState state)
        {
            return state switch
            {
                ValidationState.Success => new Color(0.25f, 0.75f, 0.35f), // green
                ValidationState.AuthenticationError => new Color(0.85f, 0.25f, 0.25f), // red
                ValidationState.NotFound => new Color(0.85f, 0.25f, 0.25f), // red
                ValidationState.RateLimited => new Color(1.00f, 0.70f, 0.15f), // amber
                ValidationState.ServerError => new Color(1.00f, 0.70f, 0.15f), // amber
                ValidationState.NetworkError => new Color(0.85f, 0.25f, 0.25f), // red
                ValidationState.NotChecked => new Color(0.55f, 0.60f, 0.70f), // gray
                _ => new Color(0.55f, 0.60f, 0.70f) // gray
            };
        }

        public static void DrawBadge(Color color, GUIContent content)
        {
            var rect = GUILayoutUtility.GetRect(14, 14, GUILayout.Width(14), GUILayout.Height(14));
            rect.y += 4f;

            if (Event.current.type == EventType.Repaint)
            {
                Handles.BeginGUI();
                var prev = Handles.color;
                Handles.color = color;
                var center = rect.center;
                var radius = rect.width * 0.5f;
                Handles.DrawSolidDisc(center, Vector3.forward, radius);
                Handles.color = prev;
                Handles.EndGUI();
            }

            if (!string.IsNullOrEmpty(content.tooltip))
            {
                GUI.Label(rect, new GUIContent(string.Empty, content.tooltip), GUIStyle.none);
            }
        }
    }
}
