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
    [Serializable] public class StringEvent : UnityEvent<string> { }
    [Serializable] public class Texture2DEvent : UnityEvent<Texture2D> { }

    /// <summary>
    /// Thin chat agent that delegates to an IChatTask provider.
    /// </summary>
    public sealed class LlmAgent : MonoBehaviour
    {
        [Tooltip("Provider asset that implements IChatTask (e.g., LlamaApiProvider). This defines how prompts are sent and responses are retrieved.")]
        [SerializeField] internal AIProviderBase providerAsset;

        [Tooltip("True if the assigned provider supports multimodal input (text + images).")]
        public bool ProviderSupportsVision => _chatTask is { SupportsVision: true };

        private IChatTask _chatTask;

        [Header("Events")]
        [Tooltip("Invoked when a prompt is sent to the provider.")]
        public StringEvent onPromptSent;

        [Tooltip("Invoked when a text response is received from the provider.")]
        public StringEvent onResponseReceived;

        [Tooltip("Invoked when a passthrough or debug image is captured and ready to be sent.")]
        public Texture2DEvent onImageCaptured = new();

        [Tooltip("Internal log of sent prompts and responses for this session.")]
        private readonly List<string> _history = new();

        [Tooltip("Read-only view of the conversation history (prompts + responses).")]
        public IReadOnlyList<string> History => _history;

        [Tooltip("Raised whenever the provider returns an assistant reply. Useful for external listeners.")]
        public event Action<string> OnAssistantReply;

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
            _chatTask = providerAsset as IChatTask;
            if (_chatTask == null)
                Debug.LogError("LlmAgent: chatTaskAsset must implement IChatTask.");
#if MRUK_INSTALLED
            _cam = FindAnyObjectByType<PassthroughCameraAccess>();
#endif
        }

        public Task SendTextOnlyAsync(string userText)
        {
            return SendInternal(userText, imageOrNull: null);
        }

        /// <summary>Capture passthrough if possible, otherwise send text only.</summary>
        public async Task SendPromptAsync(string userText)
        {
#if MRUK_INSTALLED
            if (!CanCapture)
            {
                await SendInternal(userText, null);
                return;
            }

            var tex = CaptureCameraFrame();
            var preview = Instantiate(tex);
            onImageCaptured?.Invoke(preview);
            await SendInternal(userText, tex);
            Destroy(tex);
#else
            await SendInternal(userText, null);
#endif
        }

        /// <summary>Send with a provided Texture2D (e.g., Inspector texture / fake camera).</summary>
        public Task SendPromptAsync(string userText, Texture2D image)
        {
            return SendInternal(userText, image);
        }

        /// <summary>Prefer passthrough image, fall back to text if not available.</summary>
        public async Task SendPromptWithPassthroughImageAsync(string userText)
        {
#if MRUK_INSTALLED
            if (CanCapture)
            {
                var tex = CaptureCameraFrame();
                var preview = Instantiate(tex);
                onImageCaptured?.Invoke(preview);
                await SendInternal(userText, tex);
                Destroy(tex);
                return;
            }
#endif
            Debug.LogWarning("[LlmAgent] Passthrough camera not available – sending text only.");
            await SendInternal(userText, null);
        }

        /// <summary>Send with pre-built image inputs (URLs and/or bytes).</summary>
        public Task SendPromptWithImagesAsync(string userText, List<ImageInput> images)
        {
            return SendInternalWithImages(userText, images);
        }

        private async Task SendInternal(string userText, Texture2D imageOrNull)
        {
            onPromptSent?.Invoke(userText);
            _history.Add($"User: {userText}");

            try
            {
                if (_chatTask == null)
                {
                    Debug.LogError("No IChatTask assigned.");
                    _history.Add("Assistant: (no chat task)");
                    OnAssistantReply?.Invoke(string.Empty);
                    onResponseReceived?.Invoke(string.Empty);
                    return;
                }

                List<ImageInput> imgs = null;
                if (imageOrNull && _chatTask.SupportsVision)
                {
                    imgs = new List<ImageInput> { ChatImages.FromTexture(imageOrNull) };
                }

                var req = new ChatRequest(userText, imgs);
                var res = await _chatTask.ChatAsync(req);
#if UNITY_EDITOR
                if (res?.Raw != null)
                {
                    print($"[LlmAgent] Raw provider JSON:\n{res.Raw}");
                }
#endif
                var assistant = res?.text ?? string.Empty;
                _history.Add($"Assistant: {assistant}");
                OnAssistantReply?.Invoke(assistant);
                onResponseReceived?.Invoke(assistant);
            }
            catch (Exception ex)
            {
                Debug.LogError($"SendPromptAsync failed: {ex}");
                _history.Add("Assistant: (error)");
                OnAssistantReply?.Invoke(string.Empty);
                onResponseReceived?.Invoke(string.Empty);
            }
        }

        private async Task SendInternalWithImages(string userText, List<ImageInput> images)
        {
            onPromptSent?.Invoke(userText);
            _history.Add($"User: {userText}");

            try
            {
                if (_chatTask == null)
                {
                    Debug.LogError("No IChatTask assigned.");
                    _history.Add("Assistant: (no chat task)");
                    OnAssistantReply?.Invoke(string.Empty);
                    onResponseReceived?.Invoke(string.Empty);
                    return;
                }

                var imgs = _chatTask.SupportsVision && images is { Count: > 0 } ? images : null;
                var req = new ChatRequest(userText, imgs);
                var res = await _chatTask.ChatAsync(req);
#if UNITY_EDITOR
                if (res?.Raw != null)
                {
                    print($"[LlmAgent] Raw provider JSON:\n{res.Raw}");
                }
#endif
                var assistant = res?.text ?? string.Empty;
                _history.Add($"Assistant: {assistant}");
                OnAssistantReply?.Invoke(assistant);
                onResponseReceived?.Invoke(assistant);
            }
            catch (Exception ex)
            {
                Debug.LogError($"SendPromptWithImagesAsync failed: {ex}");
                _history.Add("Assistant: (error)");
                OnAssistantReply?.Invoke(string.Empty);
                onResponseReceived?.Invoke(string.Empty);
            }
        }

#if MRUK_INSTALLED
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
#endif
    }
}
