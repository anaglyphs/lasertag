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
using System.IO;
using UnityEngine;
using System.Threading;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Collections.Generic;
using Object = UnityEngine.Object;
#if UNITY_INFERENCE_INSTALLED
using Unity.InferenceEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
#endif

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    public enum UnityInferenceProviderMode
    {
        ObjectDetection,
        ImageSegmentation,
        Chat
    }

    /// <summary>
    /// Provider for on-device AI inference using Unity Inference Engine.
    /// Supports object detection and text-only LLM chat (SmolLM, Qwen, Phi, GPT-2).
    /// </summary>
    /// <remarks>
    /// See the "Model Conversion, Serialization, and Quantization" guide in the docs for preparing .sentis assets:
    /// https://developers.meta.com/horizon/documentation/unity/unity-ai-unity-inference-engine
    /// </remarks>
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/On-Device/Unity Inference Engine")]
    public sealed class UnityInferenceEngineProvider : AIProviderBase, IObjectDetectionTask, IChatTask, IImageSegmentationTask
    {
        private struct DetectionDto
        {
            public Vector4 Box;
            public float Score;
            public int ID;
        }

        protected override InferenceType DefaultSupportedTypes => InferenceType.OnDevice;

#if UNITY_INFERENCE_INSTALLED
        [Tooltip("Select the inference mode: Object Detection or Chat (LLM)")]
        [SerializeField] internal UnityInferenceProviderMode mode = UnityInferenceProviderMode.ObjectDetection;

        [Tooltip("Embedded model asset to load (use when not using StreamingAssets).")]
        [SerializeField] internal ModelAsset modelFile;

        [Tooltip("Reference to a .sentis file under Assets/StreamingAssets used at runtime.")]
        [SerializeField] internal Object streamingAssetModel;

        [Tooltip("If enabled, load the model from StreamingAssets (.sentis) instead of the embedded ModelAsset.")]
        [SerializeField] internal bool useStreamingAsset;

        [Tooltip("Derived filename of the selected .sentis in StreamingAssets (set by the editor).")]
        [SerializeField, HideInInspector] internal string streamingAssetFileName = "";

        [Tooltip("Editor-only content identifier for the embedded model (used by internal tooling).")]
        [SerializeField, HideInInspector] internal ulong modelContentId;

        [Tooltip("Editor-only content identifier for the class labels asset (used by internal tooling).")]
        [SerializeField, HideInInspector] internal ulong classLabelsContentId;

        [Tooltip("Unity Inference Engine backend to run the model with (GPUCompute recommended on Quest 3).")]
        [SerializeField] internal BackendType backend = BackendType.GPUCompute;

        [Tooltip("Text file with one class label per line (index matches model class IDs).")]
        [SerializeField] internal TextAsset classLabelsAsset;

        [Tooltip("Filter specific class labels (indices) for segmentation/detection. Empty = all classes.")]
        [SerializeField] internal List<int> selectedClassLabelIndices = new();

        [Tooltip("Spread model execution across multiple frames to reduce CPU spikes.")]
        [SerializeField] internal bool splitOverFrames;

        [Tooltip("Number of layers to execute per frame when Split Over Frames is enabled.")]
        [Range(1, 100)]
        [SerializeField] internal int layersPerFrame = 22;

        [Tooltip("Model input width (usually overridden by the model’s actual input shape at runtime).")]
        [SerializeField] internal int inputWidth = 640;

        [Tooltip("Model input height (usually overridden by the model's actual input shape at runtime).")]
        [SerializeField] internal int inputHeight = 640;

        [Tooltip("Minimum confidence score for a detection to be kept (after NMS).")]
        [SerializeField]
        [Range(0f, 1f)] internal float scoreThreshold = 0.50f;

        [Tooltip("Configuration for on-device text-only LLM chat (SmolLM, Qwen, Phi, GPT-2, etc.)")]
        [SerializeField] internal OnDeviceLlmConfig llmConfig;

        private readonly SemaphoreSlim _llmSemaphore = new(1, 1);
        private TextOnlyLlmRunner _llmRunner;
        private Task _llmInitTask;
        private Model _model;
        private Worker _worker;
        private CommandBuffer _cb;
        private Tensor<float> _input;
        private string[] _classLabels;
        private Task _ensureWorkerTask;
        private MemoryStream _encodeStream;
        private BinaryWriter _encodeWriter;
        private HashSet<int> _selectedClassLabelIndicesSet;
        private List<DetectionDto> _reusableDetections;
        private List<int> _reusableFilteredIndices;
        private bool[] _reusableSuppressed;
        private Vector4[] _reusableScaledBoxes;
#endif
        public bool SupportsVision => false;

        private void Awake()
        {
#if UNITY_INFERENCE_INSTALLED
            if (classLabelsAsset)
            {
                _classLabels = classLabelsAsset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            }
#endif
        }

#if UNITY_INFERENCE_INSTALLED
        /// <summary>
        /// Warms up the inference worker by running a dummy forward pass.
        /// Call this in Awake/Start to avoid first-inference latency spikes in real-time scenarios.
        /// </summary>
        public async Task WarmUp()
        {
            if (mode == UnityInferenceProviderMode.Chat)
            {
                await EnsureLlmRunnerAsync();
            }
            else
            {
                await EnsureWorkerAsync();
                var tex = new Texture2D(8, 8, TextureFormat.RGB24, false);
                TextureConverter.ToTensor(tex, _input);
                _worker.Schedule(_input);
                _worker.PeekOutput(0).CompleteAllPendingOperations();
                DestroyImmediate(tex);
            }
        }

        private async Task EnsureLlmRunnerAsync()
        {
            if (_llmRunner != null)
            {
                return;
            }

            if (llmConfig == null)
            {
                Debug.LogError("[UnityInferenceEngineProvider] LLM config not assigned. Cannot warm up.");
                return;
            }

            _llmRunner = new TextOnlyLlmRunner(llmConfig);
            await _llmRunner.LoadModelAsync(modelFile, useStreamingAsset, streamingAssetFileName);
        }

        private async Task EnsureWorkerAsync()
        {
            if (_worker != null && _input != null)
            {
                return;
            }

            if (!modelFile || (useStreamingAsset && !string.IsNullOrEmpty(streamingAssetFileName)))
            {
                var srcPath = Path.Combine(Application.streamingAssetsPath, streamingAssetFileName);

                if (srcPath.Contains("://") || srcPath.Contains("jar:"))
                {
                    var dstPath = Path.Combine(Application.persistentDataPath, streamingAssetFileName);
                    if (!File.Exists(dstPath))
                    {
                        using var req = UnityWebRequest.Get(srcPath);
                        req.downloadHandler = new DownloadHandlerFile(dstPath);
                        var op = req.SendWebRequest();
                        while (!op.isDone)
                        {
                          await Task.Yield();
                        }

                        if (req.result != UnityWebRequest.Result.Success)
                        {
                            Debug.LogError($"Failed to extract .sentis from StreamingAssets: {srcPath}\n{req.error}");
                            throw new IOException(req.error);
                        }
                    }
                    _model = ModelLoader.Load(dstPath);
                }
                else
                {
                    _model = ModelLoader.Load(srcPath);
                }
            }
            else
            {
                _model = ModelLoader.Load(modelFile);
            }

            _worker = new Worker(_model, backend);

            var sh = _model.inputs[0].shape.ToIntArray();
            _input = new Tensor<float>(new TensorShape(sh));

            inputHeight = sh[2];
            inputWidth  = sh[3];

            _classLabels = classLabelsAsset
                ? classLabelsAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>();

            _cb = new CommandBuffer { name = "ObjDet_ToTensor+Schedule" };
            _cb.SetExecutionFlags(CommandBufferExecutionFlags.AsyncCompute);
        }

        private void OnDisable()
        {
            _worker?.Dispose();
            _input?.Dispose();
            _cb?.Dispose();
            _llmRunner?.Dispose();
            _llmSemaphore?.Dispose();
            _worker = null;
            _input = null;
            _model = null;
            _llmRunner = null;
            _reusableDetections = null;
            _reusableFilteredIndices = null;
            _reusableSuppressed = null;
            _reusableScaledBoxes = null;
        }

        private async Task ScheduleWorker(CancellationToken ct)
        {
            if (splitOverFrames)
            {
                var it = _worker.ScheduleIterable(_input);
                var stepBudget = Mathf.Max(1, layersPerFrame);
                var steps = 0;
                bool hasNext;
                do
                {
                    Profiler.BeginSample($"InferenceEngine.ScheduleIterable.MoveNext {steps}");
                    hasNext = it.MoveNext();
                    Profiler.EndSample();

                    steps++;
                    if (steps % stepBudget == 0)
                    {
                        await Task.Yield();
                        if (steps == stepBudget)
                        {
                            await Task.Yield();
                        }
                    }
                    ct.ThrowIfCancellationRequested();
                } while (hasNext);
            }
            else
            {
                _worker.Schedule(_input);
            }
        }
#endif

        /// <summary>
        /// Performs object detection on any <see cref="Texture"/> input (e.g., <see cref="Texture2D"/> or <see cref="RenderTexture"/>)
        /// using the Unity Inference Engine. The model runs entirely on-device, producing bounding boxes,
        /// scores, and class IDs, which are filtered via Non-Maximum Suppression (NMS) on the CPU.
        /// </summary>
        /// <param name="src">The source <see cref="Texture"/> to process. Must be readable on GPU.</param>
        /// <param name="ct">Optional <see cref="CancellationToken"/> to abort inference if needed.</param>
        /// <returns>
        /// An array of <see cref="AIProviderBase.ObjectDetectionPrediction"/> containing detected objects with bounding boxes,
        /// scores, and class labels.
        /// </returns>
        public async Task<ObjectDetectionPrediction[]> DetectAsync(Texture src, CancellationToken ct = default)
        {
#if UNITY_INFERENCE_INSTALLED
            if (!src)
            {
                throw new ArgumentNullException(nameof(src));
            }

            await EnsureWorkerAsync();

            // Avoid GPU command buffer when using CPU backend
            if (backend == BackendType.CPU)
            {
                // Pure CPU path - direct texture-to-tensor conversion
                TextureConverter.ToTensor(src, _input);
                await ScheduleWorker(ct);
            }
            else
            {
                // GPU path - use async compute command buffer
                _cb.Clear();
                _cb.ToTensor(src, _input);
                Graphics.ExecuteCommandBuffer(_cb);
                await ScheduleWorker(ct);
            }

            var inW = _input.shape[3];
            var inH = _input.shape[2];
            var scaleX = (float)src.width / inW;
            var scaleY = (float)src.height / inH;

            var boxesT = _worker.PeekOutput(0) as Tensor<float>;
            var idsT = _worker.PeekOutput(1) as Tensor<int>;
            var scT = _worker.PeekOutput(2) as Tensor<float>;
            if (boxesT == null || idsT == null || scT == null)
            {
                return Array.Empty<ObjectDetectionPrediction>();
            }

            var n = scT.shape[0];

            var boxT = await boxesT.ReadbackAndCloneAsync();
            var idT = await idsT.ReadbackAndCloneAsync();
            var scTt = await scT.ReadbackAndCloneAsync();

            List<DetectionDto> detections;
            using (boxT)
            using (idT)
            using (scTt)
            {
                var boxesArr = boxT.DownloadToArray();
                var idsArr = idT.DownloadToArray();
                var scoresArr = scTt.DownloadToArray();

                detections = RunObjectDetectionNms(boxesArr, scoresArr, idsArr, n, scaleX, scaleY);
            }

            var predictions = new ObjectDetectionPrediction[detections.Count];
            for (var i = 0; i < detections.Count; i++)
            {
                var d = detections[i];
                var label = (_classLabels != null && d.ID >= 0 && d.ID < _classLabels.Length)
                    ? _classLabels[d.ID]
                    : $"cls_{d.ID}";

                predictions[i] = new ObjectDetectionPrediction
                {
                    box = new[] { d.Box.x, d.Box.y, d.Box.z, d.Box.w },
                    score = d.Score,
                    label = label
                };
            }

            return predictions;
#else
            Debug.LogError("[UnityInferenceProvider] Unity Inference Engine package is not installed.");
            return await Task.FromResult<AIProviderBase.ObjectDetectionPrediction[]>(null);
#endif
        }

        /// <summary>
        /// Overload of <see cref="DetectAsync(Texture, CancellationToken)"/> that accepts a <see cref="RenderTexture"/>.
        /// This avoids an unnecessary GPU blit by forwarding the call to the <see cref="Texture"/> overload directly.
        /// </summary>
        /// <param name="src">Source <see cref="RenderTexture"/> to analyze.</param>
        /// <param name="ct">Optional <see cref="CancellationToken"/> to abort the operation.</param>
        /// <returns>
        /// An array of <see cref="AIProviderBase.ObjectDetectionPrediction"/> containing detected objects with bounding boxes,
        /// scores, and class labels.
        /// </returns>
        public async Task<ObjectDetectionPrediction[]> DetectAsync(RenderTexture src, CancellationToken ct = default)
        {
#if UNITY_INFERENCE_INSTALLED
            if (!src)
            {
                throw new ArgumentNullException(nameof(src));
            }

            return await DetectAsync((Texture)src, ct);
#else
            return await Task.FromResult<AIProviderBase.ObjectDetectionPrediction[]>(null);
#endif
        }

#if UNITY_INFERENCE_INSTALLED
        private List<DetectionDto> RunObjectDetectionNms(float[] boxesArr, float[] scoresArr, int[] idsArr, int n, float scaleX, float scaleY, float iouThreshold = 0.5f)
        {
            _reusableDetections ??= new List<DetectionDto>();
            _reusableDetections.Clear();

            _reusableFilteredIndices ??= new List<int>();
            _reusableFilteredIndices.Clear();
            for (var i = 0; i < n; i++)
            {
                var score = scoresArr[i];
                if (score < scoreThreshold) continue;

                var classId = idsArr[i];
                if (IsClassIdFiltered(classId)) continue;

                _reusableFilteredIndices.Add(i);
            }

            if (_reusableFilteredIndices.Count == 0)
            {
                return _reusableDetections;
            }

            _reusableFilteredIndices.Sort((a, b) => scoresArr[b].CompareTo(scoresArr[a]));

            if (_reusableSuppressed == null || _reusableSuppressed.Length < _reusableFilteredIndices.Count)
            {
                _reusableSuppressed = new bool[_reusableFilteredIndices.Count];
            }
            else
            {
                Array.Clear(_reusableSuppressed, 0, _reusableFilteredIndices.Count);
            }

            if (_reusableScaledBoxes == null || _reusableScaledBoxes.Length < _reusableFilteredIndices.Count)
            {
                _reusableScaledBoxes = new Vector4[_reusableFilteredIndices.Count];
            }

            for (var i = 0; i < _reusableFilteredIndices.Count; i++)
            {
                var idx = _reusableFilteredIndices[i];
                var boxOffset = idx * 4;
                _reusableScaledBoxes[i] = new Vector4(
                    boxesArr[boxOffset + 0] * scaleX,
                    boxesArr[boxOffset + 1] * scaleY,
                    boxesArr[boxOffset + 2] * scaleX,
                    boxesArr[boxOffset + 3] * scaleY);
            }

            for (var i = 0; i < _reusableFilteredIndices.Count; i++)
            {
                if (_reusableSuppressed[i])
                {
                    continue;
                }

                var idx = _reusableFilteredIndices[i];
                var classId = idsArr[idx];
                var score = scoresArr[idx];
                var box = _reusableScaledBoxes[i];

                _reusableDetections.Add(new DetectionDto
                {
                    Box = box,
                    Score = score,
                    ID = classId
                });

                for (var j = i + 1; j < _reusableFilteredIndices.Count; j++)
                {
                    if (_reusableSuppressed[j])
                    {
                        continue;
                    }

                    var jBox = _reusableScaledBoxes[j];

                    if (!AabbOverlap(box, jBox)) continue;

                    var iou = IoU(box, jBox);
                    if (iou > iouThreshold)
                    {
                        _reusableSuppressed[j] = true;
                    }
                }
            }

            return _reusableDetections;
        }

        private static bool AabbOverlap(Vector4 a, Vector4 b)
        {
            var aMinX = a.x - a.z * 0.5f;
            var aMaxX = a.x + a.z * 0.5f;
            var aMinY = a.y - a.w * 0.5f;
            var aMaxY = a.y + a.w * 0.5f;

            var bMinX = b.x - b.z * 0.5f;
            var bMaxX = b.x + b.z * 0.5f;
            var bMinY = b.y - b.w * 0.5f;
            var bMaxY = b.y + b.w * 0.5f;

            return !(aMaxX < bMinX || bMaxX < aMinX || aMaxY < bMinY || bMaxY < aMinY);
        }

        private static float IoU(Vector4 a, Vector4 b)
        {
            var aMin = new Vector2(a.x - a.z * 0.5f, a.y - a.w * 0.5f);
            var aMax = new Vector2(a.x + a.z * 0.5f, a.y + a.w * 0.5f);
            var bMin = new Vector2(b.x - b.z * 0.5f, b.y - b.w * 0.5f);
            var bMax = new Vector2(b.x + b.z * 0.5f, b.y + b.w * 0.5f);

            var interMin = Vector2.Max(aMin, bMin);
            var interMax = Vector2.Min(aMax, bMax);
            var interSize = Vector2.Max(interMax - interMin, Vector2.zero);

            var interArea = interSize.x * interSize.y;
            var areaA = a.z * a.w;
            var areaB = b.z * b.w;

            return interArea / (areaA + areaB - interArea + 1e-6f);
        }

        private bool IsClassIdFiltered(int classId)
        {
            if (selectedClassLabelIndices == null || selectedClassLabelIndices.Count == 0)
            {
                return false;
            }

            _selectedClassLabelIndicesSet ??= new HashSet<int>(selectedClassLabelIndices);
            return !_selectedClassLabelIndicesSet.Contains(classId);
        }

#endif

        /// <summary>
        /// (Not yet implemented) Performs object detection on a raw image byte array (JPG or PNG format)
        /// and returns results as a JSON string. Intended for CPU-based or cloud provider implementations.
        /// </summary>
        public Task<string> DetectAsync(byte[] imageJpgOrPng, CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Performs on-device text-only LLM chat inference using Unity Inference Engine.
        /// Supports streaming token generation with proven approach from SmolLM, Qwen, and Phi models.
        /// </summary>
        /// <param name="req">Chat request containing text prompt (images are ignored for text-only models).</param>
        /// <param name="stream">Optional progress reporter for streaming token-by-token responses.</param>
        /// <param name="ct">Cancellation token to abort inference.</param>
        /// <returns>Complete chat response with generated text.</returns>
        public async Task<ChatResponse> ChatAsync(ChatRequest req, IProgress<ChatDelta> stream = null, CancellationToken ct = default)
        {
#if UNITY_INFERENCE_INSTALLED
            if (req == null || string.IsNullOrEmpty(req.text))
            {
                Debug.LogWarning("[UnityInferenceEngineProvider] Chat request is null or empty. Returning empty response.");
                return new ChatResponse(string.Empty);
            }

            if (llmConfig == null)
            {
                Debug.LogError("[UnityInferenceEngineProvider] LLM config not assigned. Please assign tokenizer files and model parameters.");
                return new ChatResponse("Error: LLM configuration missing.");
            }

            await EnsureLlmRunnerAsync();

            if (_llmRunner == null)
            {
                Debug.LogError("[UnityInferenceEngineProvider] Failed to initialize LLM runner. Check logs for model loading errors.");
                return new ChatResponse(string.Empty);
            }

            await _llmSemaphore.WaitAsync(ct);

            try
            {
                var responseText = new System.Text.StringBuilder();
                await foreach (var tokenText in _llmRunner.GenerateAsync(req.text, llmConfig.defaultSystemMessage, ct))
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    responseText.Append(tokenText);
                    stream?.Report(new ChatDelta(tokenText));
                }

                return new ChatResponse(responseText.ToString());
            }
            finally
            {
                _llmSemaphore.Release();
            }
#else
            Debug.LogError("[UnityInferenceEngineProvider] Unity Inference Engine package is not installed.");
            return await Task.FromResult(new ChatResponse(string.Empty));
#endif
        }

        /// <summary>
        /// Performs semantic segmentation on a <see cref="RenderTexture"/> using the Unity Inference Engine.
        /// Returns per-pixel masks, bounding boxes, class IDs, and other metadata for visualization
        /// or further processing.
        /// </summary>
        /// <param name="src">Source <see cref="RenderTexture"/> containing the image to segment.</param>
        /// <param name="ct">Optional cancellation token to interrupt segmentation.</param>
        /// <returns>A <see cref="SegmentationResult"/> containing masks, boxes, and class labels.</returns>
        public async Task<SegmentationResult> SegmentAsync(RenderTexture src, CancellationToken ct = default)
        {
#if UNITY_INFERENCE_INSTALLED
            if (!src)
            {
                throw new ArgumentNullException(nameof(src));
            }

            await EnsureWorkerAsync();
            TextureConverter.ToTensor(src, _input);
            await ScheduleWorker(ct);

            var boxesT = _worker.PeekOutput(0) as Tensor<float>;
            var scoresT = _worker.PeekOutput(1) as Tensor<float>;
            var maskCoeffsT = _worker.PeekOutput(2) as Tensor<float>;
            var maskPrototypesT = _worker.PeekOutput(3) as Tensor<float>;

            int numObjects, maskHeight, maskWidth;
            float[] boxes;
            int[] classIds;
            float[] scores;
            float[] masks;

            using (boxesT)
            using (scoresT)
            using (maskCoeffsT)
            using (maskPrototypesT)
            {
                var numDetections = boxesT.shape[0];
                var numClasses = scoresT.shape[1];
                var maskChannels = maskCoeffsT.shape[1];
                maskHeight = maskPrototypesT.shape[1];
                maskWidth = maskPrototypesT.shape[2];

                if (numDetections == 0)
                {
                    return CreateEmptySegmentationResult(maskWidth, maskHeight);
                }

                var boxesClone = await boxesT.ReadbackAndCloneAsync();
                var scoresClone = await scoresT.ReadbackAndCloneAsync();
                var maskCoeffsClone = await maskCoeffsT.ReadbackAndCloneAsync();
                var maskPrototypesClone = await maskPrototypesT.ReadbackAndCloneAsync();

                var allBoxes = boxesClone.DownloadToArray();
                var allScores = scoresClone.DownloadToArray();
                var allMaskCoeffs = maskCoeffsClone.DownloadToArray();
                var maskPrototypes = maskPrototypesClone.DownloadToArray();

                boxesClone.Dispose();
                scoresClone.Dispose();
                maskCoeffsClone.Dispose();
                maskPrototypesClone.Dispose();

                // Step 1: Find max score and class ID for each detection (CPU)
                var maxScores = new float[numDetections];
                var maxClasses = new int[numDetections];
                for (var i = 0; i < numDetections; i++)
                {
                    var maxScore = float.MinValue;
                    var maxClass = 0;
                    for (var c = 0; c < numClasses; c++)
                    {
                        var score = allScores[i * numClasses + c];
                        if (score > maxScore)
                        {
                            maxScore = score;
                            maxClass = c;
                        }
                    }
                    maxScores[i] = maxScore;
                    maxClasses[i] = maxClass;
                }

                // Step 2: Convert boxes from corner format to center format for NMS (CPU)
                var centerBoxes = new float[numDetections * 4];
                for (var i = 0; i < numDetections; i++)
                {
                    var x1 = allBoxes[i * 4 + 0];
                    var y1 = allBoxes[i * 4 + 1];
                    var x2 = allBoxes[i * 4 + 2];
                    var y2 = allBoxes[i * 4 + 3];

                    centerBoxes[i * 4 + 0] = (x1 + x2) / 2.0f; // centerX
                    centerBoxes[i * 4 + 1] = (y1 + y2) / 2.0f; // centerY
                    centerBoxes[i * 4 + 2] = x2 - x1; // width
                    centerBoxes[i * 4 + 3] = y2 - y1; // height
                }

                // Step 3: Run CPU-based NMS
                var keepIndices = RunSegmentationNms(centerBoxes, maxScores, maxClasses, numDetections);

                if (keepIndices.Count == 0)
                {
                    return CreateEmptySegmentationResult(maskWidth, maskHeight);
                }

                numObjects = keepIndices.Count;
                boxes = new float[numObjects * 4];
                classIds = new int[numObjects];
                scores = new float[numObjects];
                masks = new float[numObjects * maskHeight * maskWidth];

                // Step 4: Extract surviving detections and generate masks
                for (var i = 0; i < numObjects; i++)
                {
                    var detIdx = keepIndices[i];

                    for (var j = 0; j < 4; j++)
                    {
                        boxes[i * 4 + j] = centerBoxes[detIdx * 4 + j];
                    }

                    classIds[i] = maxClasses[detIdx];
                    scores[i] = maxScores[detIdx];

                    var coeffs = new float[maskChannels];
                    var coeffOffset = detIdx * maskChannels;
                    for (var c = 0; c < maskChannels; c++)
                    {
                        coeffs[c] = allMaskCoeffs[coeffOffset + c];
                    }

                    var maskOffset = i * maskHeight * maskWidth;
                    var pixelCount = maskHeight * maskWidth;

                    for (var p = 0; p < pixelCount; p++)
                    {
                        float maskValue = 0;

                        for (var c = 0; c < maskChannels; c++)
                        {
                            var proto = maskPrototypes[c * pixelCount + p];
                            maskValue += coeffs[c] * proto;
                        }

                        maskValue = 1.0f / (1.0f + Mathf.Exp(-maskValue));
                        masks[maskOffset + p] = maskValue;
                    }
                }

                for (var i = 0; i < numObjects; i++)
                {
                    var o = i * 4;
                    boxes[o + 0] /= inputWidth; // centerX
                    boxes[o + 1] /= inputHeight; // centerY
                    boxes[o + 2] /= inputWidth; // width
                    boxes[o + 3] /= inputHeight; // height
                }
            }

            return new SegmentationResult
            {
                inputWidth = inputWidth,
                inputHeight = inputHeight,
                maskWidth = maskWidth,
                maskHeight = maskHeight,
                numObjects = numObjects,
                boxes = boxes,
                classIds = classIds,
                scores = scores,
                masks = masks,
                labels = _classLabels,
                maskAreLogits = false
            };
#else
            Debug.LogError("[UnityInferenceProvider] Unity Inference Engine package is not installed.");
            return await Task.FromResult(new SegmentationResult());
#endif
        }

#if UNITY_INFERENCE_INSTALLED
        private SegmentationResult CreateEmptySegmentationResult(int maskWidth, int maskHeight)
        {
            return new SegmentationResult
            {
                inputWidth = inputWidth,
                inputHeight = inputHeight,
                maskWidth = maskWidth,
                maskHeight = maskHeight,
                numObjects = 0,
                boxes = Array.Empty<float>(),
                classIds = Array.Empty<int>(),
                scores = Array.Empty<float>(),
                masks = Array.Empty<float>(),
                labels = _classLabels,
                maskAreLogits = false
            };
        }
#endif

        private List<int> RunSegmentationNms(float[] centerBoxes, float[] maxScores, int[] maxClasses, int numDetections)
        {
#if UNITY_INFERENCE_INSTALLED
            const float iouThreshold = 0.5f;

            var candidateIndices = new List<int>();
            for (var i = 0; i < numDetections; i++)
            {
                var score = maxScores[i];
                if (score < scoreThreshold) continue;

                var classId = maxClasses[i];
                if (IsClassIdFiltered(classId)) continue;

                candidateIndices.Add(i);
            }

            if (candidateIndices.Count == 0) return new List<int>();

            candidateIndices.Sort((a, b) => maxScores[b].CompareTo(maxScores[a]));

            var classToCandidates = new Dictionary<int, List<int>>();
            for (var i = 0; i < candidateIndices.Count; i++)
            {
                var idx = candidateIndices[i];
                var classId = maxClasses[idx];

                if (!classToCandidates.TryGetValue(classId, out var list))
                {
                    list = new List<int>();
                    classToCandidates[classId] = list;
                }
                list.Add(idx);
            }

            var finalDetectionIndices = new List<int>();

            foreach (var kvp in classToCandidates)
            {
                var indices = kvp.Value;
                var kept = new List<int>();

                for (var i = 0; i < indices.Count; i++)
                {
                    var idx = indices[i];
                    var suppress = false;

                    for (var j = 0; j < kept.Count; j++)
                    {
                        var keptIdx = kept[j];

                        var box1 = new Vector4(
                            centerBoxes[idx * 4 + 0],
                            centerBoxes[idx * 4 + 1],
                            centerBoxes[idx * 4 + 2],
                            centerBoxes[idx * 4 + 3]);

                        var box2 = new Vector4(
                            centerBoxes[keptIdx * 4 + 0],
                            centerBoxes[keptIdx * 4 + 1],
                            centerBoxes[keptIdx * 4 + 2],
                            centerBoxes[keptIdx * 4 + 3]);

                        if (!AabbOverlap(box1, box2)) continue;

                        if (IoU(box1, box2) <= iouThreshold) continue;

                        suppress = true;
                        break;
                    }

                    if (!suppress)
                    {
                        kept.Add(idx);
                        finalDetectionIndices.Add(idx);
                    }
                }
            }

            finalDetectionIndices.Sort((a, b) => maxScores[b].CompareTo(maxScores[a]));
            return finalDetectionIndices;
#else
            Debug.LogError("[UnityInferenceProvider] Unity Inference Engine package is not installed.");
            return new List<int>();
#endif // UNITY_INFERENCE_INSTALLED
        }
    }
}
