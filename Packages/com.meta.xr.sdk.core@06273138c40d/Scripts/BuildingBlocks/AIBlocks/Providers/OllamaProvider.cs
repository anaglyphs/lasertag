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
using UnityEngine;
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/Local/Ollama Provider")]
    public sealed class OllamaProvider : AIProviderBase, IChatTask
    {
        [Header("Server")]
        [Tooltip("Base URL to the local/remote Ollama server. Usually http://localhost:11434")]
        public string host = "http://localhost:11434";

        [Header("Model")]
        [Tooltip("Model name as listed by `ollama list`, e.g. `llama3.2` (text) or `llava` (vision).")]
        public string model = "llama3.2";

        public bool SupportsVision => true;
        private const int MaxInlineBytes = 25 * 1024 * 1024;

        protected override InferenceType DefaultSupportedTypes => InferenceType.LocalServer;

        public async Task<ChatResponse> ChatAsync(ChatRequest req, IProgress<ChatDelta> stream = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(host))
            {
                throw new InvalidOperationException("[Ollama] Host is empty.");
            }

            if (string.IsNullOrEmpty(model))
            {
                throw new InvalidOperationException("[Ollama] Model is empty.");
            }

            var endpoint = $"{host.TrimEnd('/')}/api/generate";

            List<string> imagesBase64 = null;
            if (req.images is { Count: > 0 })
            {
                imagesBase64 = new List<string>(req.images.Count);
                foreach (var img in req.images)
                {
                    var b64 = await ImageInputToBase64Async(img, ct);
                    if (!string.IsNullOrEmpty(b64))
                    {
                        imagesBase64.Add(b64);
                    }
                }

                if (imagesBase64.Count == 0)
                {
                    Debug.LogWarning("[Ollama] Images were provided but none were convertible to base64. Sending text only.");
                    imagesBase64 = null;
                }
            }

            var payload = new GeneratePayload
            {
                model = model,
                prompt = req.text ?? string.Empty,
                stream = false,
                images = imagesBase64
            };

            var json = JsonUtility.ToJson(payload);
            var transport = new HttpTransport(null);
            var rawJson = await transport.PostJsonAsync(endpoint, json);

            if (string.IsNullOrEmpty(rawJson))
            {
                Debug.LogWarning("[Ollama] Empty HTTP response.");
                return new ChatResponse(string.Empty, rawJson);
            }

            GenerateResponse resp;
            try
            {
                resp = JsonUtility.FromJson<GenerateResponse>(rawJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Ollama] Failed to parse response JSON with JsonUtility: {ex.Message}\nReturning raw text.");
                return new ChatResponse(string.Empty, rawJson);
            }

            var text = resp != null ? (resp.response ?? string.Empty) : string.Empty;

            if (string.IsNullOrEmpty(text) && resp is { done: true, done_reason: "load" })
            {
                Debug.Log("[Ollama] Model was loaded (done_reason=load). No content returned.");
            }

            stream?.Report(new ChatDelta(text));
            return new ChatResponse(text, rawJson);
        }

        /// <summary>
        /// Converts ImageInput into a raw base64 string suitable for Ollama's /api/generate "images" field.
        /// - If bytes are present, convert directly.
        /// - If data: URL is present, extract base64 portion.
        /// - If http(s) URL is present, download into bytes and convert.
        /// </summary>
        private static async Task<string> ImageInputToBase64Async(ImageInput img, CancellationToken ct)
        {
            return await ImageInputUtils.ToBase64Async(img, MaxInlineBytes, ct);
        }

        [Serializable]
        private class GeneratePayload
        {
            public string model;
            public string prompt;
            public bool stream;
            public List<string> images; // base64 strings, only for vision models
        }

        [Serializable]
        private class GenerateResponse
        {
            public string model;
            public string created_at;
            public string response; // aggregated text if stream=false
            public bool done;
            public string done_reason; // e.g., "stop", "load", "unload"
        }
    }
}
