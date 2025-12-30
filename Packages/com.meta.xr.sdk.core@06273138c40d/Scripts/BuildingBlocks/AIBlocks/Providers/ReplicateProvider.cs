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
using System.Threading;
using System.Text;
using UnityEngine;
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/Cloud/Replicate Provider")]
    public sealed class ReplicateProvider : AIProviderBase, IChatTask
    {
        [Tooltip("Your Replicate API token (r8_...).")]
        [SerializeField] internal string apiKey;

        [Tooltip("Model identifier: 'owner/model' or 'owner/model:version'.")]
        [SerializeField] internal string modelId = "owner/model:version";

        [Tooltip("Enable if the selected model supports image input.")]
        [SerializeField] internal bool supportsVision;
        public bool SupportsVision => supportsVision;

        [Tooltip("If set, this full URL will be used instead of the default Replicate endpoint.\n" +
                 "Example: https://my-proxy.example.com/v1/models/owner/model/predictions")]
        [SerializeField] private string overrideEndpointUrl;

        [Tooltip("Max bytes allowed when inlining images from URLs or bytes.")]
        [SerializeField] internal int maxInlineBytes = 12 * 1024 * 1024;

        protected override InferenceType DefaultSupportedTypes => InferenceType.Cloud;

        public async Task<ChatResponse> ChatAsync(ChatRequest req, IProgress<ChatDelta> stream = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("ReplicateProvider: API key is not set.");
            }

            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw new InvalidOperationException("ReplicateProvider: Model Id is not set.");
            }

            var url = !string.IsNullOrWhiteSpace(overrideEndpointUrl) ? overrideEndpointUrl : BuildUrl(modelId);
            var prompt = req?.text ?? string.Empty;
            string imageDataUri = null;

            if (SupportsVision && req?.images is { Count: > 0 })
            {
                // Replicate often expects a single inline image
                imageDataUri = await ToDataUriAsync(req.images[0], ct);
            }

            var payload = BuildPayload(prompt, imageDataUri);
            var transport = new HttpTransport(null);

            var extra = new Dictionary<string, string>
            {
                { "Authorization", $"Token {apiKey}" },
                { "Prefer", "wait" }
            };
            var raw = await transport.PostJsonAsync(url, payload, extra);

            if (string.IsNullOrEmpty(raw))
            {
                throw new Exception("Replicate API error: empty response");
            }

            var text = ExtractAssistantText(raw);
            return new ChatResponse(text ?? string.Empty, raw);
        }

        private static string BuildUrl(string id)
        {
            var parts = id.Split(':');
            return parts.Length == 2
                ? $"https://api.replicate.com/v1/models/{parts[0]}/versions/{parts[1]}/predictions"
                : $"https://api.replicate.com/v1/models/{id}/predictions";
        }

        private static string BuildPayload(string prompt, string imageDataUriOrNull)
        {
            var sb = new StringBuilder();
            sb.Append("{\"input\":{");
            sb.Append($"\"prompt\":\"{Escape(prompt)}\"");
            if (!string.IsNullOrEmpty(imageDataUriOrNull))
            {
                sb.Append($",\"image\":\"{Escape(imageDataUriOrNull)}\"");
            }

            sb.Append("}}");
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            return s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        // Convert ImageInput → data URI accepted by many Replicate models.
        private async Task<string> ToDataUriAsync(ImageInput img, CancellationToken ct)
        {
            return await ImageInputUtils.ToDataUriAsync(img, maxInlineBytes, ct);
        }

        // Parse common Replicate response shapes
        [Serializable]
        private class RootStr
        {
            public string output;
        }

        [Serializable]
        private class RootArr
        {
            public string[] output;
        }

        private static string ExtractAssistantText(string json)
        {
            try
            {
                var s = JsonUtility.FromJson<RootStr>(json);
                if (!string.IsNullOrEmpty(s.output)) return s.output.Trim();
            }
            catch
            {
                // ignored
            }

            try
            {
                var a = JsonUtility.FromJson<RootArr>(json);
                if (a.output is { Length: > 0 })
                    return string.Concat(a.output).Trim();
            }
            catch
            {
                // ignored
            }

            return null;
        }
    }
}
