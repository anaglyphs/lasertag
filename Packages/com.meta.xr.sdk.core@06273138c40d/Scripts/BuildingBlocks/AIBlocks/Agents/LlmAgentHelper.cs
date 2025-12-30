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
using Meta.XR.ImmersiveDebugger;
using UnityEngine;

namespace Meta.XR.BuildingBlocks.AIBlocks
{
    public enum DefaultPromptOption
    {
        DescribeImage,
        CapitalOfSwitzerland,
        Greeting
    }

    public enum PromptImageSource
    {
        Camera,
        InspectorTexture,
        ImageUrl
    }

    [RequireComponent(typeof(LlmAgent))]
    public sealed class LlmAgentHelper : MonoBehaviour
    {
        [Header("Prompt Selection")]
        [Tooltip("Custom text to send as the prompt. If left empty, the default prompt from 'Selected Prompt' will be used.")]
        [SerializeField] private string userInput;

        [Tooltip("Choose a predefined default prompt if no custom user input is provided.")]
        [SerializeField] private DefaultPromptOption selectedPrompt;

        [Header("Image Source")]
        [Tooltip("Enable or disable sending an image along with the text prompt.")]
        [SerializeField] private bool includeImage = true;

        [Tooltip("Select which image source to use when Include Image is enabled: Camera (fake image when in editor), Inspector Texture, or Image URL.")]
        [SerializeField] private PromptImageSource imageSource = PromptImageSource.Camera;

        [Tooltip("Image asset assigned in the Inspector. Only used when Image Source = InspectorTexture.")]
        [SerializeField] private Texture2D promptImage;

        [Tooltip("Direct URL to an image. Only used when Image Source = ImageUrl.")]
        [SerializeField] private string promptImageUrl;


        private LlmAgent _agent;
        private DefaultPromptOption _lastPrompt;
        private string _lastText;

        private void Awake()
        {
            _agent = GetComponent<LlmAgent>();
            _lastPrompt = selectedPrompt;
            _lastText = GetDefaultPromptText(_lastPrompt);
            if (string.IsNullOrWhiteSpace(userInput))
            {
                userInput = _lastText;
            }

            _agent.onPromptSent.AddListener(Logger);
            _agent.onResponseReceived.AddListener(Logger);
        }

        private void OnDestroy()
        {
            if (_agent == null)
            {
                return;
            }

            _agent.onPromptSent.RemoveListener(Logger);
            _agent.onResponseReceived.RemoveListener(Logger);
        }

        private void Update()
        {
            if (selectedPrompt == _lastPrompt)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(userInput) || userInput == _lastText)
            {
                userInput = GetDefaultPromptText(selectedPrompt);
            }

            _lastText = GetDefaultPromptText(selectedPrompt);
            _lastPrompt = selectedPrompt;
        }

        [DebugMember(Category = "LLM Agent")]
        public void SendPrompt()
        {
            var text = !string.IsNullOrWhiteSpace(userInput) ? userInput : GetDefaultPromptText(selectedPrompt);

            if (string.IsNullOrWhiteSpace(text))
            {
                Debug.LogWarning("[LlmAgentHelper] No prompt to send.");
                return;
            }

            if (!includeImage)
            {
                _ = _agent.SendTextOnlyAsync(text);
                return;
            }

            if (!_agent.ProviderSupportsVision)
            {
                Debug.LogWarning("[LlmAgentHelper] Current provider does not support image input. Sending text only.");
                _ = _agent.SendTextOnlyAsync(text);
                return;
            }

            switch (imageSource)
            {
                case PromptImageSource.Camera:
                    if (_agent.CanCapture)
                    {
                        _ = _agent.SendPromptWithPassthroughImageAsync(text);
                    }
                    else
                    {
                        Debug.LogWarning("[LlmAgentHelper] Passthrough not available and no Editor fake camera. Sending text only.");
                        _ = _agent.SendTextOnlyAsync(text);
                    }
                    break;

                case PromptImageSource.InspectorTexture:
                    if (promptImage)
                    {
                        _ = _agent.SendPromptAsync(text, promptImage);
                    }
                    else
                    {
                        Debug.LogWarning("[LlmAgentHelper] No Inspector texture assigned. Sending text only.");
                        _ = _agent.SendTextOnlyAsync(text);
                    }
                    break;

                case PromptImageSource.ImageUrl:
                    if (!string.IsNullOrWhiteSpace(promptImageUrl))
                    {
                        var img = new ImageInput { url = promptImageUrl };
                        _ = _agent.SendPromptWithImagesAsync(text, new List<ImageInput> { img });
                    }
                    else
                    {
                        Debug.LogWarning("[LlmAgentHelper] Empty image URL. Sending text only.");
                        _ = _agent.SendTextOnlyAsync(text);
                    }
                    break;

                default:
                    _ = _agent.SendTextOnlyAsync(text);
                    break;
            }
        }

        private static string GetDefaultPromptText(DefaultPromptOption o) => o switch
        {
            DefaultPromptOption.DescribeImage => "What do you see on this image?",
            DefaultPromptOption.CapitalOfSwitzerland => "What is the capital of Switzerland?",
            DefaultPromptOption.Greeting => "Hi, how are you?",
            _ => string.Empty
        };

        public static void Logger(string text) => Debug.Log(text);
    }
}
