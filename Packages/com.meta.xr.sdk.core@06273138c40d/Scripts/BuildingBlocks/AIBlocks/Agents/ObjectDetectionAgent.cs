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
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [Serializable]
    public struct BoxData
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public string label;
    }

    [Serializable]
    public class OnDetectionResponseReceived : UnityEvent<List<BoxData>> { }

    public class ObjectDetectionAgent : MonoBehaviour
    {
        [Header("Provider")]
        [SerializeField] internal AIProviderBase providerAsset;
        [Range(0, 1)] public float minConfidence = 0.5f;
        [SerializeField] private bool realtimeInference = true;

#if MRUK_INSTALLED
        public event Action<List<BoxData>> OnBoxesUpdated;
        private readonly List<BoxData> _batch = new(32);
        private PassthroughCameraAccess _cam;
#endif
        [SerializeField] private OnDetectionResponseReceived onDetectionResponseReceived = new();
        public OnDetectionResponseReceived OnDetectionResponseReceived => onDetectionResponseReceived;

        private IObjectDetectionTask _detector;
        private UnityInferenceEngineProvider _unityProvider;
        private bool _busy;

        private RenderTexture _captureRT;

        private void Awake()
        {
#if MRUK_INSTALLED
            _cam = FindAnyObjectByType<PassthroughCameraAccess>();
            _detector = providerAsset as IObjectDetectionTask;
            _unityProvider = providerAsset as UnityInferenceEngineProvider;

            if (_detector == null)
            {
                Debug.LogError("[ObjectDetectionAgent] providerAsset must implement IObjectDetectionTask.");
            }

            if (_unityProvider == null)
            {
                return;
            }
            var warmUp = _unityProvider.GetType().GetMethod("WarmUp",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            warmUp?.Invoke(_unityProvider, null);
#endif
        }

        private void Update()
        {
#if MRUK_INSTALLED
            if (!_cam.IsPlaying || _busy)
            {
                return;
            }
#endif
            if (realtimeInference)
            {
                CallInference();
            }
        }

        public void CallInference() => _ = RunDetection();

        private Task RunDetection()
        {
#if MRUK_INSTALLED
            return RunDetectionImpl();
#else
            Debug.LogWarning("[ObjectDetectionAgent] MRUK package is not installed. Object detection functionality is unavailable.");
            return Task.CompletedTask;
#endif
        }

#if MRUK_INSTALLED
        private async Task RunDetectionImpl()
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            try
            {
                if (_detector == null)
                {
                    return;
                }

                var src = _cam.GetTexture();
                EnsureBuffers(src.width, src.height);
                Graphics.Blit(src, _captureRT);

                Prediction[] predictions = null;

                if (_unityProvider)
                {
#if UNITY_INFERENCE_INSTALLED
                    var bin = await _unityProvider.DetectAsync(_captureRT);
                    predictions = DecodeBinary(bin);
#endif
                }
                else
                {
                    // JSON fallback for HuggingFace
                    var tex = new Texture2D(src.width, src.height, TextureFormat.RGB24, false);
                    RenderTexture.active = _captureRT;
                    tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;

                    var bytes = tex.EncodeToJPG(70);
                    Destroy(tex);

                    var json = await _detector.DetectAsync(bytes);
                    if (!string.IsNullOrEmpty(json))
                    {
                        TryExtractPredictions(json, out predictions);
                    }
                }

                if (predictions == null) return;

                _batch.Clear();
                foreach (var p in predictions)
                {
                    if (p.score < minConfidence || p.box == null || p.box.Length < 4) continue;
                    _batch.Add(new BoxData
                    {
                        position = new Vector3(p.box[0], p.box[1], 0),
                        scale = new Vector3(p.box[2], p.box[3], 0),
                        rotation = Quaternion.identity,
                        label = $"{p.label} {p.score:0.00}"
                    });
                }

                OnBoxesUpdated?.Invoke(_batch);
                onDetectionResponseReceived.Invoke(_batch);
            }
            finally { _busy = false; }
        }
#endif

#if MRUK_INSTALLED
        private void EnsureBuffers(int w, int h)
        {
            if (_captureRT && _captureRT.width == w && _captureRT.height == h) return;
            _captureRT?.Release();
            _captureRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        }

        [Serializable] public class Prediction { public float score; public string label; public float[] box; }
        [Serializable] private class Wrap { public Prediction[] predictions; }
        [Serializable] private class Arr { public Prediction[] items; }

        private static bool TryExtractPredictions(string json, out Prediction[] preds)
        {
            preds = null;
            try
            {
                json = json.Trim();
                preds = json.StartsWith("[")
                    ? JsonUtility.FromJson<Arr>("{\"items\":" + json + "}").items
                    : JsonUtility.FromJson<Wrap>(json)?.predictions;
            }
            catch
            {
                // ignored
            }

            return preds != null;
        }

        private static Prediction[] DecodeBinary(byte[] bin)
        {
            if (bin == null || bin.Length == 0)
            {
                return null;
            }
            try
            {
                using var ms = new System.IO.MemoryStream(bin);
                using var br = new System.IO.BinaryReader(ms);
                var cnt = br.ReadInt32();
                var preds = new Prediction[cnt];
                for (var i = 0; i < cnt; i++)
                {
                    var xmin = br.ReadSingle();
                    var ymin = br.ReadSingle();
                    var xmax = br.ReadSingle();
                    var ymax = br.ReadSingle();
                    var score = br.ReadSingle();
                    var id = br.ReadInt32();
                    var label = br.ReadString();

                    preds[i] = new Prediction
                    {
                        box = new[] { xmin, ymin, xmax, ymax },
                        score = score,
                        label = label
                    };
                }
                return preds;
            }
            catch { return null; }
        }
#endif
    }
}
