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


#if FUSION2 || FUSION_2_1
#define FUSION_COMPATIBLE_VERSION
#endif

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.BuildingBlocks.Editor;
using Meta.XR.MultiplayerBlocks.Shared.Editor;
using Meta.XR.Telemetry;
using UnityEngine;
#if FUSION_WEAVER && FUSION_COMPATIBLE_VERSION
using Fusion;
#endif // FUSION_WEAVER && FUSION2

namespace Meta.XR.MultiplayerBlocks.Fusion.Editor
{
    public class AutoMatchmakingFusionInstallationRoutine : NetworkInstallationRoutine
    {
        internal override IReadOnlyCollection<InstallationStepInfo> GetInstallationSteps(VariantsSelection selection)
        {
            var installationSteps = new List<InstallationStepInfo>();
            installationSteps.AddRange(base.GetInstallationSteps(selection));
            installationSteps.Add(new(Utils.GetBlockData(BlockDataIds.INetworkManager), "Finds the {0} block in active scene."));
            installationSteps.Add(new(null, "Sets the reference of network runner component to <b>FusionBootstrap</b>."));
            return installationSteps;
        }

#pragma warning disable CS1998
        public override async Task<List<GameObject>> InstallAsync(BlockData blockData, GameObject selectedGameObject)
#pragma warning restore CS1998
        {

#if FUSION_WEAVER && FUSION_COMPATIBLE_VERSION
            var autoMatchmakingGOsList = await base.InstallAsync(blockData, selectedGameObject);
            var networkRunnerComps = Utils.GetBlocksWithType<NetworkRunner>();
            if (networkRunnerComps.Count == 0 || autoMatchmakingGOsList.Count == 0)
            {
                IssueTracker.TrackWarning(IssueTracker.SDK.BuildingBlocks, "auto-matchmaking-fusion-install-failed",
                    "Network Runner Building Block or Auto Matchmaking Object could not be found.");
                return new List<GameObject>();
            }
            autoMatchmakingGOsList[0].GetComponent<FusionBootstrap>().RunnerPrefab = networkRunnerComps[0];

            return new List<GameObject>{ autoMatchmakingGOsList[0] };
#else
            throw new InvalidOperationException("It's required to install Photon Fusion to use this component");
#endif // FUSION_WEAVER && FUSION_COMPATIBLE_VERSION
        }
    }
}
