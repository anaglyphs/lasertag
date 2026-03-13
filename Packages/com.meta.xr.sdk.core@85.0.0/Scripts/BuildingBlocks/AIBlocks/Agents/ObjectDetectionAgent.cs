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

using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.Events;
using UnityEngine;
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [Serializable]
    public struct BoxData
    {
        /// <summary>
        /// World-space position of the visualized detection’s quad center. This is computed downstream
        /// from 2D box coordinates and the environment depth, and is used by
        /// <see cref="ObjectDetectionVisualizer"/> to place the label and the bounding-box mesh in 3D.
        /// </summary>
        public Vector3 position;

        /// <summary>
        /// World-space rotation applied to the rendered detection quad so it faces the user’s camera.
        /// The visualizer derives this from the current passthrough pose and is helpful for billboarding
        /// labels and quads without extra per-frame math in user scripts.
        /// </summary>
        public Quaternion rotation;

        /// <summary>
        /// World-space scale of the rendered quad that represents the detection bounds. The visualizer
        /// derives width/height from camera intrinsics and depth so box size roughly matches the real
        /// object footprint at the estimated distance from the headset.
        /// </summary>
        public Vector3 scale;

        /// <summary>
        /// Human-readable class label for the detection (for example, “person” or “cup”). This is
        /// resolved from the model’s class index by the provider and is appended with a score so UI
        /// elements can show both category and confidence to the user.
        /// </summary>
        public string label;
    }

    /// <summary>
    /// UnityEvent raised when a batch of detections has been decoded.
    /// Payload is the list of <see cref="BoxData"/> for the current frame.
    /// </summary>
    [Serializable] public class OnDetectionResponseReceived : UnityEvent<List<BoxData>> { }

    /// <summary>
    /// Runs object detection over passthrough frames on-device or via a provider, aggregates post-processing,
    /// and raises events with decoded boxes for visualization or game logic. Use this component when your
    /// goal is a task-oriented “detect → filter → render/use” loop rather than low-level model handling.
    /// </summary>
    /// <remarks>
    /// Typical setup: wire a provider such as <see cref="UnityInferenceEngineProvider"/> and
    /// <see cref="ObjectDetectionVisualizer"/> to draw quads/labels. Frames usually come from
    /// the passthrough camera; see the Passthrough overview
    /// ([guide](https://developer.oculus.com/documentation/unity/unity-passthrough/)) and environment depth
    /// ([guide](https://developers.meta.com/horizon/documentation/unity/unity-depthapi-overview/)) to understand how
    /// 2D boxes are projected into world space. The agent timeslices inference if supported and exposes an
    /// event so UX can react the moment detections are ready.
    /// </remarks>
    public class ObjectDetectionAgent : MonoBehaviour
    {
        private const int DefaultBatchCapacity = 32;
        private const int DefaultJpegQuality = 70;

        [Header("Provider")]
        [Tooltip("Provider asset that implements IObjectDetectionTask.")]
        [SerializeField] internal AIProviderBase providerAsset;

        [Tooltip("Filter out detections below this confidence threshold.")]
        [Range(0, 1)] public float minConfidence = 0.5f;

        [Tooltip("Run detection every N frames to reduce computational load. Set to 0 to disable automatic inference (manual triggering only). Higher values = better performance but less frequent updates.")]
        [SerializeField, Range(0, 120)] private int detectEveryNFrames = 1;

#if MRUK_INSTALLED
        /// <summary>
        /// Fired after detections are finalized for the current frame and converted into
        /// <see cref="BoxData"/> entries. Subscribe from UI or a
        /// <see cref="ObjectDetectionVisualizer"/> to update quads, labels, or trigger gameplay.
        /// </summary>
        public event Action<List<BoxData>> OnBoxesUpdated;

        private readonly List<BoxData> _batch = new(DefaultBatchCapacity);
        private PassthroughCameraAccess _cam;
        private DepthTextureAccess _depth;
#endif
        [SerializeField] private OnDetectionResponseReceived onDetectionResponseReceived = new();
        public OnDetectionResponseReceived OnDetectionResponseReceived => onDetectionResponseReceived;

        private IObjectDetectionTask _detector;
        private UnityInferenceEngineProvider _unityProvider;
        private bool _busy;

        private async void Awake()
        {
            await Task.CompletedTask;
#if MRUK_INSTALLED
            _cam = FindAnyObjectByType<PassthroughCameraAccess>();
            _depth = GetComponent<DepthTextureAccess>();
            _detector = providerAsset as IObjectDetectionTask;
            _unityProvider = providerAsset as UnityInferenceEngineProvider;

            if (_detector == null)
            {
                Debug.LogError("[ObjectDetectionAgent] providerAsset must implement IObjectDetectionTask.");
            }

            if (_unityProvider != null)
            {
#if UNITY_INFERENCE_INSTALLED
                await _unityProvider.WarmUp();
#else
                Debug.LogError("[UnityInferenceProvider] Unity Inference Engine package is not installed.");
#endif
            }
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
            if (detectEveryNFrames > 0 && (Time.frameCount % detectEveryNFrames == 0))
            {
                CallInference();
            }
        }

        /// <summary>
        /// Triggers an asynchronous detection pass if not already running.
        /// Equivalent to calling <see cref="RunDetection"/> and ignoring the task.
        /// </summary>
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

                _depth?.RequestDepthSample();

                var src = _cam.GetTexture();
                AIProviderBase.ObjectDetectionPrediction[] predictions = null;

                if (_unityProvider)
                {
#if UNITY_INFERENCE_INSTALLED
                    var bin = await _unityProvider.DetectAsync(src);
                    if (bin == null || bin.Length == 0)
                    {
                        return;
                    }
                    predictions = AIProviderBase.DecodeBinaryDetections(bin);
#else
                    throw new InvalidOperationException("[ObjectDetectionAgent] Unity Inference Engine package is not installed but UnityInferenceEngineProvider is being used.");
#endif
                }
                else
                {
                    var bytes = AIProviderBase.EncodeTextureToJpeg(src, DefaultJpegQuality);
                    var json = await _detector.DetectAsync(bytes);
                    if (!string.IsNullOrEmpty(json))
                    {
                        AIProviderBase.TryParseDetectionJson(json, out predictions);
                    }
                }

                if (predictions == null || predictions.Length == 0)
                {
                    return;
                }

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

    }
}
