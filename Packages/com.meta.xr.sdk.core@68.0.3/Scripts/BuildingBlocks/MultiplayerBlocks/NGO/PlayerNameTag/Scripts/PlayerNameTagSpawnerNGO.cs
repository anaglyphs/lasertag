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

using Meta.XR.MultiplayerBlocks.Shared;
using Unity.Netcode;
using UnityEngine;

namespace Meta.XR.MultiplayerBlocks.NGO
{
    public class PlayerNameTagSpawnerNGO : NetworkBehaviour, INameTagSpawner
    {
        [SerializeField] internal GameObject playerNameTagPrefab;

        public bool IsConnected => IsSpawned;

        public void Spawn(string playerName)
        {
            SpawnServerRpc(playerName);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SpawnServerRpc(string playerName, ServerRpcParams serverRpcParams = default)
        {
            var go = Instantiate(playerNameTagPrefab);
            go.GetComponent<NetworkObject>().SpawnWithOwnership(serverRpcParams.Receive.SenderClientId);
            go.GetComponent<PlayerNameTagNGO>().PlayerName.Value = playerName;
        }
    }
}
