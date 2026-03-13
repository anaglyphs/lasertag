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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Meta.XR.BuildingBlocks;
using Meta.XR.BuildingBlocks.AIBlocks;
using Meta.XR.BuildingBlocks.Editor;
using Meta.XR.Editor.RemoteContent;
using Meta.XR.Editor.Utils;
using Meta.XR.Telemetry;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_INFERENCE_INSTALLED
using Unity.InferenceEngine;
#endif

namespace Meta.XR.Editor.BuildingBlocks.AIBlocks
{
    [InitializeOnLoad]
    internal static class AIBlocksSetupRules
    {
        private static readonly string MetaXRDirectory = Path.Combine("Assets", "MetaXR");

        static AIBlocksSetupRules()
        {
            RegisterInferencePackageNamespaceTask();
#if UNITY_INFERENCE_INSTALLED
            RegisterProviderProfileContentTask();
            RegisterUnityInferenceEngineNmsShaderTask();
#endif // UNITY_INFERENCE_INSTALLED
            RegisterAgentProfileTask();
            RegisterProviderFieldValidationTask();
            EditorApplication.projectChanged += () =>
            {
                ProviderAssetsCache.Clear();
            };
        }

        private static void RegisterInferencePackageNamespaceTask()
        {
            OVRProjectSetup.AddTask(
                conditionalValidity: _ => PackageList.IsPackageInstalled("com.unity.ai.inference") && IsUsingAIBlocks(),
                level: OVRProjectSetup.TaskLevel.Recommended,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ => IsUsingInferenceEngineNamespace(),
                message: "The 'com.unity.ai.inference' package is installed but uses the old Sentis namespace. " +
                         "Unity 6+ with AI Building Blocks requires the Inference Engine namespace. " +
                         "In Package Manager, find the Sentis package, click 'Version History', and install the latest " +
                         "'Recommended' version with the 'Inference Engine' package title.",
                fix: _ => { UnityEditor.PackageManager.UI.Window.Open("com.unity.ai.inference@recommended"); },
                fixMessage: "Open Package Manager to update to Inference Engine"
            );
        }

        private static bool IsUsingAIBlocks()
        {
#if UNITY_INFERENCE_INSTALLED
            var profiles = GetProviderProfiles();
            return profiles?.OfType<UnityInferenceEngineProvider>().Any() ?? false;
#else
            return false;
#endif
        }

        private static bool IsUsingInferenceEngineNamespace()
        {
            var package = PackageList.GetPackage("com.unity.ai.inference");
            if (package == null)
            {
                return true;
            }

            return package.displayName == "Inference Engine";
        }

#if UNITY_INFERENCE_INSTALLED
        private static void RegisterProviderProfileContentTask()
        {
            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Required,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ => IsProviderProfileContentComplete(),
                message:
                $"The {nameof(UnityInferenceEngineProvider)} is missing its runtime model and/or other required files",
                asyncFix: _ => FixBlocksWithMissingContent(),
                fixMessage: $"Install the {nameof(UnityInferenceEngineProvider)}'s runtime model and required files"
            );
        }

        private static void RegisterUnityInferenceEngineNmsShaderTask()
        {
            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Required,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ => AreAllUnityInferenceEngineNmsShadersAssigned(),
                message: $"The {nameof(UnityInferenceEngineProvider)} in Object Detection mode requires an NMS Compute Shader",
                fix: _ => AutoAssignNmsShaders(),
                fixMessage: "Auto-assign NMS Compute Shader to Unity Inference Engine providers"
            );
        }

        private static bool AreAllUnityInferenceEngineNmsShadersAssigned()
        {
            return !GetUnityInferenceEngineProvidersWithMissingNmsShader().Any();
        }

        private static IEnumerable<UnityInferenceEngineProvider> GetUnityInferenceEngineProvidersWithMissingNmsShader()
        {
            return GetProviderProfiles()
                .OfType<UnityInferenceEngineProvider>()
                .Where(profile => profile.mode == UnityInferenceProviderMode.ObjectDetection && profile.nmsShader == null);
        }

        private static void AutoAssignNmsShaders()
        {
            var nmsShader = Resources.Load<ComputeShader>("NMSCompute");
            if (nmsShader == null)
            {
                Debug.LogWarning("[AIBlocksSetupRules] NMSCompute shader not found in Resources folder. Please assign it manually.");
                return;
            }

            foreach (var provider in GetUnityInferenceEngineProvidersWithMissingNmsShader())
            {
                provider.nmsShader = nmsShader;
                EditorUtility.SetDirty(provider);
            }

            AssetDatabase.SaveAssets();
        }

        private static bool IsProviderProfileContentComplete()
        {
            return !GetProviderProfilesWithMissingRequiredContent().Any();
        }
