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
using System.Threading.Tasks;
using UnityEngine.Events;
using UnityEngine;
using System;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    [Serializable] public class StringEvent : UnityEvent<string> { }
    [Serializable] public class Texture2DEvent : UnityEvent<Texture2D> { }

    /// <summary>
    /// Thin chat agent that delegates to an IChatTask provider.
    /// </summary>
    public sealed class LlmAgent : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Provider asset that implements IChatTask (e.g., LlamaApiProvider). This defines how prompts are sent and responses are retrieved.")]
        [SerializeField] internal AIProviderBase providerAsset;

        [Tooltip("Optional system prompt to set context/instructions for the LLM. This is prepended to all user messages.")]
        [TextArea(3, 10)]
        [SerializeField] private string systemPrompt;

        [Tooltip("True if the assigned provider supports multimodal input (text + images).")]
        public bool ProviderSupportsVision => _chatTask is { SupportsVision: true };

        /// <summary>
        /// Gets or sets the system prompt that provides context/instructions to the LLM.
        /// This is prepended to all user messages.
        /// </summary>
        public string SystemPrompt
        {
            get => systemPrompt;
            set => systemPrompt = value;
        }

        private IChatTask _chatTask;
        private UnityInferenceEngineProvider _unityProvider;

        [Header("Events")]
        [Tooltip("Invoked when a prompt is sent to the provider.")]
        public StringEvent onPromptSent;

        [Tooltip("Invoked when a text response is received from the provider.")]
        public StringEvent onResponseReceived;

        [Tooltip("Invoked when a passthrough or debug image is captured and ready to be sent.")]
        public Texture2DEvent onImageCaptured = new();

#if MRUK_INSTALLED
        [Tooltip("Passthrough camera access component (injected automatically when MRUK is installed).")]
        private PassthroughCameraAccess _cam;

        [Tooltip("True if passthrough capture is possible and the camera is actively playing.")]
        public bool CanCapture => _cam && _cam.IsPlaying;
#else
        [Tooltip("False if MRUK is not installed; passthrough capture is unavailable.")]
        public bool CanCapture => false;
#endif

        private void Awake()
        {
            try
            {
                _chatTask = providerAsset as IChatTask;
                if (_chatTask == null)
                {
                    Debug.LogError("LlmAgent: Provider must implement IChatTask.");
                }

#if UNITY_INFERENCE_INSTALLED
                _unityProvider = providerAsset as UnityInferenceEngineProvider;
                if (_unityProvider != null)
                {
                    _ = _unityProvider.WarmUp();
                }
#endif

#if MRUK_INSTALLED
                _cam = FindAnyObjectByType<PassthroughCameraAccess>();
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LlmAgent] Initialization failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>Capture passthrough if possible, otherwise send text only.</summary>
        public async Task SendPromptAsync(string userText)
        {
            if (TryCapturePassthroughImage(out var tex))
            {
                var preview = Instantiate(tex);
                onImageCaptured?.Invoke(preview);
                await SendPromptAsync(userText, tex);
                Destroy(tex);
            }
            else
            {
                await SendPromptAsync(userText, image: null);
            }
        }

        /// <summary>Send with a provided Texture2D (e.g., Inspector texture / fake camera). Pass null to send text only.</summary>
        public Task SendPromptAsync(string userText, Texture2D image)
        {
            List<ImageInput> imgs = null;
            if (image && _chatTask?.SupportsVision == true)
            {
                imgs = new List<ImageInput> { ChatImages.FromTexture(image) };
            }
            return SendPromptCoreAsync(userText, imgs);
        }

        /// <summary>Send with pre-built image inputs (URLs and/or bytes).</summary>
        public Task SendPromptWithImagesAsync(string userText, List<ImageInput> images)
        {
            var imgs = _chatTask?.SupportsVision == true && images is { Count: > 0 } ? images : null;
            return SendPromptCoreAsync(userText, imgs);
        }

        private async Task SendPromptCoreAsync(string userText, List<ImageInput> images)
        {
            var fullPrompt = string.IsNullOrEmpty(systemPrompt)
                ? userText
                : $"{systemPrompt}\n\n{userText}";

            onPromptSent?.Invoke(fullPrompt);

            try
            {
                if (_chatTask == null)
                {
                    HandleError("No IChatTask assigned.");
                    return;
                }

                var req = new ChatRequest(fullPrompt, images);
                var res = await _chatTask.ChatAsync(req);

                HandleSuccess(res?.text ?? string.Empty);
            }
            catch (Exception ex)
            {
                Debug.LogError($"LlmAgent request failed: {ex}");
                HandleError("(error)");
            }
        }

        private void HandleSuccess(string assistantText)
        {
            onResponseReceived?.Invoke(assistantText);
        }

        private void HandleError(string errorMessage)
        {
            Debug.LogError($"LlmAgent: {errorMessage}");
            onResponseReceived?.Invoke(string.Empty);
        }

#if MRUK_INSTALLED
        private bool TryCapturePassthroughImage(out Texture2D texture)
        {
            if (CanCapture)
            {
                texture = CaptureCameraFrame();
                return true;
            }
            texture = null;
            return false;
        }

        private Texture2D CaptureCameraFrame()
        {
            var src = _cam.GetTexture();
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
#else
        private bool TryCapturePassthroughImage(out Texture2D texture)
        {
            texture = null;
            return false;
        }
#endif
    }
}
