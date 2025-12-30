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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
#if UNITY_INFERENCE_INSTALLED
using Unity.InferenceEngine;
#endif
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
#if UNITY_INFERENCE_INSTALLED
    [CreateAssetMenu(menuName = "Meta/AI/Provider Assets/On-Device/Unity Inference Engine")]
#endif
    public sealed class UnityInferenceEngineProvider : AIProviderBase, IObjectDetectionTask
    {
        protected override InferenceType DefaultSupportedTypes => InferenceType.OnDevice;

#if UNITY_INFERENCE_INSTALLED
        [Header("Model")]
        [SerializeField] internal ModelAsset modelFile;
        [SerializeField, HideInInspector] internal ulong modelContentId;
        [SerializeField, HideInInspector] internal ulong classLabelsContentId;

        [SerializeField] internal BackendType backend = BackendType.GPUCompute;
        [SerializeField] internal TextAsset classLabelsAsset;
        [SerializeField] internal bool splitOverFrames = true;
        [Range(1, 100)]
        [SerializeField] internal int layersPerFrame = 20;

        [Header("Input Dimensions")]
        [SerializeField] private int inputWidth = 640;
        [SerializeField] private int inputHeight = 640;

        [Header("Post Processing")]
        [SerializeField] internal ComputeShader nmsShader; // optional GPU NMS
        [SerializeField] private int maxDetections = 100; // Top-K filtering

        private Worker _worker;
        private Tensor<float> _input;
        private Model _model;
        private string[] _classLabels;
        private GpuNms _gpuNms;
#endif

#if UNITY_EDITOR
        private void Reset()
        {
#if UNITY_INFERENCE_INSTALLED
            if (nmsShader == null)
            {
                nmsShader = Resources.Load<ComputeShader>("NMSCompute");
            }
#endif
        }
#endif

        private void Awake()
        {
#if UNITY_INFERENCE_INSTALLED
            if (nmsShader != null)
            {
                _gpuNms = new GpuNms(nmsShader);
            }

            if (classLabelsAsset != null)
            {
                _classLabels = classLabelsAsset.text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            }
#endif
        }

#if UNITY_INFERENCE_INSTALLED
        public void WarmUp()
        {
            EnsureWorker();
            var tex = new Texture2D(8, 8, TextureFormat.RGB24, false);
            TextureConverter.ToTensor(tex, _input);
            _worker.Schedule(_input);
            _worker.PeekOutput(0).CompleteAllPendingOperations();
            DestroyImmediate(tex);
        }

        private void EnsureWorker()
        {
            if (_worker != null && _input != null)
            {
                return;
            }

            _model = ModelLoader.Load(modelFile);
            _worker = new Worker(_model, backend);
            var sh = _model.inputs[0].shape.ToIntArray();
            _input = new Tensor<float>(new TensorShape(sh));
        }

        private void OnDisable()
        {
            _worker?.Dispose(); _input?.Dispose();
            _worker = null; _input = null; _model = null;
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
        public async Task<byte[]> DetectAsync(RenderTexture src, CancellationToken ct = default)
        {
#if UNITY_INFERENCE_INSTALLED
            if (!src)
            {
                throw new ArgumentNullException(nameof(src));
            }

            EnsureWorker();
            TextureConverter.ToTensor(src, _input);
            await ScheduleWorker(ct);

            var inW = _input.shape[3];
            var inH = _input.shape[2];
            var sx = (float)src.width / inW;
            var sy = (float)src.height / inH;

            var boxT = await (_worker.PeekOutput(0) as Tensor<float>)?.ReadbackAndCloneAsync()!;
            var idT  = await (_worker.PeekOutput(1) as Tensor<int>)?.ReadbackAndCloneAsync()!;
            var scT  = await (_worker.PeekOutput(2) as Tensor<float>)?.ReadbackAndCloneAsync()!;

            using (boxT) using (idT) using (scT)
            {
                var cnt = boxT.shape[0];
                var boxes = new List<Vector4>(cnt);
                var scores = new List<float>(cnt);
                var classIds = new List<int>(cnt);

                for (var i = 0; i < cnt; i++)
                {
                    boxes.Add(new Vector4(boxT[i, 0] * sx, boxT[i, 1] * sy, boxT[i, 2] * sx, boxT[i, 3] * sy));
                    scores.Add(scT[i]);
                    classIds.Add(idT[i]);
                }

                return EncodeDetectionsWithNms(boxes, scores, classIds);
            }
#else
            Debug.LogError("[UnityInferenceProvider] Unity Inference Engine package is not installed.");
            return await Task.FromResult<byte[]>(null);
#endif
        }
#if UNITY_INFERENCE_INSTALLED
        private byte[] EncodeDetectionsWithNms(List<Vector4> boxes, List<float> scores, List<int> classIds, float iouThreshold = 0.5f)
        {
            var keepIndices = (_gpuNms != null)
                ? _gpuNms.RunNms(boxes, scores, iouThreshold)
                : DefaultCpuNms(boxes, scores, iouThreshold);

            // sort by score desc
            keepIndices.Sort((a, b) => scores[b].CompareTo(scores[a]));
            if (keepIndices.Count > maxDetections)
            {
                keepIndices = keepIndices.GetRange(0, maxDetections);
            }

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            bw.Write(keepIndices.Count);
            foreach (var idx in keepIndices)
            {
                var b = boxes[idx];
                var score = scores[idx];
                var id = classIds[idx];

                bw.Write(b.x);
                bw.Write(b.y);
                bw.Write(b.z);
                bw.Write(b.w);
                bw.Write(score);
                bw.Write(id);

                var label = (_classLabels != null && id >= 0 && id < _classLabels.Length)
                    ? _classLabels[id]
                    : $"cls_{id}";
                bw.Write(label);
            }
            return ms.ToArray();
        }

        private List<int> DefaultCpuNms(List<Vector4> boxes, List<float> scores, float iouThreshold)
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

#endif
        // Stubbed interface members
        public Task<string> DetectAsync(byte[] imageJpgOrPng, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<byte[]> DetectBinaryAsync(byte[] imageJpgOrPng, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
