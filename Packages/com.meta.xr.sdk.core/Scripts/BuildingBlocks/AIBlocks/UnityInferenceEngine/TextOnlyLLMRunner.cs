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
using UnityEngine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
#if UNITY_INFERENCE_INSTALLED
using Unity.InferenceEngine;
#endif

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// Text-only LLM inference runner using Unity Inference Engine.
    /// Handles autoregressive generation with KV-cache for efficient token-by-token inference.
    /// Based on proven approaches from SmolLM, Qwen, and Phi models.
    /// </summary>
    internal class TextOnlyLlmRunner : IDisposable
    {
        private readonly OnDeviceLlmConfig _config;
#if UNITY_INFERENCE_INSTALLED
        private Worker _engine;
        private Model _model;
        private bool _isCpuBackend;
        private int[] _reusableAttentionBuffer;
#endif

        public TextOnlyLlmRunner(OnDeviceLlmConfig config)
        {
            _config = config;
        }

#if UNITY_INFERENCE_INSTALLED

        public async Task LoadModelAsync(ModelAsset embeddedModel, bool useStreamingAsset,
            string streamingAssetFileName)
        {
            if (!useStreamingAsset || string.IsNullOrEmpty(streamingAssetFileName))
            {
                if (!embeddedModel)
                {
                    Debug.LogError(
                        "[TextOnlyLLMRunner] No model assigned. Assign either embedded model or streaming asset model.");
                    return;
                }

                _model = ModelLoader.Load(embeddedModel);
            }
            else
            {
                var srcPath = System.IO.Path.Combine(Application.streamingAssetsPath, streamingAssetFileName);

                if (srcPath.Contains("://") || srcPath.Contains("jar:"))
                {
                    var dstPath = System.IO.Path.Combine(Application.persistentDataPath, streamingAssetFileName);
                    if (!System.IO.File.Exists(dstPath))
                    {
                        using var req = UnityEngine.Networking.UnityWebRequest.Get(srcPath);
                        req.downloadHandler = new UnityEngine.Networking.DownloadHandlerFile(dstPath);
                        var op = req.SendWebRequest();
                        while (!op.isDone)
                        {
                            await Task.Yield();
                        }

                        if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                        {
                            Debug.LogError(
                                $"[TextOnlyLLMRunner] Failed to extract .sentis from StreamingAssets: {srcPath}\n{req.error}");
                            return;
                        }
                    }

                    _model = ModelLoader.Load(dstPath);
                }
                else
                {
                    if (!System.IO.File.Exists(srcPath))
                    {
                        Debug.LogError($"[TextOnlyLLMRunner] Model file not found: {srcPath}");
                        return;
                    }
                    _model = ModelLoader.Load(srcPath);
                }
            }

            InitializeWorkers();
        }

        private void InitializeWorkers()
        {
            if (_model == null)
            {
                Debug.LogError("[TextOnlyLLMRunner] Model not loaded.");
                return;
            }

            _config.InitializeTokenizer();
            _engine = new Worker(_model, _config.backendType);
            _isCpuBackend = _config.backendType == BackendType.CPU;
            _reusableAttentionBuffer = new int[_config.maxNewTokens + _config.maxPromptLength];

            Warmup();
        }

        private void Warmup()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            using (var dummyInput = new Tensor<int>(new TensorShape(1, 1), new[] { 1 }))
            using (var dummyAttentionMask = new Tensor<int>(new TensorShape(1, 1), new[] { 1 }))
            using (var dummyPositionIds = new Tensor<int>(new TensorShape(1, 1), new[] { 0 }))
            {
                _engine.SetInput("input_ids", dummyInput);
                _engine.SetInput("attention_mask", dummyAttentionMask);
                _engine.SetInput("position_ids", dummyPositionIds);

                var emptyPastShape = new TensorShape(1, _config.numKeyValueHeads, 0, _config.headDim);
                using (var emptyPastTensor = new Tensor<float>(emptyPastShape))
                {
                    for (var i = 0; i < _config.maxLayers; i++)
                    {
                        _engine.SetInput($"past_key_values.{i}.key", emptyPastTensor);
                        _engine.SetInput($"past_key_values.{i}.value", emptyPastTensor);
                    }
                }

                _engine.Schedule();
            }

            stopwatch.Stop();
        }

        public async IAsyncEnumerable<string> GenerateAsync(string userPrompt, string systemMessage = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken ct = default)
        {
            if (_engine == null)
            {
                Debug.LogError("[TextOnlyLLMRunner] Model not loaded. Call LoadModel() first.");
                yield break;
            }

            if (_config.Tokenizer == null)
            {
                Debug.LogError(
                    "[TextOnlyLLMRunner] Tokenizer not initialized. Make sure vocab, merges, and tokenizer config files are assigned in the OnDeviceLLMConfig asset.");
                yield break;
            }

            var formattedPrompt = _config.ApplyChatTemplate(userPrompt, systemMessage);
            var inputTokens = await _config.Tokenizer.EncodeAsync(formattedPrompt, ct);

            if (inputTokens.Count > _config.maxPromptLength)
            {
                Debug.LogWarning(
                    $"[TextOnlyLLMRunner] Truncating prompt from {inputTokens.Count} to {_config.maxPromptLength} tokens to reduce prefill spikes.");
                inputTokens = inputTokens.Skip(inputTokens.Count - _config.maxPromptLength).ToList();
            }

            var outputTokens = new List<int>();

            var prefillLength = inputTokens.Count;
            Tensor<int> inputTensor = null;
            Tensor<int> attentionMaskTensor = null;
            Tensor<int> positionIdsTensor = null;

            try
            {
                inputTensor = new Tensor<int>(new TensorShape(1, prefillLength), inputTokens.ToArray());

                for (var i = 0; i < prefillLength; i++)
                {
                    _reusableAttentionBuffer[i] = 1;
                }

                attentionMaskTensor = new Tensor<int>(new TensorShape(1, prefillLength),
                    new ArraySegment<int>(_reusableAttentionBuffer, 0, prefillLength).ToArray());
                positionIdsTensor = new Tensor<int>(new TensorShape(1, prefillLength),
                    Enumerable.Range(0, prefillLength).ToArray());

                _engine.SetInput("input_ids", inputTensor);
                _engine.SetInput("attention_mask", attentionMaskTensor);
                _engine.SetInput("position_ids", positionIdsTensor);

                var emptyPastShape = new TensorShape(1, _config.numKeyValueHeads, 0, _config.headDim);
                var pastKeys = new Tensor<float>[_config.maxLayers];
                var pastValues = new Tensor<float>[_config.maxLayers];

                for (var i = 0; i < _config.maxLayers; i++)
                {
                    pastKeys[i] = new Tensor<float>(emptyPastShape);
                    pastValues[i] = new Tensor<float>(emptyPastShape);
                    _engine.SetInput($"past_key_values.{i}.key", pastKeys[i]);
                    _engine.SetInput($"past_key_values.{i}.value", pastValues[i]);
                }

                if (_config.inferenceExecutionMode == InferenceExecutionMode.NonBlocking)
                {
                    var iterator = _engine.ScheduleIterable();
                    var stepBudget = Mathf.Max(1, _config.StepsPerFrame / 2);
                    var steps = 0;
                    while (iterator.MoveNext())
                    {
                        steps++;
                        if (steps >= stepBudget)
                        {
                            steps = 0;
                            await Task.Yield();
                        }

                        if (ct.IsCancellationRequested) break;
                    }
                }
                else
                {
                    _engine.Schedule();
                }

                using var outputLogits = _engine.PeekOutput("logits") as Tensor<float>;
                var nextToken = FindArgMaxCpu(outputLogits, prefillLength - 1);

                if (nextToken != _config.eosTokenId)
                {
                    outputTokens.Add(nextToken);
                    yield return _config.Tokenizer.Decode(new List<int> { nextToken });
                }

                var step = 1;
                try
                {
                    while (step < _config.maxNewTokens && nextToken != _config.eosTokenId)
                    {
                        if (ct.IsCancellationRequested) break;

                        for (var i = 0; i < _config.maxLayers; i++)
                        {
                            var kOut = _engine.PeekOutput($"present.{i}.key") as Tensor<float>;
                            var vOut = _engine.PeekOutput($"present.{i}.value") as Tensor<float>;

                            Tensor<float> newKey = null;
                            Tensor<float> newValue = null;

                            if (kOut != null) newKey = await kOut.ReadbackAndCloneAsync();
                            if (vOut != null) newValue = await vOut.ReadbackAndCloneAsync();

                            pastKeys[i]?.Dispose();
                            pastValues[i]?.Dispose();

                            pastKeys[i] = newKey;
                            pastValues[i] = newValue;

                            _engine.SetInput($"past_key_values.{i}.key", pastKeys[i]);
                            _engine.SetInput($"past_key_values.{i}.value", pastValues[i]);
                        }

                        var currentSequenceLength = prefillLength + step;
                        using var newInputTensor = new Tensor<int>(new TensorShape(1, 1), new[] { nextToken });
                        using var newPositionIdsTensor =
                            new Tensor<int>(new TensorShape(1, 1), new[] { currentSequenceLength - 1 });

                        _reusableAttentionBuffer[currentSequenceLength - 1] = 1;
                        using var newAttentionMaskTensor = new Tensor<int>(new TensorShape(1, currentSequenceLength),
                            new ArraySegment<int>(_reusableAttentionBuffer, 0, currentSequenceLength).ToArray());

                        _engine.SetInput("input_ids", newInputTensor);
                        _engine.SetInput("attention_mask", newAttentionMaskTensor);
                        _engine.SetInput("position_ids", newPositionIdsTensor);

                        if (_config.inferenceExecutionMode == InferenceExecutionMode.NonBlocking)
                        {
                            var iterator = _engine.ScheduleIterable();
                            var stepBudget = Mathf.Max(1, _config.StepsPerFrame);
                            var steps = 0;
                            while (iterator.MoveNext())
                            {
                                steps++;
                                if (steps >= stepBudget)
                                {
                                    steps = 0;
                                    await Task.Yield();
                                }

                                if (ct.IsCancellationRequested) break;
                            }
                        }
                        else
                        {
                            _engine.Schedule();
                        }

                        using var newOutputLogits = _engine.PeekOutput("logits") as Tensor<float>;
                        nextToken = FindArgMaxCpu(newOutputLogits, 0);

                        if (nextToken != _config.eosTokenId)
                        {
                            outputTokens.Add(nextToken);
                            yield return _config.Tokenizer.Decode(new List<int> { nextToken });
                        }

                        step++;
                    }
                }
                finally
                {
                    for (var i = 0; i < _config.maxLayers; i++)
                    {
                        pastKeys[i]?.Dispose();
                        pastValues[i]?.Dispose();
                    }
                }
            }
            finally
            {
                inputTensor.Dispose();
                attentionMaskTensor.Dispose();
                positionIdsTensor.Dispose();
            }
        }

        private static int FindArgMaxCpu(Tensor<float> logits, int sequenceIndex)
        {
            var logitsArray = logits.DownloadToArray();
            var vocabSize = logits.shape[2];

            var startIndex = sequenceIndex * vocabSize;
            var maxIndex = 0;
            var maxValue = float.NegativeInfinity;

            for (var i = 0; i < vocabSize; i++)
            {
                var value = logitsArray[startIndex + i];
                if (!(value > maxValue))
                {
                    continue;
                }

                maxValue = value;
                maxIndex = i;
            }

            return maxIndex;
        }
#endif

        public void Dispose()
        {
#if UNITY_INFERENCE_INSTALLED
            _engine?.Dispose();
            _engine = null;
            _model = null;
#endif
        }
    }
}
