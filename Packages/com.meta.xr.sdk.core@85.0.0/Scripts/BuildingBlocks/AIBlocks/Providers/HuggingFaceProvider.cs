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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// HuggingFace cloud provider – supports chat/VLM (IChatTask) and object detection (IObjectDetectionTask).
    /// Normalized detections shape:
    ///   [{ "score":float, "label":string, "box":[xmin,ymin,xmax,ymax] }, ...]
    /// </summary>
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/Cloud/HuggingFace Provider")]
    public sealed class HuggingFaceProvider : AIProviderBase, IUsesCredential, IChatTask, IObjectDetectionTask
    {
        [Tooltip("Your Hugging Face API token.")]
        [SerializeField] internal string apiKey;

        [Tooltip("If ON, use this asset's API key instead of CredentialStorage.")]
        [SerializeField] internal bool overrideApiKey;

        [Tooltip("Model id used for this asset (chat/VLM or detection), e.g. meta-llama/Llama-3.2-11B-Vision-Instruct or facebook/detr-resnet-50.")]
        [SerializeField] internal string modelId;

        [Tooltip("Chat/VLM endpoint. Example: https://api-inference.huggingface.co/v1/chat/completions (router-compatible).")]
        [SerializeField] internal string endpoint;

        [Tooltip("Enable if the selected chat model supports image input.")]
        [SerializeField] internal bool supportsVision = true;

        /// <summary>
        /// Indicates whether this provider prepares and sends image inputs for supported multimodal models.
        /// Disable for pure-text models to reduce request overhead and validation checks.
        /// </summary>
        /// <remarks>
        /// See <see cref="IChatTask"/> and your model's capabilities on the provider dashboard.
        /// </remarks>
        public bool SupportsVision => supportsVision;

        [Tooltip("When ON, any http(s) image URL is fetched locally and sent inline as base64 to avoid remote fetch failures.")]
        [SerializeField] internal bool inlineRemoteImages = true;

        [Tooltip("If NOT inlining, resolve redirects locally and send the final URL.")]
        [SerializeField] internal bool resolveRemoteRedirects = true;

        [Tooltip("Max bytes to download per image when inlining.")]
        [SerializeField] internal int maxInlineBytes = 25 * 1024 * 1024;

        protected override InferenceType DefaultSupportedTypes => InferenceType.Cloud;
        string IUsesCredential.ProviderId => "HuggingFace";
        bool IUsesCredential.OverrideApiKey { get => overrideApiKey; set => overrideApiKey = value; }

        ProviderTestConfig IUsesCredential.GetTestConfig()
        {
            return new ProviderTestConfig
            {
                Endpoint = endpoint,
                Model = modelId,
                ProviderId = ((IUsesCredential)this).ProviderId
            };
        }

        /// <summary>
        /// Sends a chat turn to a Hugging Face Inference endpoint and returns assistant text.
        /// Supports optional image inputs when the selected model is multimodal.
        /// </summary>
        /// <param name="req">Message payload and (optionally) images prepared by the editor or runtime.</param>
        /// <param name="stream">Optional progress receiver for partial tokens; final text is always delivered.</param>
        /// <param name="ct">Cancellation token for request assembly and HTTP.</param>
        /// <returns><see cref="ChatResponse"/> with assistant text and raw payload for diagnostics.</returns>
        /// <remarks>
        /// See <see cref="IChatTask"/>. HF Inference: https://huggingface.co/docs/api-inference/index
        /// </remarks>
        public async Task<ChatResponse> ChatAsync(ChatRequest req,
            IProgress<ChatDelta> stream = null, CancellationToken ct = default)
        {
            ValidateConfiguration(apiKey, endpoint, modelId);
            if (req == null) throw new ArgumentNullException(nameof(req));

            var prepared = await PrepareRequestImagesAsync(req, SupportsVision, inlineRemoteImages, resolveRemoteRedirects, maxInlineBytes, ct);

            var body = BuildChatPayload(modelId, prepared);
            var http = new HttpTransport(apiKey);
            var raw = await http.PostJsonAsync(endpoint, body);

            var text = ExtractAssistantText(raw);
            stream?.Report(new ChatDelta(text ?? string.Empty));
            return new ChatResponse(text ?? string.Empty, raw);
        }

        /// <summary>
        /// Runs object detection using a Hugging Face model and returns structured detections.
        /// Handles tensor or JSON outputs depending on the model’s task definition.
        /// </summary>
        /// <param name="imageJpgOrPng">Encoded image data (e.g., JPG/PNG). Must be non-empty.</param>
        /// <param name="ct">Cancellation token to abort pre-processing or HTTP.</param>
        /// <returns>Normalized detection set (boxes, scores, class ids) for downstream visualization.</returns>
        /// <remarks>
        /// See <see cref="IObjectDetectionTask"/>. HF Tasks: https://huggingface.co/docs/inference-providers/tasks/object-detection
        /// </remarks>
        public async Task<string> DetectAsync(byte[] imageJpgOrPng, CancellationToken ct = default)
        {
            ValidateConfiguration(apiKey, model: modelId);
            if (imageJpgOrPng == null || imageJpgOrPng.Length == 0) throw new ArgumentException("DetectAsync: empty image.");

            var url = RawVisionEndpoint(modelId);
            var http = new HttpTransport(apiKey);
            var raw = await http.PostBinaryAsync(url, imageJpgOrPng, "image/jpeg");

            return TransformDetections(raw);
        }

        private static string BuildChatPayload(string modelIdIn, ChatRequest req)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"model\":\"").Append(modelIdIn)
              .Append("\",\"messages\":[{\"role\":\"user\",\"content\":[")
              .Append("{\"type\":\"text\",\"text\":\"").Append(EscapeJson(req.text)).Append("\"}");

            if (req.images is { Count: > 0 })
            {
                var urlString = ImageInputToDataUri(req.images[0]);

                if (!string.IsNullOrEmpty(urlString))
                {
                    sb.Append(",{\"type\":\"image_url\",\"image_url\":{\"url\":\"")
                      .Append(EscapeJson(urlString)).Append("\"}}");
                }
            }

            sb.Append("]}],\"stream\":false}");
            return sb.ToString();
        }

        [Serializable] private class Msg { public string content; }
        [Serializable] private class Choice { public Msg message; }
        [Serializable] private class Resp { public Choice[] choices; }

        private static string ExtractAssistantText(string raw)
        {
            try
            {
                var r = JsonUtility.FromJson<Resp>(raw);
                return (r?.choices is { Length: > 0 }) ? r.choices[0].message.content : null;
            }
            catch { return null; }
        }

        private static readonly Regex KDetr = new(
            @"\{[^{}]*?""score"":(?<s>[\d\.eE+-]+)[^{}]*?""label"":""(?<l>[^""]*)""[^{}]*?"
            + @"""box"":\{""xmin"":(?<x>[\d\.eE+-]+),""ymin"":(?<y>[\d\.eE+-]+),"
            + @"""xmax"":(?<X>[\d\.eE+-]+),""ymax"":(?<Y>[\d\.eE+-]+)\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        [Serializable] private class RawBox { public float xmin, ymin, xmax, ymax; }
        [Serializable] private class RawDetr { public float score; public string label; public RawBox box; public float[] bbox; }
        [Serializable] private class Wrapper { public RawDetr[] items; }

        private static string TransformDetections(string rawJson)
        {
            var m = KDetr.Matches(rawJson);
            if (m.Count > 0)
            {
                var sbFast = new StringBuilder(m.Count * 64).Append('[');
                var first = true;
                foreach (Match t in m)
                {
                    if (!first) sbFast.Append(',');
                    first = false;
                    sbFast.Append('{')
                        .Append("\"score\":").Append(t.Groups["s"].Value)
                        .Append(",\"label\":\"").Append(EscapeJson(t.Groups["l"].Value)).Append('"')
                        .Append(",\"box\":[").Append(t.Groups["x"].Value).Append(',')
                        .Append(t.Groups["y"].Value).Append(',')
                        .Append(t.Groups["X"].Value).Append(',')
                        .Append(t.Groups["Y"].Value).Append("]}");
                }
                sbFast.Append(']');
                return sbFast.ToString();
            }

            var wrapped = "{\"items\":" + rawJson + "}";
            var data = JsonUtility.FromJson<Wrapper>(wrapped);
            if (data?.items == null || data.items.Length == 0) return "[]";

            var sb = new StringBuilder(data.items.Length * 64).Append('[');
            var first2 = true;

            foreach (var d in data.items)
            {
                float xmin, ymin, xmax, ymax;

                if (d.box != null) // DETR corners
                {
                    xmin = d.box.xmin; ymin = d.box.ymin; xmax = d.box.xmax; ymax = d.box.ymax;
                }
                else if (d.bbox is { Length: >= 4 }) // YOLO [x,y,w,h]
                {
                    xmin = d.bbox[0]; ymin = d.bbox[1]; xmax = xmin + d.bbox[2]; ymax = ymin + d.bbox[3];
                }
                else continue;

                if (!first2) sb.Append(',');
                first2 = false;

                sb.Append('{')
                    .Append("\"score\":").Append(d.score.ToString("0.###"))
                    .Append(",\"label\":\"").Append(string.IsNullOrEmpty(d.label) ? "" : EscapeJson(d.label)).Append('"')
                    .Append(",\"box\":[").Append(xmin).Append(',').Append(ymin).Append(',')
                    .Append(xmax).Append(',').Append(ymax).Append("]}");
            }

            sb.Append(']');
            return sb.ToString();
        }

        private static string RawVisionEndpoint(string modelIdIn)
        {
            return $"https://router.huggingface.co/hf-inference/models/{modelIdIn}";
        }

        /// <summary>
        /// Performs object detection on a RenderTexture by encoding it to JPEG and calling the byte[] overload.
        /// This provides compatibility with the IObjectDetectionTask interface while adapting to cloud providers.
        /// </summary>
        /// <param name="src">Source RenderTexture to analyze.</param>
        /// <param name="ct">Cancellation token for aborting the operation.</param>
        /// <returns>UTF-8 encoded JSON string as byte array containing detection results.</returns>
        public async Task<byte[]> DetectAsync(RenderTexture src, CancellationToken ct = default)
        {
            if (!src) throw new ArgumentNullException(nameof(src));
            var jpg = EncodeTextureToJpeg(src);
            var json = await DetectAsync(jpg, ct);
            return Encoding.UTF8.GetBytes(json ?? "[]");
        }
    }
}
