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
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    public class HttpTransport
    {
        private readonly string _authToken;
        private readonly int _maxRetries;
        private readonly TimeSpan _retryDelay;

        public HttpTransport(string authToken, int maxRetries = 3, TimeSpan? retryDelay = null)
        {
            _authToken = authToken;
            _maxRetries = maxRetries;
            _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
        }

        public static string EscapeUrl(string s) => UnityWebRequest.EscapeURL(s ?? string.Empty);

        public Task<string> PostJsonAsync(string url, string payload, Dictionary<string, string> extra = null) =>
            PostBinaryAsync(url, Encoding.UTF8.GetBytes(payload), "application/json", extra);

        public async Task<string> PostBinaryAsync(string url, byte[] body, string contentType,
            Dictionary<string, string> extra = null) =>
            await ExecuteWithRetries(() => SendInternal(url, body, contentType, extra));

        public async Task<string> PostMultipartAsync(string url, List<IMultipartFormSection> form,
            Dictionary<string, string> extra = null) =>
            await ExecuteWithRetries(async () =>
            {
                var req = UnityWebRequest.Post(url, form);
                req.downloadHandler = new DownloadHandlerBuffer();
                ApplyHeaders(req, extra);
                return await SendRequestAsync(req);
            });

        public async Task<string> PostMultipartAsync(
            string url,
            Dictionary<string, string> fields,
            (string name, byte[] data, string filename, string contentType)? file = null,
            Dictionary<string, string> extra = null)
        {
            return await ExecuteWithRetries(async () =>
            {
                var form = new List<IMultipartFormSection>();
                if (fields != null)
                {
                    foreach (var kv in fields)
                        form.Add(new MultipartFormDataSection(kv.Key, kv.Value ?? string.Empty));
                }
                if (file.HasValue)
                {
                    var f = file.Value;
                    form.Add(new MultipartFormFileSection(f.name, f.data, f.filename, f.contentType));
                }

                var req = UnityWebRequest.Post(url, form);
                req.downloadHandler = new DownloadHandlerBuffer();
                ApplyHeaders(req, extra);
                return await SendRequestAsync(req);
            });
        }

        public System.Collections.IEnumerator PostAudioClipCoroutine(
            string url,
            string jsonPayload,
            AudioType audioType,
            Dictionary<string, string> extraHeaders,
            Action<AudioClip> onReady,
            Action<string> onError = null)
        {
            var body = Encoding.UTF8.GetBytes(jsonPayload ?? "{}");
            var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerAudioClip(url, audioType)
                {
                    streamAudio = false
                }
            };

            req.SetRequestHeader("Content-Type", "application/json");
            ApplyHeaders(req, extraHeaders);

            var op = req.SendWebRequest();
            while (!op.isDone) { yield return null; }

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"HTTP/{req.responseCode} {req.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(req);
            onReady?.Invoke(clip);
        }

        private async Task<string> ExecuteWithRetries(Func<Task<string>> op)
        {
            Exception last = null;
            for (var i = 0; i <= _maxRetries; i++)
            {
                try { return await op(); }
                catch (Exception ex)
                {
                    last = ex;
                    if (i == _maxRetries) break;
                    Debug.LogWarning($"HttpTransport retry {i + 1}/{_maxRetries}: {ex.Message}");
                    await Task.Delay(_retryDelay);
                }
            }
            Debug.LogError($"HttpTransport failed after {_maxRetries + 1} attempts: {last?.Message}");
            return null;
        }

        private async Task<string> SendInternal(string url, byte[] body, string type, Dictionary<string, string> extra)
        {
            var req = new UnityWebRequest(url, "POST")
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer()
            };
            req.SetRequestHeader("Content-Type", type);
            ApplyHeaders(req, extra);
            return await SendRequestAsync(req);
        }

        private void ApplyHeaders(UnityWebRequest req, Dictionary<string, string> extra)
        {
            if (!string.IsNullOrEmpty(_authToken))
                req.SetRequestHeader("Authorization", $"Bearer {_authToken}");
            if (extra == null) return;
            foreach (var kv in extra) req.SetRequestHeader(kv.Key, kv.Value);
        }

        private static async Task<string> SendRequestAsync(UnityWebRequest req)
        {
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
                throw new Exception($"HTTP/{req.responseCode} {req.error}\n{req.downloadHandler.text}");

            return req.downloadHandler.text;
        }
    }
}
