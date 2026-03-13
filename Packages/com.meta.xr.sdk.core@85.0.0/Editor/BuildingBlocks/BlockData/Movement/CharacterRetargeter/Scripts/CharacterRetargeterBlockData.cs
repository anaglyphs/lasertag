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
using UnityEditor;
using UnityEngine;

#if USING_META_XR_MOVEMENT_SDK
using Meta.XR.Movement.Editor;
#endif // USING_META_XR_MOVEMENT_SDK

namespace Meta.XR.BuildingBlocks.Editor
{
    /// <summary>
    /// The character retargeter building block.
    /// </summary>
    public class CharacterRetargeterBlockData : Meta.XR.BuildingBlocks.Editor.BlockData
    {
        internal override bool CanBeAddedOverGameObject => true;
        protected override bool UsesPrefab => false;

        protected override List<GameObject> InstallRoutine(GameObject selectedGameObject)
        {
#if USING_META_XR_MOVEMENT_SDK
            if (selectedGameObject == null)
            {
                selectedGameObject = Selection.activeGameObject;
            }
            MSDKUtilityEditor.AddCharacterRetargeter(selectedGameObject);
            return new List<GameObject> { selectedGameObject };
#else
            throw new InstallationCancelledException("It's required to install the Movement SDK package to use this component");
#endif // USING_META_XR_MOVEMENT_SDK
        }
    }
}
