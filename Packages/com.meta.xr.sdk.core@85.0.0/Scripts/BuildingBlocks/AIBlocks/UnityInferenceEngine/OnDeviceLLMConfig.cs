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
#if UNITY_INFERENCE_INSTALLED
using Unity.InferenceEngine;
#endif

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    /// <summary>
    /// Inference execution mode for balancing speed vs frame rate impact.
    /// </summary>
    public enum InferenceExecutionMode
    {
        /// <summary>
        /// Fast synchronous execution on the main thread. Fastest but causes frame drops during initial inference.
        /// Use this for non-VR applications or when you need the absolute fastest response time.
        /// </summary>
        Blocking,
        /// <summary>
        /// Asynchronous execution that yields control back to Unity's main loop.
        /// Prevents frame drops by distributing work across multiple frames.
        /// Recommended for VR/XR applications where maintaining stable 72-90+ FPS is critical.
        /// Slightly slower overall but maintains smooth frame rate.
        /// </summary>
        NonBlocking
    }

    /// <summary>
    /// Configuration for on-device text-only LLM inference.
    /// Contains model parameters, tokenizer, and chat template formatting.
    ///
    /// IMPORTANT: Different models have different architecture parameters.
    /// You must configure these values to match your specific model, e.g.:
    ///
    /// Qwen2.5-0.5B:
    /// - maxLayers: 24
    /// - numKeyValueHeads: 2
    /// - headDim: 64
    /// - eosTokenId: 151645
    /// - vocabSize: 151936
    ///
    /// Create separate config assets for each model with appropriate parameters.
    /// </summary>
    [Serializable]
    public class OnDeviceLlmConfig
    {
        [Header("Performance Settings")]
        [Tooltip("Inference execution mode:\n" +
            "• Blocking: Fastest response time (2-3 seconds), but will freeze the frame during inference. Best for non-VR or when user expects a pause.\n" +
            "• NonBlocking: Maintains smooth frame rate by distributing work across frames. Slower response (5-10 seconds) but no freezing. Best for VR.")]
        public InferenceExecutionMode inferenceExecutionMode = InferenceExecutionMode.NonBlocking;

        [Tooltip("Steps to process per frame in NonBlocking mode (ignored in Blocking mode). Higher = faster but more frame time. Adjust based on device capability.")]
        [Range(50, 300)]
        public int stepsPerFrame = 150;

        internal int StepsPerFrame => inferenceExecutionMode == InferenceExecutionMode.Blocking ? 1 : stepsPerFrame;

        [Header("Tokenizer Files")]
        [Tooltip("Tokenizer vocabulary file (JSON format with token->id mapping)")]
        public TextAsset vocabFile;

        [Tooltip("Tokenizer merges file (text format with BPE merge rules)")]
        public TextAsset mergesFile;

        [Tooltip("Tokenizer config file (JSON with special tokens and settings)")]
        public TextAsset tokenizerConfigFile;

        [Tooltip("Editor-only content identifier for the vocab file (used by internal tooling).")]
        [HideInInspector]
        public ulong vocabFileContentId;

        [Tooltip("Editor-only content identifier for the merges file (used by internal tooling).")]
        [HideInInspector]
        public ulong mergesFileContentId;

        [Tooltip("Editor-only content identifier for the tokenizer config file (used by internal tooling).")]
        [HideInInspector]
        public ulong tokenizerConfigFileContentId;

        [Tooltip("Chat template format (use {0} for user message placeholder)")]
        public string chatTemplateFormat = "<|im_start|>system\n{1}<|im_end|>\n<|im_start|>user\n{0}<|im_end|>\n<|im_start|>assistant\n";

        [Tooltip("Default system message for chat")]
        public string defaultSystemMessage = "You are a helpful assistant. Answer concisely and clearly in up to three sentences.";

        [Tooltip("Maximum number of layers (transformer blocks) in the model")]
        public int maxLayers = 30;

        [Tooltip("Number of key-value attention heads")]
        public int numKeyValueHeads = 3;

        [Tooltip("Dimension of each attention head")]
        public int headDim = 64;

        [Tooltip("End-of-sequence token ID (model-specific)")]
        public int eosTokenId = 2;

        [Tooltip("Maximum new tokens to generate")]
        public int maxNewTokens = 100;

        [Tooltip("Maximum prompt length in tokens (longer prompts will be truncated to prevent memory spikes)")]
        public int maxPromptLength = 512;

        [Tooltip("Unity Inference Engine backend (CPU recommended for LLMs for better accuracy)")]
#if UNITY_INFERENCE_INSTALLED
        public BackendType backendType = BackendType.CPU;
#endif

        private Gpt2Tokenizer _tokenizer;
        private bool _tokenizerInitialized;

        public Gpt2Tokenizer Tokenizer
        {
            get
            {
                if (!_tokenizerInitialized)
                {
                    InitializeTokenizer();
                }
                return _tokenizer;
            }
        }

        public void InitializeTokenizer()
        {
            if (_tokenizerInitialized && _tokenizer != null)
            {
                return;
            }

            if (!vocabFile || !mergesFile || !tokenizerConfigFile)
            {
                Debug.LogError("[OnDeviceLLMConfig] Tokenizer files not assigned. Please assign vocab, merges, and config files.");
                _tokenizer = null;
                _tokenizerInitialized = false;
                return;
            }

            try
            {
                _tokenizer = new Gpt2Tokenizer();
                _tokenizer.Initialize(vocabFile, mergesFile, tokenizerConfigFile);
                _tokenizerInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OnDeviceLLMConfig] Failed to initialize tokenizer: {ex.Message}\n{ex.StackTrace}");
                _tokenizer = null;
                _tokenizerInitialized = false;
            }
        }

        public string ApplyChatTemplate(string userPrompt, string systemMessage = null)
        {
            if (string.IsNullOrEmpty(systemMessage))
            {
                systemMessage = defaultSystemMessage;
            }
            return string.Format(chatTemplateFormat, userPrompt, systemMessage);
        }
    }
}
