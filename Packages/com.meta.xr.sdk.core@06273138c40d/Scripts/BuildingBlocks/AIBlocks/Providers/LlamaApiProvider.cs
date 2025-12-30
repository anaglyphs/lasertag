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
    public sealed class LlamaApiProvider : AIProviderBase, IChatTask
    {
        [SerializeField] internal string apiKey;
        [Tooltip("Full chat completions endpoint, e.g. https://api.llama.com/v1/chat/completions")]
        [SerializeField] internal string endpointUrl = "https://api.llama.com/v1/chat/completions";
        [SerializeField] internal string model = "Llama-4-Maverick-17B-128E-Instruct-FP8";
        [SerializeField] internal bool supportsVision = true;
        public bool SupportsVision => supportsVision;

        [Range(0f, 2f)] public float temperature = 0.6f;
        [Range(0f, 1f)] public float topP = 0.9f;
        [Range(0f, 1f)] public float repetitionPenalty = 1.0f;
        public int maxCompletionTokens = 512;

        [Tooltip("When ON, any http(s) image URL is fetched locally and sent inline as base64 to avoid remote fetch failures.")]
        [SerializeField] internal bool inlineRemoteImages;

        [Tooltip("If NOT inlining, resolve redirects locally and send the final URL.")]
        [SerializeField] internal bool resolveRemoteRedirects;

        [Tooltip("Max bytes to download per image when inlining (Llama limit is ~25MB).")]
        [SerializeField] internal int maxInlineBytes = 25 * 1024 * 1024;

        protected override InferenceType DefaultSupportedTypes => InferenceType.Cloud;

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

        // Shape 1 (your logs): { completion_message: { content: { type, text } } }
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

        // Shape 2: choices[].message.content is an ARRAY of parts
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

        // Shape 3: choices[].message.content is a STRING
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

        public async Task<ChatResponse> ChatAsync(ChatRequest req, IProgress<ChatDelta> stream = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("Llama API key missing in LlamaApiProvider.");
            if (string.IsNullOrWhiteSpace(endpointUrl))
                throw new InvalidOperationException("Endpoint URL missing in LlamaApiProvider.");

            var prepared = req;
            if (SupportsVision && req?.images != null && req.images.Count > 0)
            {
                prepared = new ChatRequest(req.text, new List<ImageInput>(req.images.Count));
                foreach (var img in req.images)
                {
                    var p = img;

                    if (inlineRemoteImages)
                    {
                        p = await PrepareImageInlineAsync(p, ct); // turn http(s) into bytes -> data URL later
                    }
                    else if (resolveRemoteRedirects)
                    {
                        p = await ResolveRedirectAsync(p, ct); // keep URL, but resolve to final concrete URL
                    }

                    prepared.images.Add(p);
                }
            }

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

        private static string ToImageUrl(ImageInput img)
        {
            if (!string.IsNullOrEmpty(img.url)) return img.url;

            if (img.bytes is not { Length: > 0 })
            {
                throw new ArgumentException("ImageInput had neither Url nor Bytes.");
            }

            var mime = string.IsNullOrEmpty(img.mimeType) ? "image/jpeg" : img.mimeType;
            return $"data:{mime};base64,{Convert.ToBase64String(img.bytes)}";

        }

        // Inline: download bytes & send as data URL later
        private async Task<ImageInput> PrepareImageInlineAsync(ImageInput img, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(img.url)) return img; // guard
            var bytes = await ImageInputUtils.DownloadBytesAsync(img.url, maxInlineBytes, ct);
            if (bytes == null) return img;

            var mime = ImageInputUtils.GuessMime(bytes, null, img.url);
            return new ImageInput { bytes = bytes, mimeType = mime };
        }

        // URLs only: resolve to a concrete URL (don’t download)
        private async Task<ImageInput> ResolveRedirectAsync(ImageInput img, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(img.url)) return img; // guard
            var finalUrl = await ImageInputUtils.ResolveRedirectAsync(img.url, ct);
            return new ImageInput { url = finalUrl, mimeType = img.mimeType };
        }

        private static string ExtractAssistant(string rawJson)
        {
            // Shape 1: completion_message.content.text
            try
            {
                var cmpl = JsonUtility.FromJson<ChatResponseCompletion>(rawJson);
                var text = cmpl?.completion_message?.content?.text;
                if (!string.IsNullOrEmpty(text)) return text;
            }
            catch { /* continue */ }

            // Shape 2: choices[].message.content is an ARRAY of parts
            try
            {
                var parts = JsonUtility.FromJson<ChatResponseParts>(rawJson);
                if (parts?.choices != null && parts.choices.Count > 0)
                {
                    var msg = parts.choices[0].message;
                    if (msg?.content != null && msg.content.Count > 0)
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

            // Shape 3: choices[].message.content is a STRING
            try
            {
                var text = JsonUtility.FromJson<ChatResponseText>(rawJson);
                if (text?.choices != null && text.choices.Count > 0)
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
