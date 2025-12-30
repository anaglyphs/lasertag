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
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Meta.XR.BuildingBlocks.AIBlocks;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.BuildingBlocks.AIBlocks
{
    [InitializeOnLoad]
    internal static class RemoteProviderProfileRegistry
    {
        [Serializable]
        private struct ProviderProfileDefinitions
        {
            public ProviderProfileData[] providerProfiles;
        }

        [Serializable]
        public struct ProviderProfileData
        {
            public string blockId;
            public string inferenceType;
            public string provider;

            // Cloud params
            public string endpoint;
            public string model;

            // OnDevice params
            public string backendType;
            public int layersPerFrame;
            public ulong modelContentId;
            public bool splitOverFrames;
            public ulong classLabelsContentId;

            // Vision
            public bool supportsVision;
            public bool inlineRemoteImages;
            public bool resolveRedirects;
            public int maxInlineBytes;

            // Defaults
            public float temperature;
            public float topP;
            public float repetitionPenalty;
            public int maxCompletionTokens;

            // Speech to Text
            public string language;
            public string sttFormat;
            public bool includeAudioEvents;

            // Text to Speech
            public string voice;
            public string ttsFormat;
            public float speed;
            public string instructions;

            public InferenceType GetInferenceType()
            {
                if (!Enum.TryParse(typeof(InferenceType), inferenceType, out var type))
                {
                    throw new InvalidEnumArgumentException($"Unknown inference type: {inferenceType}");
                }

                return (InferenceType)type;
            }

            public string GetDisplayName()
            {
                return string.IsNullOrEmpty(model)
                    ? $"{provider}"
                    : $"{provider} - {model}";
            }
        }

        private static readonly Dictionary<(string, InferenceType), List<ProviderProfileData>> ProviderProfilesByBlockAndInferenceType = new();
        private static readonly Dictionary<string, InferenceType> InferenceTypesSupportedForBlockId = new();

        public static IEnumerable<ProviderProfileData> AvailableProviderProfilesFor(string blockId,
            InferenceType inferenceType) =>
            ProviderProfilesByBlockAndInferenceType.TryGetValue((blockId, inferenceType), out var profiles)
                ? profiles
                : Enumerable.Empty<ProviderProfileData>();

        public static IEnumerable<InferenceType> AvailableInferenceTypesForBlockId(string blockId)
        {
            var supportedTypes = InferenceTypesSupportedForBlockId.GetValueOrDefault(blockId, InferenceType.None);

            foreach (InferenceType type in Enum.GetValues(typeof(InferenceType)))
            {
                if (type == InferenceType.None)
                {
                    continue;
                }

                if ((supportedTypes & type) != InferenceType.None)
                {
                    yield return type;
                }
            }
        }

        static RemoteProviderProfileRegistry()
        {
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Fetch();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        private static async Task Fetch()
        {
            var result = await UserInterface.RemoteJsonContent<ProviderProfileDefinitions>.Create(
                "provider_profile_definitions.json", 24030454696583743);

            if (!result.IsSuccess)
            {
                return;
            }

            foreach (var providerProfile in result.Content.providerProfiles)
            {
                var inferenceType = providerProfile.GetInferenceType();
                var key = (providerProfile.blockId, inferenceType);

                var list = ProviderProfilesByBlockAndInferenceType.GetValueOrDefault(key, new List<ProviderProfileData>());
                list.Add(providerProfile);
                ProviderProfilesByBlockAndInferenceType[key] = list;

                var prevInferenceType =
                    InferenceTypesSupportedForBlockId.GetValueOrDefault(providerProfile.blockId, InferenceType.None);
                InferenceTypesSupportedForBlockId[providerProfile.blockId] = prevInferenceType | inferenceType;
            }
        }
    }
}
