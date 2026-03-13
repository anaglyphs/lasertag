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
    /// <summary>
    /// OpenAI provider implementing chat (Responses API), speech-to-text, and text-to-speech.
    /// Supports optional image inputs for multimodal models when <see cref="SupportsVision"/> is enabled.
    /// </summary>
    /// <remarks>
    /// Guides: https://platform.openai.com/docs/
    /// Used by UI and samples via <see cref="IChatTask"/>, <see cref="ISpeechToTextTask"/>,
    /// and <see cref="ITextToSpeechTask"/>.
    /// </remarks>
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/Cloud/OpenAI Provider")]
    public sealed class OpenAIProvider : AIProviderBase, IUsesCredential, IChatTask, ISpeechToTextTask, ITextToSpeechTask
    {
        [SerializeField] internal string apiKey;

        [Tooltip("If ON, use this asset's API key instead of CredentialStorage.")]
        [SerializeField] internal bool overrideApiKey;

        string IUsesCredential.ProviderId => "OpenAI";

        bool IUsesCredential.OverrideApiKey
        {
            get => overrideApiKey;
            set => overrideApiKey = value;
        }

        ProviderTestConfig IUsesCredential.GetTestConfig()
        {
            return new ProviderTestConfig
            {
                Endpoint = string.IsNullOrEmpty(apiRoot) ? "https://api.openai.com/v1" : apiRoot,
                Model = model,
                ProviderId = ((IUsesCredential)this).ProviderId
            };
        }

        [Tooltip("Root like https://api.openai.com/v1 (a /v1 suffix is fine too).")]
        [SerializeField] internal string apiRoot = "https://api.openai.com/v1";

        [Tooltip(
            "Set this to the model you're using for this asset, e.g. gpt-4.1-mini / gpt-4o-mini-tts / gpt-4o-transcribe.")]
        [SerializeField] internal string model = "gpt-4.1-mini";
        [SerializeField] internal bool supportsVision = true;
        [SerializeField] internal bool inlineRemoteImages = true;
        [SerializeField] internal bool resolveRemoteRedirects = true;
        [SerializeField] internal int maxInlineBytes = 25 * 1024 * 1024;

        [Tooltip(
            "OpenAI supports 'json' or 'text' for transcriptions. (Whisper also supports others via verbose_json, etc.)")]
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

        bool IChatTask.SupportsVision => supportsVision;

        /// <summary>
        /// Indicates whether this provider can handle **vision** inputs (images) alongside text during chat.
        /// When <c>true</c>, <see cref="ChatAsync"/> will package <see cref="ImageInput"/> items using the
        /// OpenAI Responses format and, depending on settings, inline or resolve remote URLs. Toggle this
        /// for models like GPT-4o or any multimodal model that supports image understanding.
        /// </summary>
        /// <remarks>
        /// Controlled by the serialized <c>supportsVision</c> field and reported via
        /// <see cref="IChatTask.SupportsVision"/>. See also <see cref="inlineRemoteImages"/> and
        /// <see cref="resolveRemoteRedirects"/> to influence how remote images are prepared.
        /// </remarks>
        public bool SupportsVision => supportsVision;

        /// <summary>
        /// Declares the default inference location supported by this provider: **cloud**.
        /// </summary>
        /// <remarks>
        /// Used by <see cref="AIProviderBase"/> to filter capability and route tasks. This provider does
        /// not advertise local/edge execution out of the box; if you need on-device models, use a provider
        /// that returns <see cref="InferenceType.OnDevice"/>, <see cref="InferenceType.Cloud"/> or <see cref="InferenceType.LocalServer"/>.
        /// </remarks>
        protected override InferenceType DefaultSupportedTypes => InferenceType.Cloud;

        [Serializable]
        class RespContent
        {
            public string type;
            public string text;
        }

        [Serializable]
        class RespOutputItem
        {
            public string type; // "message"
            public string status; // "completed"
            public string role; // "assistant"
            public RespContent[] content;
        }

        [Serializable]
        class ResponsesRoot
        {
            public string output_text; // SDK convenience (if present)
            public RespOutputItem[] output; // canonical list
        }

        /// <summary>
        /// Sends a chat turn to OpenAI's **Responses API** and returns the assistant's text reply.
        /// </summary>
        /// <param name="req">
        /// The user message and optional <see cref="ImageInput"/> list. If <see cref="SupportsVision"/> is
        /// enabled, images are serialized as <c>input_image</c> items; remote images can be inlined or have
        /// redirects resolved depending on <see cref="inlineRemoteImages"/> and <see cref="resolveRemoteRedirects"/>.
        /// </param>
        /// <param name="stream">
        /// Optional incremental callback for partial text via <see cref="ChatDelta"/>. This implementation
        /// reports the final text once per call (non-streaming HTTP). Use to update UI progressively.
        /// </param>
        /// <param name="ct">
        /// Cancellation token for image preparation and HTTP. Cancels the request if the operation is aborted.
        /// </param>
        /// <returns>
        /// A <see cref="ChatResponse"/> containing the assistant text and the raw JSON payload for debugging
        /// or downstream parsing.
        /// </returns>
        /// <remarks>
        /// Validates <c>apiKey</c> and <c>model</c>, builds a single <c>input</c> message per the Responses
        /// schema, and POSTs to <c>{apiRoot}/v1/responses</c>. The method extracts <c>output_text</c> if present,
        /// or falls back to the first text item in the first output message.
        /// <para>
        /// OpenAI Responses docs: https://platform.openai.com/docs/guides/responses/
        /// </para>
        /// See also<see cref="AIProviderBase"/><see cref="IChatTask"/>.
        /// </remarks>
        public async Task<ChatResponse> ChatAsync(ChatRequest req, IProgress<ChatDelta> stream = null,
            CancellationToken ct = default)
        {
            ValidateConfiguration(apiKey, model: model);

            var endpoint = NormalizeEndpoint(apiRoot, "https://api.openai.com/v1") + "/responses";

            var prepared = await PrepareRequestImagesAsync(req, supportsVision, inlineRemoteImages,
                resolveRemoteRedirects, maxInlineBytes, ct);

            var payload = BuildResponsesPayload(model, prepared);
            var http = new HttpTransport(apiKey);
            var raw = await http.PostJsonAsync(endpoint, payload);

            var text = ExtractAssistantText(raw);
            stream?.Report(new ChatDelta(text ?? string.Empty));
            return new ChatResponse(text ?? string.Empty, raw);
        }

        private static string BuildResponsesPayload(string modelId, ChatRequest req)
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\"model\":\"").Append(EscapeJson(modelId)).Append("\",\"input\":[{");
            sb.Append("\"role\":\"user\",\"content\":[");

            var first = true;

            if (!string.IsNullOrWhiteSpace(req?.text))
            {
                sb.Append("{\"type\":\"input_text\",\"text\":\"").Append(EscapeJson(req.text)).Append("\"}");
                first = false;
            }

            if (req?.images is { Count: > 0 })
            {
                for (var i = 0; i < req.images.Count; i++)
                {
                    var url = ImageInputToDataUri(req.images[i]);

                    if (string.IsNullOrEmpty(url))
                    {
                        continue;
                    }

                    if (!first) sb.Append(',');
                    first = false;

                    sb.Append("{\"type\":\"input_image\",\"image_url\":\"")
                        .Append(EscapeJson(url)).Append("\"}");
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
                {
                    if (r != null) return r.output_text;
                }

                if (r?.output is { Length: > 0 })
                {
                    for (var i = 0; i < r.output.Length; i++)
                    {
                        var item = r.output[i];
                        if (item?.content == null)
                        {
                            continue;
                        }

                        for (var j = 0; j < item.content.Length; j++)
                        {
                            var c = item.content[j];
                            if (string.IsNullOrEmpty(c?.text))
                            {
                                continue;
                            }

                            if (c != null) return c.text;
                        }
                    }
                }
            }
            catch
            {
                // ignored: we'll return null and let caller decide
            }

            return null;
        }

        [Serializable]
        class OpenAITranscript
        {
            public string text;
        }

        /// <summary>
        /// Transcribes an audio clip using OpenAI **audio/transcriptions** and returns plain text.
        /// </summary>
        /// <param name="audioBytes">
        /// Raw audio data (e.g., WAV). Throws if null or empty. The content is sent as multipart/form-data.
        /// </param>
        /// <param name="language">
        /// Optional ISO language override (for example, <c>"en"</c>, <c>"de"</c>). If null/empty,
        /// falls back to <see cref="sttLanguage"/> or lets OpenAI auto-detect.
        /// </param>
        /// <param name="ct">Cancellation token for the HTTP request.</param>
        /// <returns>Transcript text. If <see cref="sttResponseFormat"/> is <c>"text"</c>, the raw body is returned.</returns>
        /// <remarks>
        /// Honors <see cref="sttResponseFormat"/> (e.g., <c>json</c> or <c>text</c>) and optional
        /// <see cref="sttTemperature"/>. Requires valid <c>apiKey</c> and <c>model</c>. POSTs to
        /// <c>{apiRoot}/v1/audio/transcriptions</c>.
        /// <para>
        /// OpenAI Transcriptions: https://platform.openai.com/docs/guides/speech-to-text
        /// </para>
        /// See also <see cref="ISpeechToTextTask"/>.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when <paramref name="audioBytes"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown if <c>apiKey</c> or <c>model</c> is missing.</exception>
        public async Task<string> TranscribeAsync(byte[] audioBytes, string language = null,
            CancellationToken ct = default)
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

            var url = NormalizeEndpoint(apiRoot, "https://api.openai.com/v1") + "/audio/transcriptions";
            var fields = new Dictionary<string, string> { { "model", model } };
            var effectiveLanguage = string.IsNullOrEmpty(language) ? sttLanguage : language;
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

        /// <summary>
        /// Coroutine that synthesizes speech with OpenAI **audio/speech** and yields a Unity <see cref="AudioClip"/>.
        /// </summary>
        /// <param name="text">
        /// Input text to speak. Logs and exits if empty. Combined with <see cref="ttsVoice"/> and optional
        /// <see cref="ttsInstructions"/> to control style, plus <see cref="ttsSpeed"/> for playback rate.
        /// </param>
        /// <param name="voice">
        /// Optional voice name override. If null/empty, uses <see cref="ttsVoice"/>.
        /// </param>
        /// <param name="onReady">
        /// Callback invoked with the created <see cref="AudioClip"/> once download/decoding completes.
        /// </param>
        /// <remarks>
        /// Selects <see cref="AudioType"/> based on <see cref="ttsOutputFormat"/> (e.g., WAV/MP3). Requires
        /// valid <c>apiKey</c> and <c>model</c>. POSTs to <c>{apiRoot}/v1/audio/speech</c>, then streams the
        /// response into a <see cref="AudioClip"/> via the internal HTTP helper.
        /// <para>
        /// OpenAI TTS: https://platform.openai.com/docs/guides/text-to-speech
        /// </para>
        /// See also <see cref="ITextToSpeechTask"/>.
        /// </remarks>
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

            var url = NormalizeEndpoint(apiRoot, "https://api.openai.com/v1") + "/audio/speech";
            var fmt = (ttsOutputFormat ?? "mp3").ToLowerInvariant();
            var audioType = fmt.Contains("wav")
                ? AudioType.WAV
                : fmt.Contains("flac")
                    ? AudioType.UNKNOWN
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

            var chosenVoice = string.IsNullOrEmpty(voice) ? ttsVoice : voice;

            var sb = new StringBuilder(256);
            sb.Append("{\"model\":\"").Append(EscapeJson(model)).Append("\",")
                .Append("\"voice\":\"").Append(EscapeJson(chosenVoice)).Append("\",")
                .Append("\"input\":\"").Append(EscapeJson(text)).Append("\",")
                .Append("\"response_format\":\"").Append(responseFmt).Append("\"");

            if (!string.IsNullOrWhiteSpace(ttsInstructions))
            {
                sb.Append(",\"instructions\":\"").Append(EscapeJson(ttsInstructions)).Append("\"");
            }

            if (ttsSpeed > 0f && Math.Abs(ttsSpeed - 1.0f) > 0.0001f)
            {
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
    }
}
