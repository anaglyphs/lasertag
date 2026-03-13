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
using System.Linq;
using Meta.XR.BuildingBlocks.Editor;
using Meta.XR.BuildingBlocks.AIBlocks;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.BuildingBlocks.AIBlocks
{
    internal class AIBlocksInstallationRoutine : InstallationRoutine
    {
        internal const string CreateNewProviderOption = "Create a new provider asset";
        private static readonly Dictionary<string, List<string>> ProviderOptionsCache = new();

        [SerializeField]
        [Variant(
            Behavior = VariantAttribute.VariantBehavior.Parameter,
            OptionsMethod = nameof(GetAvailableProviderAssets),
            Description =
                "Choose an existing provider asset or import a default one. Provider assets that come from a provider " +
                "supporting the task at hand are listed here as 'existing'. This does not mean that every " +
                "asset listed here is suitable for this block.",
            Default = CreateNewProviderOption,
            Condition = nameof(HasExistingProviders),
            Order = 0
        )]
        public string providerAssetSelection;

        [SerializeField]
        [Variant(
            Behavior = VariantAttribute.VariantBehavior.Parameter,
            Description = "Select where the ML model is ran (on device, cloud, etc).",
            OptionsMethod = nameof(GetAvailableInferenceTypes),
            Default = InferenceType.OnDevice,
            DisableCondition = nameof(ShouldEnableModelProvider),
            Order = 1
        )]

        public InferenceType inferenceType;
        [SerializeField]
        [Variant(
            Behavior = VariantAttribute.VariantBehavior.Parameter,
            OptionsMethod = nameof(GetAvailableProviders),
            Description = "Select a model provider to use for inference.",
            DisableCondition = nameof(ShouldEnableModelProvider),
            Order = 2
        )]
        public string modelProvider;

        static AIBlocksInstallationRoutine()
        {
            EditorApplication.projectChanged += () => ProviderOptionsCache.Clear();
        }

        private IEnumerable<string> GetAvailableProviders()
        {
            var possibleInferenceTypes = GetAvailableInferenceTypes().ToArray();

            if (possibleInferenceTypes.All(t => t != inferenceType))
            {
                inferenceType = possibleInferenceTypes.First();
            }

            return RemoteProviderProfileRegistry.AvailableProviderProfilesFor(TargetBlockDataId, inferenceType)
                .Select(provider => provider.GetDisplayName());
        }

        private IEnumerable<InferenceType> GetAvailableInferenceTypes()
        {
            return RemoteProviderProfileRegistry.AvailableInferenceTypesForBlockId(TargetBlockDataId);
        }

        public bool ShouldEnableModelProvider()
        {
            return string.IsNullOrEmpty(providerAssetSelection) || providerAssetSelection == CreateNewProviderOption;
        }

        public bool HasExistingProviders()
        {
            if (string.IsNullOrEmpty(TargetBlockDataId))
            {
                return false;
            }

            var options = GetAvailableProviderAssets() as List<string> ?? GetAvailableProviderAssets().ToList();

            if (!string.IsNullOrEmpty(providerAssetSelection) && !options.Contains(providerAssetSelection))
            {
                providerAssetSelection = CreateNewProviderOption;
            }

            return options.Count > 1;
        }

        private IEnumerable<string> GetAvailableProviderAssets()
        {
            if (ProviderOptionsCache.TryGetValue(TargetBlockDataId, out var cachedOptions))
            {
                return cachedOptions;
            }

            var existingProviders = AIBlocksSetupRules.FindExistingProviderAssets(TargetBlockDataId);
            var options = new List<string> { CreateNewProviderOption };

            foreach (var provider in existingProviders)
            {
                options.Add(provider.name);
            }

            ProviderOptionsCache[TargetBlockDataId] = options;

            return options;
        }

        protected static void RemoveComponent<T>(GameObject @object) where T : Component
        {
            var component = @object.GetComponent<T>();
            if (component)
            {
                DestroyImmediate(component);
            }
        }
    }
}
