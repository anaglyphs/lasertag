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
using System.Collections;
using System.Threading;
using UnityEngine;
using System.Text;
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/Cloud/OpenAI Provider")]
    public sealed class OpenAIProvider : AIProviderBase, IChatTask, ISpeechToTextTask, ITextToSpeechTask
    {
        [SerializeField] internal string apiKey;

        [Tooltip("Root like https://api.openai.com/v1 (a /v1 suffix is fine too).")]
        [SerializeField] internal string apiRoot = "https://api.openai.com/v1";

        [Tooltip("Set this to the model you’re using for this asset, e.g. gpt-4.1-mini / gpt-4o-mini-tts / gpt-4o-transcribe.")]
        [SerializeField] internal string model = "gpt-4.1-mini";

        [SerializeField] internal bool supportsVision = true;
        [SerializeField] internal bool inlineRemoteImages = true;
        [SerializeField] internal bool resolveRemoteRedirects = true;
        [SerializeField] internal int maxInlineBytes = 25 * 1024 * 1024;

        [Tooltip("OpenAI supports 'json' or 'text' for transcriptions. (Whisper also supports others via verbose_json, etc.)")]
        [SerializeField] internal string sttResponseFormat = "json";
        [Tooltip("Optional ISO language code (e.g., 'en', 'de'). If empty, OpenAI will auto-detect.")]
        [SerializeField] internal string sttLanguage = "";

        [Tooltip("Optional temperature for STT (0..1). Leave < 0 to omit.")]
        [Range(0, 1)]
        [SerializeField] internal float sttTemperature;

        [SerializeField] internal string ttsVoice = "alloy";

        [Tooltip("mp3, wav, flac, aac, opus, or pcm")]
        [SerializeField] internal string ttsOutputFormat = "mp3";

        [Tooltip("Playback speed 0.25 .. 4.0 (1.0 default). Leave <= 0 to omit.")]
        [Range(0.25f, 4f)]
        [SerializeField] internal float ttsSpeed = 1.0f;

        [Tooltip("Optional instructions to control voice style (ignored by tts-1 / tts-1-hd).")]
        [TextArea]
        [SerializeField] internal string ttsInstructions;

        // Some projects may hold multiple copies of the interfaces during domain reloads;
        // having an explicit implementation helps the compiler pick the correct symbol.
        bool IChatTask.SupportsVision => supportsVision;
        public bool SupportsVision => supportsVision;

        protected override InferenceType DefaultSupportedTypes => InferenceType.Cloud;

        [Serializable]
        private class RespContent
        {
            public string type;
            public string text;
        }

        [Serializable]
        private class RespOutputItem
        {
            public string type; // "message"
            public string status; // "completed"
            public string role; // "assistant"
            public RespContent[] content;
        }

        [Serializable]
        private class ResponsesRoot
        {
            public string output_text; // SDK convenience (if present)
            public RespOutputItem[] output; // canonical list
        }

        public async Task<ChatResponse> ChatAsync(ChatRequest req, IProgress<ChatDelta> stream = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("OpenAI: apiKey is empty.");
            if (string.IsNullOrWhiteSpace(model)) throw new InvalidOperationException("OpenAI: model is empty.");

            var endpoint = EnsureApiRoot(apiRoot) + "/responses";

            var prepared = req;
            if (supportsVision && req?.images is { Count: > 0 })
            {
                prepared = new ChatRequest(req.text, new List<ImageInput>(req.images.Count));
                foreach (var img in req.images)
                {
                    var p = img;
                    if (inlineRemoteImages) p = await PrepareImageInlineAsync(p, ct);
                    else if (resolveRemoteRedirects) p = await ResolveRedirectAsync(p, ct);
                    prepared.images.Add(p);
                }
            }

            var payload = BuildResponsesPayload(model, prepared);
            var http = new HttpTransport(apiKey);
            var raw = await http.PostJsonAsync(endpoint, payload);

            var text = ExtractAssistantText(raw);
            stream?.Report(new ChatDelta(text ?? string.Empty));
            return new ChatResponse(text ?? string.Empty, raw);
        }

        private static string BuildResponsesPayload(string modelId, ChatRequest req)
        {
            // { "model": "...",
            //   "input": [ { "role":"user", "content":[ {type:"input_text"...}, {type:"input_image"...} ] } ],
            //   "stream": false
            // }
            var sb = new StringBuilder(1024);
            sb.Append("{\"model\":\"").Append(Esc(modelId)).Append("\",\"input\":[{");
            sb.Append("\"role\":\"user\",\"content\":[");

            var first = true;

            // text first (if any)
            if (!string.IsNullOrWhiteSpace(req?.text))
            {
                sb.Append("{\"type\":\"input_text\",\"text\":\"").Append(Esc(req.text)).Append("\"}");
                first = false;
            }

            // one or more images
            if (req?.images is { Count: > 0 })
            {
                for (int i = 0; i < req.images.Count; i++)
                {
                    var img = req.images[i];
                    string url;

                    if (img.bytes is { Length: > 0 })
                    {
                        var mime = string.IsNullOrEmpty(img.mimeType) ? "image/png" : img.mimeType;
                        url = $"data:{mime};base64,{Convert.ToBase64String(img.bytes)}";
                    }
                    else
                    {
                        url = img.url; // can be http(s) or data:
                    }

                    if (!string.IsNullOrEmpty(url))
                    {
                        if (!first) sb.Append(',');
                        first = false;

                        sb.Append("{\"type\":\"input_image\",\"image_url\":\"")
                            .Append(Esc(url)).Append("\"}");
                    }
                }
            }

            sb.Append("]}],\"stream\":false}");
            return sb.ToString();
        }

        private static string ExtractAssistantText(string rawJson)
        {
            try
            {
                var r = JsonUtility.FromJson<ResponsesRoot>(rawJson);

                if (!string.IsNullOrEmpty(r?.output_text))
                    return r.output_text;

                // Fallback: first output message's first text item
                if (r?.output != null && r.output.Length > 0)
                {
                    var item = r.output[0];
                    if (item?.content != null)
                    {
                        for (int i = 0; i < item.content.Length; i++)
                        {
                            var c = item.content[i];
                            if (!string.IsNullOrEmpty(c?.text))
                                return c.text;
                        }
                    }
                }
            }
            catch
            {
                // ignored: we’ll return null and let caller decide
            }

            return null;
        }

        [Serializable]
        private class OpenAITranscript
        {
            public string text;
        }

        public async Task<string> TranscribeAsync(byte[] audioBytes, string language = null, CancellationToken ct = default)
        {
            if (audioBytes == null || audioBytes.Length == 0)
            {
                throw new ArgumentException("OpenAI STT: empty audio buffer.");
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("OpenAI: apiKey is empty.");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException("OpenAI: model is empty.");
            }

            var url = EnsureApiRoot(apiRoot) + "/audio/transcriptions";
            var fields = new Dictionary<string, string> { { "model", model } }; var effectiveLanguage = string.IsNullOrEmpty(language) ? sttLanguage : language;
            if (!string.IsNullOrEmpty(effectiveLanguage))
                fields["language"] = effectiveLanguage;
            if (!string.IsNullOrEmpty(sttResponseFormat)) fields["response_format"] = sttResponseFormat;
            if (sttTemperature >= 0f)
            {
                fields["temperature"] = sttTemperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var http = new HttpTransport(apiKey);
            var body = await http.PostMultipartAsync(
                url,
                fields,
                file: ("file", audioBytes, "clip.wav", "audio/wav"),
                extra: null
            );

            if (string.Equals(sttResponseFormat, "text", StringComparison.OrdinalIgnoreCase))
            {
                return body ?? string.Empty;
            }

            var resp = JsonUtility.FromJson<OpenAITranscript>(body);
            return resp?.text ?? string.Empty;
        }

        public IEnumerator SynthesizeStreamCoroutine(string text, string voice = null, Action<AudioClip> onReady = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning("OpenAI TTS: empty input.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Debug.LogError("OpenAI: apiKey is empty.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                Debug.LogError("OpenAI: model is empty.");
                yield break;
            }

            var url = EnsureApiRoot(apiRoot) + "/audio/speech";
            var fmt = (ttsOutputFormat ?? "mp3").ToLowerInvariant();
            var audioType = fmt.Contains("wav")
                ? AudioType.WAV
                :
                fmt.Contains("flac")
                    ?
                    AudioType.UNKNOWN
                    : // UnityWebRequestMultimedia supports WAV/OGGVORBIS/MPEG; we route FLAC/AAC/OPUS as mp3 container unless you have custom loaders. Keep mp3/wav safest.
                    AudioType.MPEG;
            var responseFmt = fmt switch
            {
                "wav" => "wav",
                "flac" => "flac",
                "aac" => "aac",
                "opus" => "opus",
                "pcm" => "pcm",
                _ => "mp3"
            };

            // Build JSON payload, add optional instructions/speed only if provided
            var chosenVoice = string.IsNullOrEmpty(voice) ? ttsVoice : voice;

            var sb = new StringBuilder(256);
            sb.Append("{\"model\":\"").Append(Esc(model)).Append("\",")
                .Append("\"voice\":\"").Append(Esc(chosenVoice)).Append("\",")
                .Append("\"input\":\"").Append(Esc(text)).Append("\",")
                .Append("\"response_format\":\"").Append(responseFmt).Append("\"");

            if (!string.IsNullOrWhiteSpace(ttsInstructions))
            {
                sb.Append(",\"instructions\":\"").Append(Esc(ttsInstructions)).Append("\"");
            }

            if (ttsSpeed > 0f && Math.Abs(ttsSpeed - 1.0f) > 0.0001f)
            {
                // 0.25..4.0
                var sp = Mathf.Clamp(ttsSpeed, 0.25f, 4f)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                sb.Append(",\"speed\":").Append(sp);
            }

            sb.Append("}");
            var payload = sb.ToString();

            var http = new HttpTransport(apiKey);
            AudioClip result = null;
            string err = null;
            yield return http.PostAudioClipCoroutine(
                url, payload, audioType, extraHeaders: null,
                onReady: clip => result = clip,
                onError: e => err = e);

            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogError("OpenAI TTS error: " + err);
                yield break;
            }

            onReady?.Invoke(result);
        }

        private static string EnsureApiRoot(string root)
        {
            // Accept "https://api.openai.com" or ".../v1"
            if (string.IsNullOrWhiteSpace(root)) return "https://api.openai.com/v1";
            var trimmed = root.Trim().TrimEnd('/');
            const string v1 = "/v1";
            var idx = trimmed.IndexOf(v1, StringComparison.OrdinalIgnoreCase);
            return (idx >= 0) ? trimmed.Substring(0, idx + v1.Length) : trimmed + v1;
        }

        private async Task<ImageInput> PrepareImageInlineAsync(ImageInput img, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(img.url)) return img;
            var bytes = await ImageInputUtils.DownloadBytesAsync(img.url, maxInlineBytes, ct);
            if (bytes == null) return img;
            var mime = ImageInputUtils.GuessMime(bytes, null, img.url);
            return new ImageInput { bytes = bytes, mimeType = mime };
        }

        private async Task<ImageInput> ResolveRedirectAsync(ImageInput img, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(img.url)) return img;
            var finalUrl = await ImageInputUtils.ResolveRedirectAsync(img.url, ct);
            return new ImageInput { url = finalUrl, mimeType = img.mimeType };
        }

        private static string Esc(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
    }
}
