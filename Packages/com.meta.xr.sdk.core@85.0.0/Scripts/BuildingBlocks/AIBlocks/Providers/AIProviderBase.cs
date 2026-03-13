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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <remarks>
    /// Your project already uses InferenceType as a bitmask in editor code
    /// (see AvailableInferenceTypesForBlockId bitwise checks), so we rely on that.
    /// If your enum is missing the [Flags] attribute, add it there (values stay the same).
    /// </remarks>
    [Flags]
    public enum InferenceType
    {
        None = 0,
        Cloud = 1 << 0, // 1
        LocalServer = 1 << 1, // 2
        OnDevice = 1 << 2, // 4
    }

    /// <summary>
    /// Configuration data for testing a provider connection.
    /// Uses ProviderId string to dynamically identify provider types without hardcoded enums.
    /// </summary>
    public struct ProviderTestConfig
    {
        public string Endpoint;
        public string Model;
        public string ProviderId;
    }

    /// <summary>
    /// Base class for AI providers (cloud or device). Centralizes capability flags, shared validation,
    /// and common utilities used by concrete providers like <see cref="OpenAIProvider"/> and
    /// <see cref="UnityInferenceEngineProvider"/>. See the "AI Building Blocks" guide for usage patterns.
    /// </summary>
    /// <remarks>
    /// Guide: [AI Building Blocks Overview](https://developers.meta.com/horizon/documentation/unity/unity-ai-building-blocks-overview/)
    /// Providers advertise supported execution via <see cref="SupportedInferenceTypes"/> and
    /// <see cref="DefaultSupportedTypes"/>; editor tooling reads these to enable/disable features.
    /// </remarks>
    public abstract class AIProviderBase : ScriptableObject
    {
        /// <summary>
        /// Bitmask of execution modes that this provider supports (for example, Cloud, Device, Hybrid).
        /// Editors use this to filter incompatible tasks at design time and to show capability badges.
        /// </summary>
        [SerializeField] private InferenceType supportedInferenceTypes = InferenceType.None;

        protected abstract InferenceType DefaultSupportedTypes { get; }

        public InferenceType SupportedInferenceTypes => supportedInferenceTypes;

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (supportedInferenceTypes != InferenceType.None || DefaultSupportedTypes == InferenceType.None)
            {
                return;
            }

            supportedInferenceTypes = DefaultSupportedTypes;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        /// <summary>
        /// Validates provider configuration parameters and throws descriptive exceptions if invalid.
        /// Consolidates duplicate validation logic from all providers.
        /// </summary>
        /// <param name="apiKey">API key to validate (required).</param>
        /// <param name="endpoint">Endpoint URL to validate (optional).</param>
        /// <param name="model">Model ID to validate (optional).</param>
        /// <exception cref="InvalidOperationException">Thrown when required parameters are missing.</exception>
        protected void ValidateConfiguration(string apiKey, string endpoint = null, string model = null)
        {
            var providerName = GetType().Name.Replace("Provider", "");

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException($"{providerName}: apiKey is empty.");

            if (endpoint != null && string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException($"{providerName}: endpoint is empty.");

            if (model != null && string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException($"{providerName}: model is empty.");
        }

        /// <summary>
        /// Prepares image for inline transmission by downloading remote URLs and converting to base64.
        /// Consolidates duplicate PrepareImageInlineAsync methods from all providers.
        /// </summary>
        /// <param name="img">Image input to prepare.</param>
        /// <param name="maxInlineBytes">Maximum bytes to download.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Image with bytes populated for inline transmission.</returns>
        private static async Task<ImageInput> PrepareImageInlineAsync(ImageInput img, int maxInlineBytes,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(img.url)) return img;
            var bytes = await ImageInputUtils.DownloadBytesAsync(img.url, maxInlineBytes, ct);
            if (bytes == null) return img;
            var mime = ImageInputUtils.GuessMime(bytes, null, img.url);
            return new ImageInput { bytes = bytes, mimeType = mime };
        }

        /// <summary>
        /// Resolves redirect URLs to their final destination.
        /// Consolidates duplicate ResolveRedirectAsync methods from all providers.
        /// </summary>
        /// <param name="img">Image input with URL to resolve.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>Image with resolved final URL.</returns>
        private static async Task<ImageInput> ResolveRedirectAsync(ImageInput img, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(img.url)) return img;
            var finalUrl = await ImageInputUtils.ResolveRedirectAsync(img.url, ct);
            return new ImageInput { url = finalUrl, mimeType = img.mimeType };
        }

        /// <summary>
        /// Escapes string for safe JSON encoding.
        /// Consolidates duplicate Esc() methods from multiple providers.
        /// </summary>
        /// <param name="s">String to escape.</param>
        /// <returns>JSON-safe escaped string.</returns>
        protected static string EscapeJson(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        /// <summary>
        /// Normalizes endpoint URLs by ensuring proper /v1 suffix for OpenAI-compatible APIs.
        /// Consolidates endpoint normalization logic.
        /// </summary>
        /// <param name="endpoint">Endpoint URL to normalize.</param>
        /// <param name="defaultEndpoint">Default endpoint if input is empty.</param>
        /// <returns>Normalized endpoint URL.</returns>
        protected static string NormalizeEndpoint(string endpoint, string defaultEndpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return defaultEndpoint;
            var trimmed = endpoint.Trim().TrimEnd('/');
            const string v1 = "/v1";
            var idx = trimmed.IndexOf(v1, StringComparison.OrdinalIgnoreCase);
            return (idx >= 0) ? trimmed.Substring(0, idx + v1.Length) : trimmed + v1;
        }

        /// <summary>
        /// Prepares a ChatRequest by processing images (inline or redirect resolution).
        /// Consolidates duplicate image prep loop that appears in OpenAI, Llama, HuggingFace providers.
        /// </summary>
        protected async Task<ChatRequest> PrepareRequestImagesAsync(
            ChatRequest req,
            bool supportsVision,
            bool inlineRemote,
            bool resolveRedirects,
            int maxBytes,
            CancellationToken ct)
        {
            if (!supportsVision || req?.images is not { Count: > 0 })
                return req;

            var prepared = new ChatRequest(req.text, new System.Collections.Generic.List<ImageInput>(req.images.Count));
            foreach (var img in req.images)
            {
                var p = img;
                if (inlineRemote) p = await PrepareImageInlineAsync(p, maxBytes, ct);
                else if (resolveRedirects) p = await ResolveRedirectAsync(p, ct);
                prepared.images.Add(p);
            }

            return prepared;
        }

        /// <summary>
        /// Converts an ImageInput to a data URI string for use in API payloads.
        /// If the image has bytes, encodes them as base64 with the appropriate MIME type.
        /// If the image has a URL, returns it as-is.
        /// Consolidates duplicate conversion logic from OpenAI, Llama, HuggingFace providers.
        /// </summary>
        /// <param name="img">Image input to convert.</param>
        /// <param name="defaultMime">Default MIME type to use if not specified (default: "image/png").</param>
        /// <returns>Data URI string or URL.</returns>
        protected static string ImageInputToDataUri(ImageInput img, string defaultMime = "image/png")
        {
            if (!string.IsNullOrEmpty(img.url)) return img.url;

            if (img.bytes is not { Length: > 0 }) throw new ArgumentException("ImageInput has neither URL nor bytes.");
            var mime = string.IsNullOrEmpty(img.mimeType) ? defaultMime : img.mimeType;
            return $"data:{mime};base64,{Convert.ToBase64String(img.bytes)}";
        }

        private static Texture2D _cachedEncodeTex;

        /// <summary>
        /// Resets static caches on domain reload to prevent memory leaks.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCaches()
        {
            if (_cachedEncodeTex != null)
            {
                Destroy(_cachedEncodeTex);
                _cachedEncodeTex = null;
            }
        }

        /// <summary>
        /// Encodes a texture to JPEG format.
        /// Consolidates duplicate texture encoding logic from ObjectDetectionAgent and ObjectDetectionDebugger.
        /// Uses a cached Texture2D to avoid per-frame allocations.
        /// <para>
        /// <strong>Thread Safety:</strong> This method is NOT thread-safe. It should only be called from the main Unity thread
        /// as it accesses Unity's rendering APIs and uses a shared static texture cache.
        /// </para>
        /// </summary>
        /// <param name="src">Source texture to encode.</param>
        /// <param name="quality">JPEG quality (1-100). Default is 75.</param>
        /// <returns>Encoded JPEG byte array.</returns>
        internal static byte[] EncodeTextureToJpeg(Texture src, int quality = 75)
        {
            if (!src) throw new ArgumentNullException(nameof(src), "Source texture cannot be null.");

            const int defaultQuality = 75;
            quality = Mathf.Clamp(quality, 1, 100);
            if (quality == 0) quality = defaultQuality;

            if (_cachedEncodeTex == null || _cachedEncodeTex.width != src.width || _cachedEncodeTex.height != src.height)
            {
                if (_cachedEncodeTex != null)
                {
                    Destroy(_cachedEncodeTex);
                }
                _cachedEncodeTex = new Texture2D(src.width, src.height, TextureFormat.RGB24, false);
            }

            RenderTexture rt = null;
            try
            {
                rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                _cachedEncodeTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                _cachedEncodeTex.Apply();
                RenderTexture.active = prev;
                return _cachedEncodeTex.EncodeToJPG(quality);
            }
            finally
            {
                if (rt) RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Shared prediction structure for object detection results.
        /// </summary>
        [Serializable]
        protected internal class ObjectDetectionPrediction
        {
            public float score;
            public string label;
            public float[] box;
        }

        /// <summary>
        /// Decodes binary-encoded object detection results from UnityInferenceEngine.
        /// Consolidates duplicate DecodeBinary methods from ObjectDetectionAgent and ObjectDetectionDebugger.
        /// Binary format: [count][xmin,ymin,xmax,ymax,score,classId,label] per detection.
        /// </summary>
        /// <param name="bin">Binary-encoded detection data.</param>
        /// <returns>Array of predictions, or null if decoding fails.</returns>
        internal static ObjectDetectionPrediction[] DecodeBinaryDetections(byte[] bin)
        {
            if (bin == null || bin.Length == 0) return null;
            try
            {
                using var ms = new MemoryStream(bin);
                using var br = new BinaryReader(ms);
                var cnt = br.ReadInt32();
                var preds = new ObjectDetectionPrediction[cnt];
                for (var i = 0; i < cnt; i++)
                {
                    var xmin = br.ReadSingle();
                    var ymin = br.ReadSingle();
                    var xmax = br.ReadSingle();
                    var ymax = br.ReadSingle();
                    preds[i] = new ObjectDetectionPrediction
                    {
                        box = new[] { xmin, ymin, xmax, ymax },
                        score = br.ReadSingle()
                    };
                    br.ReadInt32();
                    preds[i].label = br.ReadString();
                }
                return preds;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIProviderBase] Failed to decode binary detections: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Wrapper classes for JSON deserialization of detection results.
        /// </summary>
        [Serializable] protected class DetectionWrapper { public ObjectDetectionPrediction[] predictions; }
        [Serializable] protected class DetectionArray { public ObjectDetectionPrediction[] items; }

        /// <summary>
        /// Parses JSON-encoded object detection results from cloud providers.
        /// Consolidates duplicate TryExtractPredictions methods from ObjectDetectionAgent and ObjectDetectionDebugger.
        /// </summary>
        /// <param name="json">JSON string containing detection results.</param>
        /// <param name="preds">Output array of predictions if parsing succeeds.</param>
        /// <returns>True if parsing succeeded, false otherwise.</returns>
        internal static bool TryParseDetectionJson(string json, out ObjectDetectionPrediction[] preds)
        {
            preds = null;
            try
            {
                json = json?.Trim();
                if (string.IsNullOrEmpty(json)) return false;
                if (json.StartsWith("["))
                {
                    var arr = JsonUtility.FromJson<DetectionArray>("{\"items\":" + json + "}");
                    preds = arr?.items;
                }
                else
                {
                    var wrapper = JsonUtility.FromJson<DetectionWrapper>(json);
                    preds = wrapper?.predictions;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AIProviderBase] Failed to parse detection JSON: {ex.Message}");
            }
            return preds != null;
        }
    }
}
