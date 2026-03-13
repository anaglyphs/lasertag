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
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/Cloud/Llama API Provider")]
    public sealed class LlamaApiProvider : AIProviderBase, IUsesCredential, IChatTask
    {
        [Tooltip("Project-wide API key for Llama API. Ignored when 'Override API Key' is OFF (uses CredentialStorage).")]
        [SerializeField] internal string apiKey;

        [Tooltip("If ON, use this asset's API key instead of CredentialStorage.")]
        [SerializeField] internal bool overrideApiKey;

        [Tooltip("Full chat completions endpoint, e.g. https://api.llama.com/v1/chat/completions")]
        [SerializeField] internal string endpointUrl = "https://api.llama.com/v1/chat/completions";

        [Tooltip("Model identifier to use (e.g., 'Llama-3.1-70B-Instruct' or your hosted variant).")]
        [SerializeField] internal string model = "Llama-4-Maverick-17B-128E-Instruct-FP8";

        [Tooltip("If ON, package image inputs for multimodal models; turn OFF for text-only models.")]
        [SerializeField] internal bool supportsVision = true;

        /// <summary>
        /// Indicates whether this provider prepares and sends image inputs for supported multimodal models.
        /// Disable for pure-text models to reduce request overhead and validation checks.
        /// </summary>
        /// <remarks>
        /// See <see cref="IChatTask"/> and your model's capabilities on the provider dashboard.
        /// </remarks>
        public bool SupportsVision => supportsVision;

        [Tooltip("Sampling temperature. Higher = more random (0–2).")]
        [Range(0f, 2f)] public float temperature = 0.6f;

        [Tooltip("Nucleus sampling cap; keep probability mass under this threshold (0–1).")]
        [Range(0f, 1f)] public float topP = 0.9f;

        [Tooltip("Discourage repetition (>1 penalizes repeats, <1 encourages them).")]
        [Range(0f, 1f)] public float repetitionPenalty = 1.0f;

        [Tooltip("Hard cap on new tokens produced per response.")]
        public int maxCompletionTokens = 512;

        [Tooltip("When ON, any http(s) image URL is fetched locally and sent inline as base64 to avoid remote fetch failures.")]
        [SerializeField] internal bool inlineRemoteImages;

        [Tooltip("If NOT inlining, resolve redirects locally and send the final URL.")]
        [SerializeField] internal bool resolveRemoteRedirects;

        [Tooltip("Max bytes to download per image when inlining (Llama limit is ~25MB).")]
        [SerializeField] internal int maxInlineBytes = 25 * 1024 * 1024;

        protected override InferenceType DefaultSupportedTypes => InferenceType.Cloud;
        string IUsesCredential.ProviderId => "LlamaApi";
        bool IUsesCredential.OverrideApiKey { get => overrideApiKey; set => overrideApiKey = value; }

        ProviderTestConfig IUsesCredential.GetTestConfig()
        {
            return new ProviderTestConfig
            {
                Endpoint = endpointUrl,
                Model = model,
                ProviderId = ((IUsesCredential)this).ProviderId
            };
        }

        [Serializable]
        private class ImageUrl
        {
            public string url;
        }

        [Serializable]
        private class ContentItem
        {
            public string type;
            public string text;
            public string output_text;
            public ImageUrl image_url;
        }

        [Serializable]
        private class MessageReq
        {
            public string role;
            public List<ContentItem> content;
        }

        [Serializable]
        private class ChatBody
        {
            public string model;
            public List<MessageReq> messages;
            public float temperature;
            public float top_p;
            public float repetition_penalty;
            public int max_completion_tokens;
        }

        [Serializable]
        private class CompletionContent
        {
            public string type;
            public string text;
        }

        [Serializable]
        private class CompletionMessage
        {
            public string role;
            public string stop_reason;
            public CompletionContent content;
        }

        [Serializable]
        private class ChatResponseCompletion
        {
            public string id;
            public CompletionMessage completion_message;
        }

        [Serializable]
        private class MessageParts
        {
            public string role;
            public List<ContentItem> content;
        }

        [Serializable]
        private class ChoiceParts
        {
            public MessageParts message;
        }

        [Serializable]
        private class ChatResponseParts
        {
            public List<ChoiceParts> choices;
        }

        [Serializable]
        private class MessageText
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class ChoiceText
        {
            public MessageText message;
        }

        [Serializable]
        private class ChatResponseText
        {
            public List<ChoiceText> choices;
        }

        /// <summary>
        /// Sends a chat request to Llama-API and extracts the assistant text, handling vision inputs
        /// when enabled. Applies safe defaults for temperature and max tokens unless overridden.
        /// </summary>
        /// <param name="req">User message and optional images; validated against provider capabilities.</param>
        /// <param name="stream">Optional partial output callback; final text is still returned.</param>
        /// <param name="ct">Cancellation token for image prep and HTTP.</param>
        /// <returns><see cref="ChatResponse"/> with assistant text and provider metadata.</returns>
        /// <remarks>
        /// See <see cref="IChatTask"/>. API docs: https://llama.developer.meta.com/docs/overview/
        /// </remarks>
        public async Task<ChatResponse> ChatAsync(ChatRequest req, IProgress<ChatDelta> stream = null, CancellationToken ct = default)
        {
            ValidateConfiguration(apiKey, endpointUrl);

            var prepared = await PrepareRequestImagesAsync(req, SupportsVision, inlineRemoteImages, resolveRemoteRedirects, maxInlineBytes, ct);

            var body = BuildBody(prepared);
            var json = JsonUtility.ToJson(body, false);

            var transport = new HttpTransport(apiKey);
            var raw = await transport.PostJsonAsync(endpointUrl.Trim(), json);
            if (string.IsNullOrEmpty(raw))
            {
                throw new Exception("Llama API error: empty response from transport");
            }

            var answer = ExtractAssistant(raw);
            return new ChatResponse(answer, raw: raw);
        }

        private ChatBody BuildBody(ChatRequest req)
        {
            var content = new List<ContentItem>();

            if (!string.IsNullOrWhiteSpace(req.text))
            {
                content.Add(new ContentItem { type = "text", text = req.text });
            }

            if (SupportsVision && req.images != null && req.images.Count > 0)
            {
                var count = Mathf.Min(req.images.Count, 9);
                for (var i = 0; i < count; i++)
                {
                    content.Add(new ContentItem
                    {
                        type = "image_url",
                        image_url = new ImageUrl { url = ToImageUrl(req.images[i]) }
                    });
                }
            }

            var messages = new List<MessageReq>
            {
                new() { role = "user", content = content }
            };

            return new ChatBody
            {
                model = model,
                messages = messages,
                temperature = temperature,
                top_p = topP,
                repetition_penalty = repetitionPenalty,
                max_completion_tokens = Mathf.Max(1, maxCompletionTokens)
            };
        }

        private static string ToImageUrl(ImageInput img) => ImageInputToDataUri(img, "image/jpeg");

        private static string ExtractAssistant(string rawJson)
        {
            try
            {
                var cmpl = JsonUtility.FromJson<ChatResponseCompletion>(rawJson);
                var text = cmpl?.completion_message?.content?.text;
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch { /* continue */ }

            try
            {
                var parts = JsonUtility.FromJson<ChatResponseParts>(rawJson);
                if (parts?.choices is { Count: > 0 })
                {
                    var msg = parts.choices[0].message;
                    if (msg?.content is { Count: > 0 })
                    {
                        var sb = new StringBuilder();
                        foreach (var c in msg.content)
                        {
                            if (!string.IsNullOrEmpty(c.text)) sb.Append(c.text);
                            else if (!string.IsNullOrEmpty(c.output_text)) sb.Append(c.output_text);
                        }

                        var s = sb.ToString();
                        if (!string.IsNullOrEmpty(s)) return s;
                    }
                }
            }
            catch { /* continue */ }

            try
            {
                var text = JsonUtility.FromJson<ChatResponseText>(rawJson);
                if (text?.choices is { Count: > 0 })
                {
                    var s = text.choices[0].message?.content ?? "";
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { /* continue */ }

            return string.Empty;
        }
    }
}
