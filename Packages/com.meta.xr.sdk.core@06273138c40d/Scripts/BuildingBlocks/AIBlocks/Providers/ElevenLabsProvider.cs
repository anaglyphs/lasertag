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
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/Cloud/ElevenLabs Provider")]
    public sealed class ElevenLabsProvider : AIProviderBase, ISpeechToTextTask, ITextToSpeechTask
    {
        [SerializeField] internal string apiKey;
        [SerializeField] internal string endpoint = "https://api.elevenlabs.io";

        [Tooltip("Set this to the model you’re using for this asset. For STT use 'scribe_v1'. For TTS use e.g. 'eleven_flash_v2_5'.")]
        [SerializeField] internal string model = "eleven_flash_v2_5";
        [SerializeField] internal string voiceId = "21m00Tcm4TlvDq8ikWAM";

        [Tooltip("Optional ISO language code (e.g., 'en', 'de'). If empty, auto-detect is used.")]
        [SerializeField] internal string sttLanguage = "";

        [Tooltip("If enabled, include non-speech audio event tags (e.g., laughter) in transcripts when supported.")]
        [SerializeField] internal bool sttIncludeAudioEvents;

        [Serializable] private class TranscriptResponse { public string text; }

        protected override InferenceType DefaultSupportedTypes => InferenceType.Cloud;

        public async Task<string> TranscribeAsync(byte[] audioBytes, string language = null,
            CancellationToken ct = default)
        {
            if (audioBytes == null || audioBytes.Length == 0)
                throw new ArgumentException("ElevenLabs STT: audio buffer is empty.");

            var baseUrl = endpoint?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new Exception("ElevenLabs STT: endpoint is empty.");

            var url = $"{baseUrl}/v1/speech-to-text";

            var fields = new Dictionary<string, string> { { "model_id", model } };

            // provider-level override if caller didn't pass one
            var effectiveLanguage = string.IsNullOrEmpty(language) ? sttLanguage : language;
            if (!string.IsNullOrEmpty(effectiveLanguage))
                fields["language_code"] = effectiveLanguage;

            if (sttIncludeAudioEvents)
            {
                // Cover likely parameter variants used by vendors
                fields["audio_events"] = "true";
                fields["enable_audio_events"] = "true";
            }

            var headers = new Dictionary<string, string> { { "xi-api-key", apiKey } };
            var http = new HttpTransport(authToken: null);

            var json = await http.PostMultipartAsync(
                url,
                fields,
                file: ("file", audioBytes, "clip.wav", "audio/wav"),
                extra: headers);

            var resp = JsonUtility.FromJson<TranscriptResponse>(json);
            return resp?.text ?? string.Empty;
        }

        public IEnumerator SynthesizeStreamCoroutine(string text, string voice = null, Action<AudioClip> onReady = null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning("ElevenLabs TTS: empty text.");
                yield break;
            }

            var vid = string.IsNullOrEmpty(voice) ? voiceId : voice;
            if (string.IsNullOrEmpty(vid))
            {
                Debug.LogError("ElevenLabs TTS: voice id is required (set Default Voice Id or pass voice).");
                yield break;
            }

            var baseUrl = endpoint?.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                Debug.LogError("ElevenLabs TTS: endpoint is empty.");
                yield break;
            }

            const string outputFormat = "mp3_22050_32";
            const int latencyHint = 0;

            var url = $"{baseUrl}/v1/text-to-speech/{HttpTransport.EscapeUrl(vid)}/stream" +
                      $"?model_id={HttpTransport.EscapeUrl(model)}" +
                      $"&output_format={HttpTransport.EscapeUrl(outputFormat)}" +
                      $"&optimize_streaming_latency={latencyHint}";

            var payload = "{\"text\":\"" + Escape(text) + "\",\"model_id\":\"" + Escape(model) + "\"}";

            var headers = new Dictionary<string, string> { { "xi-api-key", apiKey } };
            var http = new HttpTransport(authToken: null);

            AudioClip result = null;
            string err = null;
            yield return http.PostAudioClipCoroutine(
                url, payload, AudioType.MPEG, headers,
                onReady: clip => result = clip,
                onError: e => err = e);

            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogError("ElevenLabs TTS error: " + err);
                yield break;
            }

            onReady?.Invoke(result);
        }

        private static string Escape(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
    }
}
