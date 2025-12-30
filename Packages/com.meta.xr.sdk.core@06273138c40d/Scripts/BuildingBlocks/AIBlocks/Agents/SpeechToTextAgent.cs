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
using System.Collections;
using UnityEngine.Events;
using System.Threading;
using UnityEngine;
using System.IO;
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [Serializable]
    public class TranscriptEvent : UnityEvent<string> { }

    public sealed class SpeechToTextAgent : MonoBehaviour
    {
        [Header("Provider")]
        [Tooltip("Provider asset that implements ISpeechToTextTask (e.g., OpenAIProvider, ElevenLabsProvider).")]
        [SerializeField] internal AIProviderBase providerAsset;

        [Header("Mic Settings")]
        [SerializeField] private string deviceName = ""; // blank = default
        [SerializeField] private int sampleRate = 16000;
        [SerializeField] private int channels = 1;

        [Header("Turn Detection (VAD)")]
        [SerializeField] private float rmsWindowSeconds = 0.02f;
        [SerializeField] private float vadStartThresholdDb = 12f;
        [SerializeField] private float vadStopThresholdDb = 6f;
        [SerializeField] private float maxSilenceSeconds = 0.35f;
        [SerializeField] private float minSpeechSeconds = 0.20f;
        [SerializeField] private float endPadSeconds = 0.06f;

        [Header("Envelope Smoothing")]
        [SerializeField] private float envelopeAttackSeconds = 0.015f;
        [SerializeField] private float envelopeReleaseSeconds = 0.08f;

        [Header("Noise Floor (robust)")]
        [SerializeField] private float noiseWindowSeconds = 1.5f;

        [Range(0.05f, 0.5f)]
        [SerializeField] private float noisePercentile = 0.20f;
        [SerializeField] private float quietMarginDb = 1f;

        [Header("Capture Limits")]
        [SerializeField] private float maxDurationSeconds = 20f; // 0 = unlimited
        [SerializeField] private float prePadSeconds = 0.35f;

        [Tooltip("If true, do not auto-stop; caller must StopNow().")]
        [SerializeField] private bool manualStopOnly;

        [Header("Events")]
        [SerializeField] public TranscriptEvent onTranscript = new();

        [Header("Debug / Output")]
        [TextArea]
        [SerializeField] private string lastTranscript = "";

        public string LastTranscript => lastTranscript;

        private ISpeechToTextTask _stt;
        private AudioClip _mic;
        private float[] _readBuf;
        private readonly List<float> _capture = new();
        private readonly Queue<float> _pre = new();
        private int _preCapacity;
        private int _lastMicPos;
        private bool _listening;

        private float _elapsed, _silence, _speech;
        private float _env;
        private int _rmsWindowSamples;
        private int _lastSpeechSampleIdx = -1;

        private List<float> _quietDb = new();
        private int _quietCapacity;
        private const float MIN_DB = -80f;

        private void Awake()
        {
            _stt = providerAsset as ISpeechToTextTask;
            if (_stt == null)
                Debug.LogError(
                    "SpeechToTextAgent: providerAsset must implement ISpeechToTextTask."); // :contentReference[oaicite:0]{index=0}

            _preCapacity = Mathf.Max(1, Mathf.RoundToInt(prePadSeconds * sampleRate) * channels);
            _rmsWindowSamples = Mathf.Max(1, Mathf.RoundToInt(rmsWindowSeconds * sampleRate) * channels);

            _quietCapacity = Mathf.Max(4, Mathf.RoundToInt(noiseWindowSeconds / Mathf.Max(0.005f, rmsWindowSeconds)));
            _quietDb.Capacity = Mathf.Max(_quietCapacity, 32);
        }

        private void OnDisable() => StopNow();

        [ContextMenu("STT/Start Listening")]
        public void StartListening()
        {
            if (_listening || _stt == null) return;
            if (manualStopOnly)
                Debug.LogWarning(
                    "SpeechToTextAgent: 'manualStopOnly' is TRUE, auto-stop will be disabled."); // :contentReference[oaicite:1]{index=1}
            StartCoroutine(CoListenOnce());
        }

        [ContextMenu("STT/Stop Now")]
        public void StopNow()
        {
            if (!_listening) return;
            _listening = false;
        }

        [ContextMenu("STT/Clear Last Transcript")]
        public void ClearLastTranscript()
        {
            lastTranscript = "";
        }

        private IEnumerator CoListenOnce()
        {
            _capture.Clear();
            _pre.Clear();
            _quietDb.Clear();

            _elapsed = _silence = _speech = 0f;
            _lastMicPos = 0;
            _env = 0f;
            _lastSpeechSampleIdx = -1;

            _mic = Microphone.Start(deviceName, true, 10, sampleRate);
            while (Microphone.GetPosition(deviceName) <= 0) yield return null;

            _listening = true;

            var started = false;
            var maxSamples = (maxDurationSeconds > 0f)
                ? Mathf.RoundToInt(maxDurationSeconds * sampleRate) * channels
                : int.MaxValue;

            while (_listening)
            {
                var pos = Microphone.GetPosition(deviceName);
                var delta = pos - _lastMicPos;
                if (delta < 0) delta += _mic.samples;
                if (delta == 0)
                {
                    yield return null;
                    continue;
                }

                Ensure(ref _readBuf, delta * channels);
                _mic.GetData(_readBuf, _lastMicPos);
                _lastMicPos = pos;

                // pre-roll ring
                for (int i = 0; i < delta * channels; i++)
                {
                    if (_pre.Count >= _preCapacity) _pre.Dequeue();
                    _pre.Enqueue(_readBuf[i]);
                }

                // windowed processing
                int iSample = 0;
                while (iSample < delta * channels)
                {
                    var take = Mathf.Min(_rmsWindowSamples, delta * channels - iSample);

                    // RMS -> dB
                    var rms = 0f;
                    for (int j = 0; j < take; j++)
                    {
                        var s = _readBuf[iSample + j];
                        rms += s * s;
                    }

                    rms = Mathf.Sqrt(rms / Mathf.Max(1, take));
                    var winSec = take / (float)(sampleRate * channels);

                    // envelope
                    var rising = rms > _env;
                    var tau = rising
                        ? Mathf.Max(0.003f, envelopeAttackSeconds)
                        : Mathf.Max(0.01f, envelopeReleaseSeconds);
                    var k = 1f - Mathf.Exp(-winSec / tau);
                    _env += (rms - _env) * k;
                    var envDb = 20f * Mathf.Log10(Mathf.Max(_env, 1e-7f));

                    // noise estimate
                    float noiseDb = EstimateNoiseDb();
                    var snrDbTemp = envDb - noiseDb;
                    bool qualifiesAsQuiet = snrDbTemp < (Mathf.Max(0f, vadStartThresholdDb) - quietMarginDb);
                    if (qualifiesAsQuiet)
                    {
                        if (_quietDb.Count >= _quietCapacity) _quietDb.RemoveAt(0);
                        _quietDb.Add(Mathf.Clamp(envDb, MIN_DB, 20f));
                        noiseDb = EstimateNoiseDb();
                    }

                    var snrDb = envDb - noiseDb;
                    var above = snrDb >= vadStartThresholdDb;
                    var below = snrDb < vadStopThresholdDb;

                    if (!started)
                    {
                        if (above)
                        {
                            started = true;
                            _capture.AddRange(_pre);
                            _pre.Clear();
                        }
                        else
                        {
                            _elapsed += winSec;
                        }
                    }

                    if (started)
                    {
                        for (int j = 0; j < take; j++) _capture.Add(_readBuf[iSample + j]);
                        _elapsed += winSec;

                        if (above)
                        {
                            _speech += winSec;
                            _silence = 0f;
                            _lastSpeechSampleIdx = _capture.Count;
                        }
                        else
                        {
                            _silence += winSec;
                        }

                        if (!manualStopOnly &&
                            _speech >= Mathf.Max(0f, minSpeechSeconds) &&
                            below &&
                            _silence >= Mathf.Max(0.08f, maxSilenceSeconds))
                        {
                            _listening = false;
                            break;
                        }

                        if (_capture.Count >= maxSamples)
                        {
                            _listening = false;
                            break;
                        }
                    }

                    iSample += take;
                }

                yield return null;
            }

            Microphone.End(deviceName);

            if (_capture.Count == 0 && _pre.Count > 0) _capture.AddRange(_pre);

            if (_lastSpeechSampleIdx >= 0)
            {
                var padSamples = Mathf.RoundToInt(endPadSeconds * sampleRate) * channels;
                var trimTo = Mathf.Min(_capture.Count, _lastSpeechSampleIdx + Mathf.Max(0, padSamples));
                if (trimTo < _capture.Count && trimTo > 0) _capture.RemoveRange(trimTo, _capture.Count - trimTo);
            }

            // Encode WAV 16-bit little-endian
            var wav = EncodeWav(_capture, sampleRate, channels);

            var task = _stt.TranscribeAsync(wav, null, CancellationToken.None);
            while (!task.IsCompleted) yield return null;

            if (!task.IsFaulted)
            {
                lastTranscript = task.Result;
                onTranscript?.Invoke(task.Result);
                Debug.Log($"[STT] {task.Result}");
            }
        }

        private float EstimateNoiseDb()
        {
            if (_quietDb.Count == 0) return -60f;
            var tmp = _quietDb.ToArray();
            Array.Sort(tmp);
            int idx = Mathf.Clamp(Mathf.FloorToInt(noisePercentile * (tmp.Length - 1)), 0, tmp.Length - 1);
            return tmp[idx];
        }

        private static void Ensure(ref float[] buf, int needed)
        {
            if (buf == null || buf.Length != needed) buf = new float[needed];
        }

        private static byte[] EncodeWav(List<float> samples, int hz, int ch)
        {
            var sampleCount = samples.Count;
            var bytesPerSample = 2;
            var byteCount = 44 + sampleCount * bytesPerSample;
            var data = new byte[byteCount];

            using var ms = new MemoryStream(data);
            using var bw = new BinaryWriter(ms);

            void W(string s) => bw.Write(System.Text.Encoding.ASCII.GetBytes(s));
            void DW(int v) => bw.Write(v);
            void W16(short v) => bw.Write(v);

            W("RIFF");
            DW(byteCount - 8);
            W("WAVE");
            W("fmt ");
            DW(16);
            W16(1);
            W16((short)ch);
            DW(hz);
            DW(hz * ch * bytesPerSample);
            W16((short)(ch * bytesPerSample));
            W16(16);
            W("data");
            DW(sampleCount * bytesPerSample);

            for (int i = 0; i < sampleCount; i++)
            {
                var v = Mathf.Clamp(samples[i], -1f, 1f);
                var s = (short)Mathf.RoundToInt(v * short.MaxValue);
                W16(s);
            }

            return data;
        }
    }
}
