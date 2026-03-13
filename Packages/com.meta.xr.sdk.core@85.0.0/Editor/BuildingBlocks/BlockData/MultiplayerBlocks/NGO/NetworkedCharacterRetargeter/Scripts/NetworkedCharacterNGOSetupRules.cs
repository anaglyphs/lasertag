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

#if UNITY_NGO_MODULE_DEFINED && USING_META_XR_MOVEMENT_SDK
using System.Linq;
using Meta.XR.BuildingBlocks;
using Meta.XR.BuildingBlocks.Editor;
using UnityEditor;
using Meta.XR.Movement.Networking.NGO;

namespace Meta.XR.MultiplayerBlocks.NGO.Editor
{
    [InitializeOnLoad]
    internal static class NetworkedCharacterNGOSetupRules
    {
        static NetworkedCharacterNGOSetupRules()
        {
            OVRProjectSetup.AddTask(
                level: OVRProjectSetup.TaskLevel.Required,
                group: OVRProjectSetup.TaskGroup.Features,
                isDone: _ =>
                {
                    var networkedCharacterBb = GetNetworkedCharacterRetargeterBB();
                    if (networkedCharacterBb == null)
                    {
                        return true;
                    }

                    var spawnerPrefab = networkedCharacterBb.GetComponent<NetworkCharacterSpawnerNGO>().gameObject;
                    var characterPrefab =
                        networkedCharacterBb.GetComponent<NetworkCharacterSpawnerNGO>().NetworkedCharacterHandler;
                    return BuildingBlockNGOUtils.IsPrefabContainedInNetworkManagerBB(spawnerPrefab)
                           && BuildingBlockNGOUtils.IsPrefabContainedInNetworkManagerBB(characterPrefab);
                },
                fix: _ =>
                {
                    var networkedCharacterBb = GetNetworkedCharacterRetargeterBB();
                    BuildingBlockNGOUtils.AddPrefabsToNetworkManagerBB(
                        networkedCharacterBb.GetComponent<NetworkCharacterSpawnerNGO>().gameObject,
                        networkedCharacterBb.GetComponent<NetworkCharacterSpawnerNGO>().NetworkedCharacterHandler);
                },
                fixMessage: "Add the Networked Retargeted Character prefab to the network prefabs list",
                message:
                "When using the Networked Retargeted Character NGO Block, you must add its prefabs to the network prefabs list"
            );
        }

        public static void Fix()
        {
            var networkedCharacterBb = GetNetworkedCharacterRetargeterBB();
            if (networkedCharacterBb == null)
            {
                return;
            }

            var spawnerPrefab = networkedCharacterBb.gameObject;
            var characterPrefab = networkedCharacterBb.GetComponent<NetworkCharacterSpawnerNGO>()
                .NetworkedCharacterHandler;
            BuildingBlockNGOUtils.AddPrefabsToNetworkManagerBB(spawnerPrefab, characterPrefab);
        }

        private static BuildingBlock GetNetworkedCharacterRetargeterBB() =>
            Utils.GetInterfaceBlocksInScene(BlockDataIds.INetworkedCharacterRetargeter,
                    BlockDataIds.NetworkedCharacterRetargeterNGOInstallationRoutine)
                .FirstOrDefault();
    }
}
#endif // UNITY_NGO_MODULE_DEFINED && USING_META_XR_MOVEMENT_SDK