#endif // UNITY_INFERENCE_INSTALLED

        private static void RegisterAgentProfileTask()
        {
            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Required,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ => !GetAgentsWithMissingProfiles().Any(),
                message: "The AI Agent component is missing its provider profile",
                fix: _ => CreateMissingAgentProfiles(),
                fixMessage: "Create the AI Agent provider profile"
            );
        }

        private static void RegisterProviderFieldValidationTask()
        {
            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Required,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ => AreAllProviderFieldsComplete(),
                message: "The Provider is missing required fields",
                fix: _ => SelectFirstIncompleteProfile()
            );
        }

        private static void CreateMissingAgentProfiles()
        {
            foreach (var trackingAgent in GetAgentsWithMissingProfiles())
            {
                try
                {
                    CreateProviderProfileForAgent(trackingAgent);
                }
                catch (Exception ex)
                {
                    IssueTracker.TrackError(IssueTracker.SDK.BuildingBlocks, "ai-provider-profile-creation-error", $"Failed to create provider profile for tracking agent: {ex.Message}");
                }
            }
        }

        private static IEnumerable<AIProviderBase> GetProviderProfilesWithMissingFields() =>
            GetProviderProfiles()
                .Where(profile => GetMissingProviderFields(profile).Length > 0);

        private static bool AreAllProviderFieldsComplete() => !GetProviderProfilesWithMissingFields().Any();

        private static void SelectFirstIncompleteProfile()
        {
            Selection.activeObject = GetProviderProfilesWithMissingFields().First();
        }

        private static IEnumerable<Component> GetAgentsWithMissingProfiles()
        {
            var missingObjectDetectionAgents = XR.BuildingBlocks.Editor.Utils
                .GetBlocksWithType<ObjectDetectionAgent>()
                .Where(agent => agent.providerAsset == null)
                .Cast<Component>();

            var missingLlmAgents = XR.BuildingBlocks.Editor.Utils
                .GetBlocksWithType<LlmAgent>()
                .Where(agent => agent.providerAsset == null)
                .Cast<Component>();

            var missingSttAgents = XR.BuildingBlocks.Editor.Utils
                .GetBlocksWithType<SpeechToTextAgent>()
                .Where(agent => agent.providerAsset == null)
                .Cast<Component>();

            var missingTtsAgents = XR.BuildingBlocks.Editor.Utils
                .GetBlocksWithType<TextToSpeechAgent>()
                .Where(agent => agent.providerAsset == null)
                .Cast<Component>();

            return missingObjectDetectionAgents
                .Concat(missingLlmAgents)
                .Concat(missingSttAgents)
                .Concat(missingTtsAgents);
        }

        private static AIProviderBase CreateProviderForProviderName(string providerName)
        {
            var providerTypeMap = new Dictionary<string, Type>
            {
                { "HuggingFace", typeof(HuggingFaceProvider) },
                { "ElevenLabs", typeof(ElevenLabsProvider) },
                { "LlamaAPI", typeof(LlamaApiProvider) },
                { "Ollama", typeof(OllamaProvider) },
                { "OpenAI", typeof(OpenAIProvider) },
                { "Replicate", typeof(ReplicateProvider) },
#if UNITY_INFERENCE_INSTALLED
                { "UnityInferenceEngine", typeof(UnityInferenceEngineProvider) }
#endif // UNITY_INFERENCE_INSTALLED
            };

            if (providerTypeMap.TryGetValue(providerName, out var type))
            {
                var provider = (AIProviderBase)ScriptableObject.CreateInstance(type);

#if UNITY_INFERENCE_INSTALLED
                if (provider is UnityInferenceEngineProvider uieProvider)
                {
                    if (uieProvider.nmsShader == null)
                    {
                        uieProvider.nmsShader = Resources.Load<ComputeShader>("NMSCompute");
                    }
                }
#endif

                return provider;
            }

            throw new ArgumentException($"Unknown provider name {providerName}");
        }

        private static void CreateProviderProfileForAgent(Component agent)
        {
            var bb = agent.GetComponent<BuildingBlock>();
            var selection = new VariantsSelection();
            selection.SetupForCheckpoint(bb);

            var inferenceType = GetInferenceTypeFromSelection(selection);
            var modelProviderName = GetModelProviderNameFromSelection(selection);
            var selectedProviderAssetName = GetProviderAssetPathFromSelection(selection);

            var selectedProviderProfile = RemoteProviderProfileRegistry
                .AvailableProviderProfilesFor(bb.BlockId, inferenceType)
                .First(provider => provider.GetDisplayName() == modelProviderName);

            AIProviderBase providerProfile = null;

            // Check if user selected an existing provider asset
            if (!string.IsNullOrEmpty(selectedProviderAssetName) &&
                selectedProviderAssetName != AIBlocksInstallationRoutine.CreateNewProviderOption)
            {
                // Find the provider asset by name
                var existingProviders = FindExistingProviderAssets(bb.BlockId);
                providerProfile = existingProviders.FirstOrDefault(p => p.name == selectedProviderAssetName);
            }

            // If no existing provider was found/selected, create a new one
            if (providerProfile == null)
            {
                providerProfile = CreateProviderForProviderName(selectedProviderProfile.provider);
                FillProviderProfileData(providerProfile, selectedProviderProfile);

                Directory.CreateDirectory(MetaXRDirectory);
                var assetPath = GetUniqueProviderAssetPath(bb, selectedProviderProfile, MetaXRDirectory);

                AssetDatabase.CreateAsset(providerProfile, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            switch (agent)
            {
                case ObjectDetectionAgent detectionAgent:
                    detectionAgent.providerAsset = providerProfile;
                    break;
                case LlmAgent llmAgent:
                    llmAgent.providerAsset = providerProfile;
                    break;
                case SpeechToTextAgent speechToTextAgent:
                    speechToTextAgent.providerAsset = providerProfile;
                    break;
                case TextToSpeechAgent textToSpeechAgent:
                    textToSpeechAgent.providerAsset = providerProfile;
                    break;
                default:
                    throw new ArgumentException($"Unknown agent type {agent.GetType()}");
            }

            EditorUtility.SetDirty(agent);
        }

        private static string GetUniqueProviderAssetPath(BuildingBlock bb,
            RemoteProviderProfileRegistry.ProviderProfileData selectedProviderProfile, string directory)
        {
            var fileName = $"{bb.GetBlockData().BlockName}_{selectedProviderProfile.provider}_ProviderProfile"
                .Replace(" ", "");
            const string fileExtension = "asset";

            var count = 0;
            string filePath;

            do
            {
                filePath = Path.Combine(directory,
                    $"{fileName}{(count > 0 ? count.ToString() : "")}.{fileExtension}");
                count++;
            } while (File.Exists(filePath));

            return filePath;
        }

        private static InferenceType GetInferenceTypeFromSelection(VariantsSelection selection)
        {
            return (InferenceType)selection
                .First(variant => variant.MemberInfo.Name == nameof(AIBlocksInstallationRoutine.inferenceType))
                .RawValue;
        }

        private static string GetModelProviderNameFromSelection(VariantsSelection selection)
        {
            return selection
                .First(v => v.MemberInfo.Name == nameof(AIBlocksInstallationRoutine.modelProvider))
                .RawValue.ToString();
        }

        private static string GetProviderAssetPathFromSelection(VariantsSelection selection)
        {
            var variant = selection.FirstOrDefault(v => v.MemberInfo.Name == nameof(AIBlocksInstallationRoutine.providerAssetSelection));
            if (variant == null) return null;

            var providerAssetName = variant.RawValue?.ToString();
            if (string.IsNullOrEmpty(providerAssetName) || providerAssetName == AIBlocksInstallationRoutine.CreateNewProviderOption)
            {
                return null;
            }

            return providerAssetName;
        }

        private static readonly Dictionary<string, Type> BlockInterfaceCache = new();
        private static readonly Dictionary<string, List<AIProviderBase>> ProviderAssetsCache = new();

        public static List<AIProviderBase> FindExistingProviderAssets(string blockId)
        {
            if (ProviderAssetsCache.TryGetValue(blockId, out var cachedProviders))
            {
                return cachedProviders;
            }

            var compatibleProviders = new List<AIProviderBase>();

            var requiredInterface = GetRequiredInterfaceForBlock(blockId);

            if (requiredInterface == null)
            {
                ProviderAssetsCache[blockId] = compatibleProviders;
                return compatibleProviders;
            }

            var allProviderTypes = new List<Type>
            {
                typeof(HuggingFaceProvider),
                typeof(ElevenLabsProvider),
                typeof(LlamaApiProvider),
                typeof(OllamaProvider),
                typeof(OpenAIProvider),
                typeof(ReplicateProvider),
#if UNITY_INFERENCE_INSTALLED
                typeof(UnityInferenceEngineProvider)
#endif
            };

            foreach (var providerType in allProviderTypes)
            {
                var guids = AssetDatabase.FindAssets($"t:{providerType.Name}");

                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var provider = AssetDatabase.LoadAssetAtPath(path, providerType) as AIProviderBase;

                    if (provider != null && requiredInterface.IsInstanceOfType(provider))
                    {
                        compatibleProviders.Add(provider);
                    }
                }
            }

            ProviderAssetsCache[blockId] = compatibleProviders;

            return compatibleProviders;
        }

        private static Type GetRequiredInterfaceForBlock(string blockId)
        {
            if (BlockInterfaceCache.TryGetValue(blockId, out var cachedInterface))
            {
                return cachedInterface;
            }

            var blockData = Meta.XR.BuildingBlocks.Editor.Utils.GetBlockData(blockId);

            if (blockData == null)
            {
                return null;
            }

            var installationRoutines = InstallationRoutine.Registry.Values
                .Where(routine => routine.TargetBlockDataId == blockId)
                .ToList();

            if (!installationRoutines.Any())
            {
                return null;
            }

            var prefab = installationRoutines.First().Prefab;

            if (prefab == null)
            {
                return null;
            }

            var prefabPath = AssetDatabase.GetAssetPath(prefab);

            if (string.IsNullOrEmpty(prefabPath))
            {
                return null;
            }

            var prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
            Type interfaceType = null;

            try
            {
                if (prefabContents.GetComponent<ObjectDetectionAgent>() != null)
                {
                    interfaceType = typeof(IObjectDetectionTask);
                }
                else if (prefabContents.GetComponent<LlmAgent>() != null)
                {
                    interfaceType = typeof(IChatTask);
                }
                else if (prefabContents.GetComponent<SpeechToTextAgent>() != null)
                {
                    interfaceType = typeof(ISpeechToTextTask);
                }
                else if (prefabContents.GetComponent<TextToSpeechAgent>() != null)
                {
                    interfaceType = typeof(ITextToSpeechTask);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabContents);
            }

            if (interfaceType != null)
            {
                BlockInterfaceCache[blockId] = interfaceType;
            }

            return interfaceType;
        }

        private static string[] GetMissingProviderFields(AIProviderBase profile)
        {
            if (profile == null)
            {
                return Array.Empty<string>();
            }

            var fieldsToValidate = profile switch
            {
                ElevenLabsProvider => new[]
                {
                    nameof(ElevenLabsProvider.apiKey), nameof(ElevenLabsProvider.endpoint),
                    nameof(ElevenLabsProvider.model)
                },
                HuggingFaceProvider => new[]
                {
                    nameof(HuggingFaceProvider.apiKey), nameof(HuggingFaceProvider.endpoint),
                    nameof(HuggingFaceProvider.modelId)
                },
                LlamaApiProvider => new[]
                {
                    nameof(LlamaApiProvider.apiKey), nameof(LlamaApiProvider.endpointUrl),
                    nameof(LlamaApiProvider.model)
                },
                OllamaProvider => new[] { nameof(OllamaProvider.host), nameof(OllamaProvider.model) },
                OpenAIProvider => new[]
                    { nameof(OpenAIProvider.apiKey), nameof(OpenAIProvider.model), nameof(OpenAIProvider.apiRoot) },
                ReplicateProvider => new[] { nameof(ReplicateProvider.apiKey), nameof(ReplicateProvider.modelId) },
#if UNITY_INFERENCE_INSTALLED
                UnityInferenceEngineProvider uie when uie.mode == UnityInferenceProviderMode.ObjectDetection => new[]
                {
                    nameof(UnityInferenceEngineProvider.nmsShader)
                },
#endif
                _ => Enumerable.Empty<string>()
            };

            return fieldsToValidate.Where(field =>
                    {
                        var fieldInfo = profile.GetType()
                            .GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (fieldInfo == null)
                        {
                            return false;
                        }

                        var value = fieldInfo.GetValue(profile);
                        return value switch
                        {
                            string stringValue => string.IsNullOrEmpty(stringValue),
                            _ => value == null
                        };
                    }
                )
                .ToArray();
        }

#if UNITY_INFERENCE_INSTALLED
        private static IEnumerable<Task> GetFixTasksForProviderProfiles(UnityInferenceEngineProvider profiles)
        {
            if (profiles.modelFile == null && profiles.modelContentId != 0)
            {
                yield return FixMissingContent<ModelAsset>(profiles, profiles.modelContentId,
                    modelAsset => profiles.modelFile = modelAsset);
            }

            if (profiles.classLabelsAsset == null && profiles.classLabelsContentId != 0)
            {
                yield return FixMissingContent<TextAsset>(profiles, profiles.classLabelsContentId,
                    textAsset => profiles.classLabelsAsset = textAsset);
            }

            // On-device SLM tokenizer files
            if (profiles.llmConfig != null)
            {
                if (profiles.llmConfig.vocabFile == null && profiles.llmConfig.vocabFileContentId != 0)
                {
                    yield return FixMissingContent<TextAsset>(profiles, profiles.llmConfig.vocabFileContentId,
                        textAsset => profiles.llmConfig.vocabFile = textAsset);
                }

                if (profiles.llmConfig.mergesFile == null && profiles.llmConfig.mergesFileContentId != 0)
                {
                    yield return FixMissingContent<TextAsset>(profiles, profiles.llmConfig.mergesFileContentId,
                        textAsset => profiles.llmConfig.mergesFile = textAsset);
                }

                if (profiles.llmConfig.tokenizerConfigFile == null && profiles.llmConfig.tokenizerConfigFileContentId != 0)
                {
                    yield return FixMissingContent<TextAsset>(profiles, profiles.llmConfig.tokenizerConfigFileContentId,
                        textAsset => profiles.llmConfig.tokenizerConfigFile = textAsset);
                }
            }
        }

        private static async Task FixBlocksWithMissingContent()
        {
            await Task.WhenAll(
                GetProviderProfilesWithMissingRequiredContent()
                    .SelectMany(GetFixTasksForProviderProfiles)
            );
        }
#endif

        private static async Task FixMissingContent<T>(Object providerProfile, ulong contentId,
            Action<T> assignModelFile) where T : Object
        {
            if (contentId == 0)
            {
                return;
            }

            try
            {
                using var progressDisplayer = new UnityScopedProgressDisplayer("[Meta XR] Fetching remote content");
                var downloader = new RemoteBinaryContentDownloader(contentId)
                    .WithProgressDisplay(progressDisplayer)
                    .WithoutCache();
                var result = await downloader.Fetch();

                if (!result.IsSuccess)
                {
                    IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "ai-content-download-failed", $"Failed to download content with ID: {contentId}");
                    return;
                }

                var filePath = Path.Combine(MetaXRDirectory, result.FileName);
                Directory.CreateDirectory(MetaXRDirectory);
                await File.WriteAllBytesAsync(filePath, result.Content);
                AssetDatabase.Refresh();

                var modelAsset = AssetDatabase.LoadAssetAtPath<T>(filePath);
                if (modelAsset != null)
                {
                    assignModelFile(modelAsset);
                    EditorUtility.SetDirty(providerProfile);
                }
                else
                {
                    IssueTracker.TrackError(IssueTracker.SDK.BuildingBlocks, "ai-content-asset-load-failed", $"Failed to load asset at path: {filePath}");
                }
            }
            catch (Exception ex)
            {
                IssueTracker.TrackError(IssueTracker.SDK.BuildingBlocks, "ai-content-fix-error", $"Error fixing missing content for ID {contentId}: {ex.Message}");
            }
        }

        private static IEnumerable<AIProviderBase> GetProviderProfiles()
        {
            var objectDetectionProfiles = XR.BuildingBlocks.Editor.Utils
                .GetBlocksWithType<ObjectDetectionAgent>()
                .Select(agent => agent.providerAsset);

            var llmProfiles = XR.BuildingBlocks.Editor.Utils
                .GetBlocksWithType<LlmAgent>()
                .Select(agent => agent.providerAsset);

            var sttProfiles = XR.BuildingBlocks.Editor.Utils
                .GetBlocksWithType<SpeechToTextAgent>()
                .Select(agent => agent.providerAsset);

            var ttsProfiles = XR.BuildingBlocks.Editor.Utils
                .GetBlocksWithType<TextToSpeechAgent>()
                .Select(agent => agent.providerAsset);

            return objectDetectionProfiles
                .Concat(llmProfiles)
                .Concat(sttProfiles)
                .Concat(ttsProfiles);
        }

