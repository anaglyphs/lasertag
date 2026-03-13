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
    public sealed class ReplicateProvider : AIProviderBase, IUsesCredential, IChatTask
    {
        [Tooltip("Your Replicate API token (r8_...).")]
        [SerializeField] internal string apiKey;

        [Tooltip("If ON, use this asset's API key instead of CredentialStorage.")]
        [SerializeField] internal bool overrideApiKey;

        [Tooltip("Model identifier: 'owner/model' or 'owner/model:version'.")]
        [SerializeField] internal string modelId = "owner/model:version";

        [Tooltip("Enable if the selected model supports image input.")]
        [SerializeField] internal bool supportsVision;

        /// <summary>
        /// Indicates whether this provider prepares single-image inputs for supported Replicate models.
        /// Disable for text-only models or when the selected model ignores images.
        /// </summary>
        /// <remarks>
        /// See <see cref="IChatTask"/> and the model card on Replicate for vision capabilities.
        /// </remarks>
        public bool SupportsVision => supportsVision;

        [Tooltip("If set, this full URL will be used instead of the default Replicate endpoint.\n" +
                 "Example: https://my-proxy.example.com/v1/models/owner/model/predictions")]
        [SerializeField] internal string overrideEndpointUrl;

        [Tooltip("Max bytes allowed when inlining images from URLs or bytes.")]
        [SerializeField] internal int maxInlineBytes = 12 * 1024 * 1024;

        protected override InferenceType DefaultSupportedTypes => InferenceType.Cloud;
        string IUsesCredential.ProviderId => "Replicate";
        bool IUsesCredential.OverrideApiKey { get => overrideApiKey; set => overrideApiKey = value; }

        ProviderTestConfig IUsesCredential.GetTestConfig()
        {
            return new ProviderTestConfig
            {
                Endpoint = string.IsNullOrEmpty(overrideEndpointUrl) ? "https://api.replicate.com/v1" : overrideEndpointUrl,
                Model = modelId,
                ProviderId = ((IUsesCredential)this).ProviderId
            };
        }

        /// <summary>
        /// Executes a text (or multimodal) prediction on Replicate and returns assistant text.
        /// Handles model id vs specific version and chooses the simplest synchronous polling flow.
        /// </summary>
        /// <param name="req">Input message and optional images, validated against model parameters.</param>
        /// <param name="stream">Optional partial callback; final text is returned after completion.</param>
        /// <param name="ct">Cancellation token for HTTP and polling.</param>
        /// <returns><see cref="ChatResponse"/> with extracted text and raw response for debugging.</returns>
        /// <remarks>
        /// See <see cref="IChatTask"/>. Docs: https://replicate.com/docs/reference/http
        /// </remarks>
        public async Task<ChatResponse> ChatAsync(ChatRequest req, IProgress<ChatDelta> stream = null, CancellationToken ct = default)
        {
            ValidateConfiguration(apiKey, model: modelId);

            var url = !string.IsNullOrWhiteSpace(overrideEndpointUrl) ? overrideEndpointUrl : BuildUrl(modelId);
            var prompt = req?.text ?? string.Empty;
            string imageDataUri = null;

            if (SupportsVision && req?.images is { Count: > 0 })
            {
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
            return EscapeJson(s);
        }

        private async Task<string> ToDataUriAsync(ImageInput img, CancellationToken ct)
        {
            return await ImageInputUtils.ToDataUriAsync(img, maxInlineBytes, ct);
        }

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
