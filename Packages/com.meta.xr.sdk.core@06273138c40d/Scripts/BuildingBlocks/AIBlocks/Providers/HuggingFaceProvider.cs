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
    public sealed class HuggingFaceProvider : AIProviderBase, IChatTask, IObjectDetectionTask
    {
        [Tooltip("Your Hugging Face API token.")]
        [SerializeField] internal string apiKey;

        [Tooltip("Model id used for this asset (chat/VLM or detection), e.g. meta-llama/Llama-3.2-11B-Vision-Instruct or facebook/detr-resnet-50.")]
        [SerializeField] internal string modelId;

        [Tooltip("Chat/VLM endpoint. Example: https://api-inference.huggingface.co/v1/chat/completions (router-compatible).")]
        [SerializeField] internal string endpoint;

        [Tooltip("Enable if the selected chat model supports image input.")]
        [SerializeField] internal bool supportsVision = true;
        public bool SupportsVision => supportsVision;

        [Tooltip("When ON, any http(s) image URL is fetched locally and sent inline as base64 to avoid remote fetch failures.")]
        [SerializeField] internal bool inlineRemoteImages = true;

        [Tooltip("If NOT inlining, resolve redirects locally and send the final URL.")]
        [SerializeField] internal bool resolveRemoteRedirects = true;

        [Tooltip("Max bytes to download per image when inlining.")]
        [SerializeField] internal int maxInlineBytes = 25 * 1024 * 1024;

        protected override InferenceType DefaultSupportedTypes => InferenceType.Cloud;

        public async Task<ChatResponse> ChatAsync(ChatRequest req,
            IProgress<ChatDelta> stream = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("HuggingFace: apiKey is empty.");
            if (string.IsNullOrWhiteSpace(endpoint)) throw new InvalidOperationException("HuggingFace: endpoint is empty.");
            if (string.IsNullOrWhiteSpace(modelId)) throw new InvalidOperationException("HuggingFace: modelId is empty.");
            if (req == null) throw new ArgumentNullException(nameof(req));

            var prepared = req;
            if (SupportsVision && req.images != null && req.images.Count > 0)
            {
                prepared = new ChatRequest(req.text, new System.Collections.Generic.List<ImageInput>(req.images.Count));
                foreach (var img in req.images)
                {
                    var p = img;
                    if (inlineRemoteImages)
                    {
                        p = await PrepareImageInlineAsync(p, ct); // http(s) → bytes (+mime)
                    }
                    else if (resolveRemoteRedirects)
                    {
                        p = await ResolveRedirectAsync(p, ct);    // keep URL, resolve to final
                    }
                    prepared.images.Add(p);
                }
            }

            var body = BuildChatPayload(modelId, prepared);
            var http = new HttpTransport(apiKey);
            var raw = await http.PostJsonAsync(endpoint, body);

            var text = ExtractAssistantText(raw);
            stream?.Report(new ChatDelta(text ?? string.Empty));
            return new ChatResponse(text ?? string.Empty, raw);
        }

        public async Task<string> DetectAsync(byte[] imageJpgOrPng, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("HuggingFace: apiKey is empty.");
            if (string.IsNullOrWhiteSpace(modelId)) throw new InvalidOperationException("HuggingFace: modelId is empty.");
            if (imageJpgOrPng == null || imageJpgOrPng.Length == 0) throw new ArgumentException("DetectAsync: empty image.");

            var url = RawVisionEndpoint(modelId);
            var http = new HttpTransport(apiKey);
            var raw = await http.PostBinaryAsync(url, imageJpgOrPng, "image/jpeg"); // accepts JPG/PNG

            return TransformDetections(raw); // normalized JSON
        }

        public Task<byte[]> DetectBinaryAsync(byte[] imageJpgOrPng, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        private static string BuildChatPayload(string modelIdIn, ChatRequest req)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"model\":\"").Append(modelIdIn)
              .Append("\",\"messages\":[{\"role\":\"user\",\"content\":[")
              .Append("{\"type\":\"text\",\"text\":\"").Append(Esc(req.text)).Append("\"}");

            if (req.images != null && req.images.Count > 0)
            {
                // Send the first image; URL may already be data: after prep
                var img = req.images[0];
                string urlString = null;

                if (img.bytes != null && img.bytes.Length > 0)
                {
                    var mime = string.IsNullOrEmpty(img.mimeType) ? "image/png" : img.mimeType;
                    urlString = $"data:{mime};base64,{Convert.ToBase64String(img.bytes)}";
                }
                else
                {
                    urlString = img.url;
                }

                if (!string.IsNullOrEmpty(urlString))
                {
                    sb.Append(",{\"type\":\"image_url\",\"image_url\":{\"url\":\"")
                      .Append(Esc(urlString)).Append("\"}}");
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
                        .Append(",\"label\":\"").Append(Esc(t.Groups["l"].Value)).Append('"')
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
                    .Append(",\"label\":\"").Append(string.IsNullOrEmpty(d.label) ? "" : Esc(d.label)).Append('"')
                    .Append(",\"box\":[").Append(xmin).Append(',').Append(ymin).Append(',')
                    .Append(xmax).Append(',').Append(ymax).Append("]}");
            }

            sb.Append(']');
            return sb.ToString();
        }

        private async Task<ImageInput> PrepareImageInlineAsync(ImageInput img, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(img.url)) return img; // guard
            var bytes = await ImageInputUtils.DownloadBytesAsync(img.url, maxInlineBytes, ct);
            if (bytes == null) return img;

            var mime = ImageInputUtils.GuessMime(bytes, null, img.url);
            return new ImageInput { bytes = bytes, mimeType = mime };
        }

        private async Task<ImageInput> ResolveRedirectAsync(ImageInput img, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(img.url)) return img; // guard
            var finalUrl = await ImageInputUtils.ResolveRedirectAsync(img.url, ct);
            return new ImageInput { url = finalUrl, mimeType = img.mimeType };
        }

        private static string RawVisionEndpoint(string modelIdIn)
        {
            return $"https://router.huggingface.co/hf-inference/models/{modelIdIn}";
        }

        private static string Esc(string s)
        {
            return (s ?? "").Replace("\\", @"\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        public Task<byte[]> DetectAsync(RenderTexture src, CancellationToken ct = default)
            => throw new NotImplementedException("HuggingFaceProvider does not support RenderTexture input.");
    }
}
