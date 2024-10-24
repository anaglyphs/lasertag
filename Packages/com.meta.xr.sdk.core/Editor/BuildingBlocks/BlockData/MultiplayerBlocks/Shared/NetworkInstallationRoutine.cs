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
using System.Threading.Tasks;
using Meta.XR.BuildingBlocks.Editor;
using UnityEngine;

namespace Meta.XR.MultiplayerBlocks.Shared.Editor
{
    public class NetworkInstallationRoutine : InstallationRoutine
    {
        internal enum NetworkImplementation
        {
            UnityNetcodeForGameObjects,
            PhotonFusion
        }

        [SerializeField]
        [Variant(Behavior = VariantAttribute.VariantBehavior.Definition,
            Description = "The underlying network Implementation that will be used for all network Blocks.",
            Default = NetworkImplementation.UnityNetcodeForGameObjects)]
        internal NetworkImplementation implementation;

        [SerializeField]
        [Variant(Behavior = VariantAttribute.VariantBehavior.Parameter,
            Description = "Indicates whether Auto Matchmaking should also be included (recommended for prototyping).",
            Condition = nameof(CanInstallAutoMatchmaking),
            Default = true)]
        internal bool installAutoMatchmaking;

        protected bool CanInstallAutoMatchmaking()
            => TargetBlockDataId != BlockDataIds.IAutoMatchmaking
               && TargetBlockDataId != BlockDataIds.INetworkManager
               && !IsAutoMatchmakingPresentInScene;

        private static bool IsAutoMatchmakingPresentInScene => Utils.GetBlock(BlockDataIds.IAutoMatchmaking);

        private bool ShouldInstallAutoMatchmaking => installAutoMatchmaking && CanInstallAutoMatchmaking();

        internal override IEnumerable<string> ComputePackageDependencies(VariantsSelection variantSelection)
        {
            if (ShouldInstallAutoMatchmaking)
            {
                var autoMatchmakingBlock = Utils.GetBlockData(BlockDataIds.IAutoMatchmaking);
                var additionalDependencies = InterfaceBlockData.ComputePackageDependencies(autoMatchmakingBlock as InterfaceBlockData, variantSelection);
                return base.ComputePackageDependencies(variantSelection).Concat(additionalDependencies);
            }

            return base.ComputePackageDependencies(variantSelection);
        }

        public override async Task<List<GameObject>> InstallAsync(BlockData blockData, GameObject selectedGameObject)
        {
            // Installing Auto Matchmaking for all networking blocks use cases if not present
            // As an optional dependency, developers can easily remove and use their own matchmaking.
            if (ShouldInstallAutoMatchmaking)
            {
                var autoMatchmakingBlockData = Utils.GetBlockData(BlockDataIds.IAutoMatchmaking);
                await autoMatchmakingBlockData.InstallWithDependencies();
            }

            return await base.InstallAsync(blockData, selectedGameObject);
        }
    }
}
