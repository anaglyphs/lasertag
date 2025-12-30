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

using UnityEngine.Events;
using System.Collections;
using UnityEngine;
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    public sealed class TextToSpeechAgent : MonoBehaviour
    {
        [Serializable] public class TtsAudioClipEvent : UnityEvent<AudioClip> { }
        [Serializable] public class TtsTextEvent : UnityEvent<string> { }

        public enum TtsPreset
        {
            Greeting,
            Llama,
            PromptReply
        }

        [Header("Provider")]
        [Tooltip("Asset implementing ITextToSpeechTask (e.g., OpenAIProvider, ElevenLabsProvider).")]
        [SerializeField]
        internal AIProviderBase providerAsset;

        [Header("Playback")]
        [SerializeField] private AudioSource audioSource;

        [Header("Preset & Text")]
        [SerializeField] private TtsPreset preset = TtsPreset.Greeting;

        [TextArea(2, 5)]
        [SerializeField] private string text = "";

        [Header("Events")]
        [SerializeField] public TtsAudioClipEvent onClipReady = new();
        [SerializeField] public TtsTextEvent onSpeakStarting = new();
        [SerializeField] public UnityEvent onSpeakFinished = new();

        private ITextToSpeechTask _tts;
        private Coroutine _speakCo;
        private AudioClip _lastClip;

        private void Awake()
        {
            _tts = providerAsset as ITextToSpeechTask;
            if (_tts == null) Debug.LogError("TextToSpeechAgent: providerAsset must implement ITextToSpeechTask.");

            if (!audioSource)
                audioSource = TryGetComponent(out AudioSource a) ? a : gameObject.AddComponent<AudioSource>();

            if (string.IsNullOrWhiteSpace(text))
                text = GetPresetText(preset);
        }

        private void OnDisable() => StopSpeaking();

        private static string GetPresetText(TtsPreset p) => p switch
        {
            TtsPreset.Greeting => "Hi! This is a quick text-to-speech test.",
            TtsPreset.Llama => "I love Llamas. I think they are the most beautiful animals in the world.",
            TtsPreset.PromptReply => "I’ve received your request and I’ll read it out loud now.",
            _ => "Hello!"
        };

        public void ApplyPreset(TtsPreset p)
        {
            preset = p;
            text = GetPresetText(p);
        }

        /// Public entry so other systems (e.g., LLM) can trigger TTS.
        public void SpeakText(string overrideText = null)
        {
            var toSpeak = string.IsNullOrWhiteSpace(overrideText) ? (text ?? string.Empty) : overrideText;
            if (string.IsNullOrWhiteSpace(toSpeak))
            {
                Debug.LogWarning("[TTS] Empty text.");
                return;
            }

            if (_tts == null)
            {
                Debug.LogError("[TTS] No provider.");
                return;
            }

            StopSpeaking();
            _speakCo = StartCoroutine(CoSynthesizeAndPlay(toSpeak));
        }

        public void StopSpeaking()
        {
            if (_speakCo != null)
            {
                StopCoroutine(_speakCo);
                _speakCo = null;
            }

            if (audioSource && audioSource.isPlaying)
            {
                audioSource.Stop();
                onSpeakFinished?.Invoke();
            }
        }

        private IEnumerator CoSynthesizeAndPlay(string toSpeak)
        {
            AudioClip clip = null;
            IEnumerator synth = _tts.SynthesizeStreamCoroutine(toSpeak, null, c => clip = c);

            while (true)
            {
                bool moveNext;
                try
                {
                    moveNext = synth.MoveNext();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TTS] Synthesis failed: {ex}");
                    yield break;
                }

                if (!moveNext) break;
                yield return synth.Current;
            }

            if (!clip)
            {
                Debug.LogError("[TTS] No AudioClip returned.");
                yield break;
            }

            _lastClip = clip;
            onClipReady?.Invoke(clip);
            audioSource.clip = clip;
            onSpeakStarting?.Invoke(toSpeak);
            audioSource.Play();

            while (audioSource.isPlaying) yield return null;
            onSpeakFinished?.Invoke();

            _speakCo = null;
        }

        public AudioClip LastClip => _lastClip;

        public string CurrentText
        {
            get => text;
            set => text = value;
        }
    }
}
