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

using UnityEngine;
using Fusion;
using Meta.XR.MultiplayerBlocks.Shared;

namespace Meta.XR.MultiplayerBlocks.Fusion
{
    public class PlayerNameTagSpawnerFusion : MonoBehaviour, INameTagSpawner
    {
        [SerializeField] private GameObject playerNameTagPrefab;

        private NetworkRunner _networkRunner;
        private bool _sceneLoaded = false;

        private void OnEnable()
        {
            FusionBBEvents.OnSceneLoadDone += OnLoaded;
        }

        private void OnDisable()
        {
            FusionBBEvents.OnSceneLoadDone -= OnLoaded;
        }

        private void OnLoaded(NetworkRunner networkRunner)
        {
            _sceneLoaded = true;
            _networkRunner = networkRunner;
        }

        #region INameTagSpawner

        public bool IsConnected => _networkRunner != null && _sceneLoaded;

        public void Spawn(string playerName)
        {
            var spawnedObject = _networkRunner.Spawn(
                playerNameTagPrefab,
                Vector3.zero,
                Quaternion.identity,
                _networkRunner.LocalPlayer);

            var nameTag = spawnedObject.GetComponent<PlayerNameTagFusion>();
            nameTag.OculusName = playerName;

        }

        #endregion
    }
}