#if UNITY_INFERENCE_INSTALLED
        private static IEnumerable<UnityInferenceEngineProvider> GetProviderProfilesWithMissingRequiredContent()
        {
            return GetProviderProfiles()
                .OfType<UnityInferenceEngineProvider>()
                .Where(profile => (profile.modelFile == null && profile.modelContentId != 0)
                                  || (profile.classLabelsAsset == null && profile.classLabelsContentId != 0)
                                  || (profile.llmConfig != null && profile.llmConfig.vocabFile == null && profile.llmConfig.vocabFileContentId != 0)
                                  || (profile.llmConfig != null && profile.llmConfig.mergesFile == null && profile.llmConfig.mergesFileContentId != 0)
                                  || (profile.llmConfig != null && profile.llmConfig.tokenizerConfigFile == null && profile.llmConfig.tokenizerConfigFileContentId != 0));
        }
#endif

        private static void FillProviderProfileData(AIProviderBase providerProfile,
            RemoteProviderProfileRegistry.ProviderProfileData data)
        {
            switch (providerProfile)
            {
                case ElevenLabsProvider elevenLabsProvider:
                    elevenLabsProvider.endpoint = data.endpoint;
                    elevenLabsProvider.model = data.model;
                    elevenLabsProvider.sttLanguage = data.language;
                    elevenLabsProvider.sttIncludeAudioEvents = data.includeAudioEvents;
                    elevenLabsProvider.voiceId = data.voice;
                    break;
                case HuggingFaceProvider huggingFaceProvider:
                    huggingFaceProvider.endpoint = data.endpoint;
                    huggingFaceProvider.modelId = data.model;
                    huggingFaceProvider.supportsVision = data.supportsVision;
                    huggingFaceProvider.inlineRemoteImages = data.inlineRemoteImages;
                    huggingFaceProvider.resolveRemoteRedirects = data.resolveRedirects;
                    huggingFaceProvider.maxInlineBytes = data.maxInlineBytes;
                    break;
                case LlamaApiProvider llamaApiProvider:
                    llamaApiProvider.endpointUrl = data.endpoint;
                    llamaApiProvider.model = data.model;
                    llamaApiProvider.supportsVision = data.supportsVision;
                    llamaApiProvider.inlineRemoteImages = data.inlineRemoteImages;
                    llamaApiProvider.resolveRemoteRedirects = data.resolveRedirects;
                    llamaApiProvider.maxInlineBytes = data.maxInlineBytes;
                    llamaApiProvider.temperature = data.temperature;
                    llamaApiProvider.topP = data.topP;
                    llamaApiProvider.repetitionPenalty = data.repetitionPenalty;
                    llamaApiProvider.maxCompletionTokens = data.maxCompletionTokens;
                    break;
                case OllamaProvider ollamaProvider:
                    ollamaProvider.host = data.endpoint;
                    ollamaProvider.model = data.model;
                    break;
                case OpenAIProvider openAIProvider:
                    openAIProvider.apiRoot = data.endpoint;
                    openAIProvider.model = data.model;
                    openAIProvider.supportsVision = data.supportsVision;
                    openAIProvider.inlineRemoteImages = data.inlineRemoteImages;
                    openAIProvider.resolveRemoteRedirects = data.resolveRedirects;
                    openAIProvider.maxInlineBytes = data.maxInlineBytes;
                    openAIProvider.sttLanguage = data.language;
                    openAIProvider.sttResponseFormat = data.sttFormat;
                    openAIProvider.sttTemperature = data.temperature;
                    openAIProvider.ttsVoice = data.voice;
                    openAIProvider.ttsOutputFormat = data.ttsFormat;
                    openAIProvider.ttsSpeed = data.speed;
                    openAIProvider.ttsInstructions = data.instructions;
                    break;
                case ReplicateProvider replicateProvider:
                    replicateProvider.modelId = data.model;
                    replicateProvider.supportsVision = data.supportsVision;
                    replicateProvider.maxInlineBytes = data.maxInlineBytes;
                    break;
#if UNITY_INFERENCE_INSTALLED
                case UnityInferenceEngineProvider unityInferenceEngineProvider:
                    unityInferenceEngineProvider.layersPerFrame = data.layersPerFrame;
                    unityInferenceEngineProvider.modelContentId = data.modelContentId;
                    unityInferenceEngineProvider.classLabelsContentId = data.classLabelsContentId;
                    unityInferenceEngineProvider.splitOverFrames = data.splitOverFrames;

                    if (Enum.TryParse(data.backendType, out BackendType backendType))
                    {
                        unityInferenceEngineProvider.backend = backendType;
                    }

                    // Set UnityInferenceEngine mode
                    if (!string.IsNullOrEmpty(data.unityInferenceMode) &&
                        Enum.TryParse(data.unityInferenceMode, out UnityInferenceProviderMode mode))
                    {
                        unityInferenceEngineProvider.mode = mode;
                    }

                    // Initialize and populate LLM tokenizer content IDs for Chat mode
                    if (unityInferenceEngineProvider.mode == UnityInferenceProviderMode.Chat)
                    {
                        // Initialize llmConfig if it doesn't exist
                        if (unityInferenceEngineProvider.llmConfig == null)
                        {
                            unityInferenceEngineProvider.llmConfig = new OnDeviceLlmConfig();
                        }

                        // Populate tokenizer content IDs
                        unityInferenceEngineProvider.llmConfig.vocabFileContentId = data.vocabFileContentId;
                        unityInferenceEngineProvider.llmConfig.mergesFileContentId = data.mergesFileContentId;
                        unityInferenceEngineProvider.llmConfig.tokenizerConfigFileContentId = data.tokenizerConfigFileContentId;
                    }

                    break;
#endif // UNITY_INFERENCE_INSTALLED
                default:
                    throw new ArgumentException($"Unknown provider type {providerProfile.GetType()}");
            }

            EditorUtility.SetDirty(providerProfile);
        }
    }
}
