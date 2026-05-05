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

using System.Threading.Tasks;
using UnityEngine.Events;
using UnityEngine;
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// UnityEvent raised when segmentation results have been processed.
    /// Payload is the <see cref="SegmentationResult"/> for the current frame.
    /// </summary>
    [Serializable] public class OnSegmentationResponseReceived : UnityEvent<SegmentationResult> { }

    /// <summary>
    /// Runs image segmentation over passthrough frames on-device, processes segmentation results,
    /// and raises events with decoded masks and boxes for visualization or game logic. Use this component
    /// when your goal is a task-oriented "segment → filter → render/use" loop rather than low-level model handling.
    /// </summary>
    /// <remarks>
    /// Typical setup: wire a provider such as <see cref="UnityInferenceEngineProvider"/> and
    /// <see cref="ImageSegmentationVisualizer"/> to draw masks/labels. Frames usually come from
    /// the passthrough camera; see the Passthrough overview and environment depth documentation to understand
    /// how 2D segmentation masks are processed. The agent timeslices inference if supported and exposes an
    /// event so UX can react the moment segmentation is ready.
    /// </remarks>
    public sealed class ImageSegmentationAgent : MonoBehaviour
    {
        [Header("Provider")]
        [Tooltip("Provider asset that implements IImageSegmentationTask.")]
        [SerializeField] internal AIProviderBase providerAsset;

        [Tooltip("Run segmentation every N frames to reduce computational load. Set to 0 to disable automatic inference (manual triggering only). Higher values = better performance but less frequent updates.")]
        [SerializeField, Range(0, 120)] internal int segmentEveryNFrames = 1;

        [Tooltip("Maximum resolution (width or height) for captured images before sending to inference. Lower values reduce GC allocations and improve performance. Set to 0 for no downscaling.")]
        [SerializeField] internal int captureMaxResolution = 640;

        /// <summary>
        /// Gets or sets the maximum capture resolution for inference.
        /// </summary>
        public int CaptureMaxResolution
        {
            get => captureMaxResolution;
            set => captureMaxResolution = value;
        }

#if MRUK_INSTALLED
        /// <summary>
        /// Fired after segmentation is finalized for the current frame and converted into
        /// <see cref="SegmentationResult"/>. Subscribe from UI or a
        /// <see cref="ImageSegmentationVisualizer"/> to update masks, labels, or trigger gameplay.
        /// </summary>
        public event Action<SegmentationResult> OnSegmentationUpdated;

        private PassthroughCameraAccess _cam;
        private DepthTextureAccess _depth;
#endif
        [SerializeField] private OnSegmentationResponseReceived onSegmentationResponseReceived = new();
        public OnSegmentationResponseReceived OnSegmentationResponseReceived => onSegmentationResponseReceived;

        private UnityInferenceEngineProvider _unityProvider;
        private IImageSegmentationTask _segmentTask;
        private RenderTexture _captureRT;
        private bool _busy;
        private bool _mrukWarningShown;

        private async void Awake()
        {
            await Task.CompletedTask;
#if MRUK_INSTALLED
            _cam = FindAnyObjectByType<PassthroughCameraAccess>();
            _depth = GetComponent<DepthTextureAccess>();
            _segmentTask = providerAsset as IImageSegmentationTask;
            _unityProvider = providerAsset as UnityInferenceEngineProvider;

            if (_segmentTask == null)
            {
                Debug.LogError("[ImageSegmentationAgent] providerAsset must implement IImageSegmentationTask.");
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
            if (segmentEveryNFrames > 0 && (Time.frameCount % segmentEveryNFrames == 0))
            {
                CallInference();
            }
        }

        /// <summary>
        /// Triggers an asynchronous segmentation pass if not already running.
        /// Equivalent to calling <see cref="RunSegmentation"/> and ignoring the task.
        /// </summary>
        public void CallInference() => _ = RunSegmentation();

        private Task RunSegmentation()
        {
#if MRUK_INSTALLED
            return RunSegmentationImpl();
#else
            if (!_mrukWarningShown)
            {
                Debug.LogWarning("[ImageSegmentationAgent] MRUK package is not installed. Image segmentation functionality is unavailable.");
                _mrukWarningShown = true;
            }
            return Task.CompletedTask;
#endif
        }

#if MRUK_INSTALLED
        private async Task RunSegmentationImpl()
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            try
            {
                if (_segmentTask == null)
                {
                    return;
                }

                _depth?.RequestDepthSample();

                var src = _cam.GetTexture();

                if (!_captureRT || _captureRT.width != src.width || _captureRT.height != src.height)
                {
                    if (_captureRT)
                    {
                        _captureRT.Release();
                    }

                    _captureRT = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                }
                Graphics.Blit(src, _captureRT);

                var result = await _segmentTask.SegmentAsync(_captureRT);
                if (result == null || result.numObjects == 0)
                {
                    return;
                }

                OnSegmentationUpdated?.Invoke(result);
                onSegmentationResponseReceived.Invoke(result);
            }
            finally
            {
                _busy = false;
            }
        }
#endif
    }
}
