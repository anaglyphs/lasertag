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
using Meta.XR.BuildingBlocks.Editor;
using UnityEditor;
using UnityEngine;

#if USING_META_XR_MOVEMENT_SDK
using Meta.XR.Movement.Networking.Editor;
using Meta.XR.Movement.Networking.Fusion;
#endif // USING_META_XR_MOVEMENT_SDK

namespace Meta.XR.MultiplayerBlocks.Fusion.Editor
{
    /// <summary>
    /// Network installation routine for using Photon Fusion 2.
    /// </summary>
    public class NetworkedCharacterFusionInstallationRoutine :
        Meta.XR.MultiplayerBlocks.Shared.Editor.NetworkInstallationRoutine
    {
        /// <summary>
        /// Installs the block using the selected game object.
        /// </summary>
        /// <param name="block">The block to be installed</param>
        /// <param name="selectedGameObject">The selected game object</param>
        /// <returns>The installed game object instances.</returns>
        /// <exception cref="OVRConfigurationTaskException">Error with executing this block.</exception>
        public override List<GameObject> Install(BlockData block, GameObject selectedGameObject)
        {
#if USING_META_XR_MOVEMENT_SDK
            var characterPrefab = NetworkCharacterSpawnerEditor.CreateCharacterPrefabFromModel();
            if (characterPrefab == null)
            {
                throw new OVRConfigurationTaskException("Must have a configured retargeted character for networking!");
            }

            var installation = base.Install(block, selectedGameObject);

            // Update prefab reference.
            var instance = installation[0].GetComponent<NetworkCharacterSpawnerFusion>();
            Undo.RecordObject(instance, "Update " + instance.name);
            instance.CharacterRetargeterPrefabs = new[] { characterPrefab };
            EditorUtility.SetDirty(instance);

            return installation;
#else
            throw new InstallationCancelledException("It's required to install the Movement SDK package to use this component");
#endif // USING_META_XR_MOVEMENT_SDK
        }
    }
}
