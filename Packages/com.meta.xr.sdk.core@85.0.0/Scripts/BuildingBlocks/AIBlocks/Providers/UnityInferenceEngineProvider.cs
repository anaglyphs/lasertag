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
    /// Optimized for low latency with GPU compute for real-time XR scenarios.
    /// </summary>
    /// <remarks>
    /// See the "Model Conversion, Serialization, and Quantization" guide in the docs for preparing .sentis assets:
    /// https://developers.meta.com/horizon/documentation/unity/unity-ai-unity-inference-engine
    /// </remarks>
#if UNITY_INFERENCE_INSTALLED
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/On-Device/Unity Inference Engine")]
#endif
    public sealed class UnityInferenceEngineProvider : AIProviderBase, IObjectDetectionTask, IChatTask
    {
        /// <summary>
        /// Indicates that this provider supports only <see cref="InferenceType.OnDevice"/> execution,
        /// meaning all inference runs locally on the headset (using Unity Inference Engine backends).
        /// </summary>
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

        [Tooltip("Spread model execution across multiple frames to reduce CPU/GPU spikes.")]
        [SerializeField] internal bool splitOverFrames = true;

        [Tooltip("Number of layers to execute per frame when Split Over Frames is enabled.")]
        [Range(1, 100)]
        [SerializeField] internal int layersPerFrame = 22;

        [Tooltip("Model input width (usually overridden by the model’s actual input shape at runtime).")]
        [SerializeField] internal int inputWidth = 640;

        [Tooltip("Model input height (usually overridden by the model’s actual input shape at runtime).")]
        [SerializeField] internal int inputHeight = 640;

        [Tooltip("Compute shader used for GPU Non-Maximum Suppression (NMS) and packing results.")]
        [SerializeField] internal ComputeShader nmsShader;

        [Tooltip("Maximum number of detections to keep after NMS.")]
        [SerializeField] internal int maxDetections = 100;

        [Tooltip("Minimum confidence score for a detection to be kept (after NMS).")]
        [SerializeField]
        [Range(0f, 1f)] internal float scoreThreshold = 0.50f;

        [Tooltip("Configuration for on-device text-only LLM chat (SmolLM, Qwen, Phi, GPT-2, etc.)")]
        [SerializeField] internal OnDeviceLlmConfig llmConfig;

        private const int EncodeStreamInitialCapacity = 4 * 1024;
        private readonly SemaphoreSlim _llmSemaphore = new(1, 1);
        private TextOnlyLlmRunner _llmRunner;
        private Task _llmInitTask;
        private Model _model;
        private GpuNms _gpuNms;
        private Worker _worker;
        private CommandBuffer _cb;
        private Tensor<float> _input;
        private string[] _classLabels;
        private Task _ensureWorkerTask;
        private MemoryStream _encodeStream;
        private BinaryWriter _encodeWriter;
        private List<Vector4> _reusableBoxes;
        private List<float> _reusableScores;
        private List<int> _reusableClassIds;
        private HashSet<int> _selectedClassLabelIndicesSet;
#endif
        public bool SupportsVision => false;

#if UNITY_EDITOR
        private void Reset()
        {
#if UNITY_INFERENCE_INSTALLED
            if (nmsShader == null)
            {
                nmsShader = UnityEngine.Resources.Load<ComputeShader>("NMSCompute");
            }
#endif
        }
#endif

        private void Awake()
        {
#if UNITY_INFERENCE_INSTALLED
            if (nmsShader)
            {
                _gpuNms = new GpuNms(nmsShader);
            }

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

            if (_gpuNms == null && nmsShader)
            {
                _gpuNms = new GpuNms(nmsShader);
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

            _reusableBoxes = new List<Vector4>();
            _reusableScores = new List<float>();
            _reusableClassIds = new List<int>();
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
        }

        private async Task ScheduleWorker(CancellationToken ct)
        {
            if (splitOverFrames)
            {
                var it = _worker.ScheduleIterable(_input);
                var stepBudget = Mathf.Max(1, layersPerFrame);
                var steps = 0;
                while (it.MoveNext())
                {
                    if (ct.IsCancellationRequested)
                    {
                        ct.ThrowIfCancellationRequested();
                    }

                    if ((++steps % stepBudget) == 0)
                    {
                        await Task.Yield();
                    }
                }
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
        /// scores, and class IDs, which are filtered via GPU-based Non-Maximum Suppression (NMS) and returned
        /// as a compact binary result.
        /// </summary>
        /// <param name="src">The source <see cref="Texture"/> to process. Must be readable on GPU.</param>
        /// <param name="ct">Optional <see cref="CancellationToken"/> to abort inference if needed.</param>
        /// <returns>
        /// A binary-encoded byte array containing filtered detections in the format:
        /// [count][x,y,w,h,score,classId,label] per detection.
        /// </returns>
        public async Task<byte[]> DetectAsync(Texture src, CancellationToken ct = default)
        {
#if UNITY_INFERENCE_INSTALLED
            if (!src)
            {
                throw new ArgumentNullException(nameof(src));
            }

            await EnsureWorkerAsync();

            _cb.Clear();
            _cb.ToTensor(src, _input);
            Graphics.ExecuteCommandBuffer(_cb);
            await ScheduleWorker(ct);

            var inW = _input.shape[3];
            var inH = _input.shape[2];
            var scaleX = (float)src.width / inW;
            var scaleY = (float)src.height / inH;

            var boxesT = _worker.PeekOutput(0) as Tensor<float>;
            var idsT = _worker.PeekOutput(1) as Tensor<int>;
            var scT = _worker.PeekOutput(2) as Tensor<float>;
            if (boxesT == null || idsT == null || scT == null)
            {
                return Array.Empty<byte>();
            }

            var n = scT.shape[0];

            if (_gpuNms == null || boxesT.dataOnBackend is not ComputeTensorData boxesData || idsT.dataOnBackend is not ComputeTensorData idsData || scT.dataOnBackend is not ComputeTensorData scData)
            {
                var boxT = await boxesT.ReadbackAndCloneAsync();
                var idT = await idsT.ReadbackAndCloneAsync();
                var scTt = await scT.ReadbackAndCloneAsync();
                using (boxT)
                using (idT)
                using (scTt)
                {
                    var boxesArr = boxT.DownloadToArray();
                    var idsArr = idT.DownloadToArray();
                    var scoresArr = scTt.DownloadToArray();

                    _reusableBoxes.Clear();
                    _reusableScores.Clear();
                    _reusableClassIds.Clear();

                    for (var i = 0; i < n; i++)
                    {
                        var score = scoresArr[i];
                        if (score < scoreThreshold) continue;

                        var classId = idsArr[i];
                        if (IsClassIdFiltered(classId)) continue;

                        var o = i * 4;
                        _reusableBoxes.Add(new Vector4(
                            boxesArr[o + 0] * scaleX,
                            boxesArr[o + 1] * scaleY,
                            boxesArr[o + 2] * scaleX,
                            boxesArr[o + 3] * scaleY));
                        _reusableScores.Add(score);
                        _reusableClassIds.Add(classId);
                    }

                    return await EncodeDetectionsWithNms(_reusableBoxes, _reusableScores, _reusableClassIds);
                }
            }

            var detections = await _gpuNms.RunNmsAsync(
                boxesData.buffer, scData.buffer, idsData.buffer, n,
                iouThreshold: 0.5f,
                minConfidence: scoreThreshold,
                maxKeep: maxDetections,
                scaleX: scaleX, scaleY: scaleY
            );

            detections = detections.FindAll(d => !IsClassIdFiltered(d.ID));

            _encodeStream ??= new MemoryStream(EncodeStreamInitialCapacity);
            _encodeWriter ??= new BinaryWriter(_encodeStream);
            _encodeStream.Position = 0;
            _encodeStream.SetLength(0);

            _encodeWriter.Write(detections.Count);
            foreach (var d in detections)
            {
                _encodeWriter.Write(d.Box.x);
                _encodeWriter.Write(d.Box.y);
                _encodeWriter.Write(d.Box.z);
                _encodeWriter.Write(d.Box.w);
                _encodeWriter.Write(d.Score);
                _encodeWriter.Write(d.ID);

                var label = (_classLabels != null && d.ID >= 0 && d.ID < _classLabels.Length)
                    ? _classLabels[d.ID]
                    : $"cls_{d.ID}";
                _encodeWriter.Write(label);
            }

            return _encodeStream.ToArray();
#else
            Debug.LogError("[UnityInferenceProvider] Unity Inference Engine package is not installed.");
            return await Task.FromResult<byte[]>(null);
#endif
        }


        /// <summary>
        /// Overload of <see cref="DetectAsync(Texture, CancellationToken)"/> that accepts a <see cref="RenderTexture"/>.
        /// This avoids an unnecessary GPU blit by forwarding the call to the <see cref="Texture"/> overload directly.
        /// </summary>
        /// <param name="src">Source <see cref="RenderTexture"/> to analyze.</param>
        /// <param name="ct">Optional <see cref="CancellationToken"/> to abort the operation.</param>
        /// <returns>
        /// A binary-encoded byte array containing filtered detection results:
        /// [count][x,y,w,h,score,classId,label] per detection.
        /// </returns>
        public async Task<byte[]> DetectAsync(RenderTexture src, CancellationToken ct = default)
        {
#if UNITY_INFERENCE_INSTALLED
            if (!src)
            {
                throw new ArgumentNullException(nameof(src));
            }

            return await DetectAsync((Texture)src, ct);
#else
            return await Task.FromResult<byte[]>(null);
#endif
        }

#if UNITY_INFERENCE_INSTALLED
        private async Task<byte[]> EncodeDetectionsWithNms(List<Vector4> boxes, List<float> scores, List<int> classIds, float iouThreshold = 0.5f)
        {
            var keepIndices = _gpuNms != null
                ? await _gpuNms.RunNmsIndicesAsync(boxes, scores, classIds, iouThreshold, scoreThreshold, maxDetections)
                : DefaultCpuNms(boxes, scores, iouThreshold);

            keepIndices.Sort((a, b) => scores[b].CompareTo(scores[a]));
            if (keepIndices.Count > maxDetections)
            {
                keepIndices = keepIndices.GetRange(0, maxDetections);
            }

            _encodeStream ??= new MemoryStream(EncodeStreamInitialCapacity);
            _encodeWriter ??= new BinaryWriter(_encodeStream);
            _encodeStream.Position = 0;
            _encodeStream.SetLength(0);

            var bw = _encodeWriter;
            bw.Write(keepIndices.Count);

            foreach (var idx in keepIndices)
            {
                var b = boxes[idx];
                var s = scores[idx];
                var id = classIds[idx];

                bw.Write(b.x);
                bw.Write(b.y);
                bw.Write(b.z);
                bw.Write(b.w);
                bw.Write(s);
                bw.Write(id);

                var label = _classLabels != null && id >= 0 && id < _classLabels.Length ? _classLabels[id] : $"cls_{id}";
                bw.Write(label);
            }

            return _encodeStream.ToArray();
        }

        private static List<int> DefaultCpuNms(IReadOnlyList<Vector4> boxes, IReadOnlyList<float> scores, float iouThreshold)
        {
            var kept = new List<int>();
            for (var i = 0; i < boxes.Count; i++)
            {
                var keep = true;
                for (var j = 0; j < boxes.Count; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    if (!(scores[j] > scores[i]) || !(IoU(boxes[i], boxes[j]) > iouThreshold))
                    {
                        continue;
                    }

                    keep = false;
                    break;
                }

                if (keep) kept.Add(i);
            }

            return kept;
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

            if (_selectedClassLabelIndicesSet == null)
            {
                _selectedClassLabelIndicesSet = new HashSet<int>(selectedClassLabelIndices);
            }

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

    }
}
