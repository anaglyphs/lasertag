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
using UnityEngine;

namespace Meta.XR.Editor.BuildingBlocks.AIBlocks
{
    internal class AIBlocksInstallationRoutine : InstallationRoutine
    {
        [SerializeField]
        [Variant(
            Behavior = VariantAttribute.VariantBehavior.Parameter,
            Description = "Select where the ML model is ran (on device, cloud, etc).",
            OptionsMethod = nameof(GetAvailableInferenceTypes),
            Default = InferenceType.OnDevice,
            Order = 0
        )]
        public InferenceType inferenceType;

        [SerializeField]
        [Variant(
            Behavior = VariantAttribute.VariantBehavior.Parameter,
            OptionsMethod = nameof(GetAvailableProviders),
            Description = "Select model provider to use for inference.",
            Order = 1
        )]
        public string modelProvider;

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

        private IEnumerable<InferenceType> GetAvailableInferenceTypes() =>
            RemoteProviderProfileRegistry.AvailableInferenceTypesForBlockId(TargetBlockDataId);

        protected static void RemoveComponent<T>(GameObject @object) where T : Component
        {
            var component = @object.GetComponent<T>();
            if (component != null)
            {
                DestroyImmediate(component);
            }
        }
    }
}
