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
using UnityEditor;

namespace Meta.XR.BuildingBlocks.Editor
{
    [InitializeOnLoad]
    public class CustomHierarchyIcon
    {
        private const string HierarchyIconPath =
            "Assets/Oculus/VR/Editor/BuildingBlocks/Icons/ovr_bb_icon.png";

        private static Texture2D _hierarchyIcon;
        private static Texture2D HierarchyIcon
        {
            get
            {
                if (_hierarchyIcon == null)
                {
                    _hierarchyIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(HierarchyIconPath);
                }

                return _hierarchyIcon;
            }
        }

        static CustomHierarchyIcon()
        {
#if UNITY_6000_5_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI -= HierarchyItemOnGuiEntityID;
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += HierarchyItemOnGuiEntityID;
#else
            EditorApplication.hierarchyWindowItemOnGUI -= HierarchyItemOnGUIInstanceID;
            EditorApplication.hierarchyWindowItemOnGUI += HierarchyItemOnGUIInstanceID;
#endif
        }

#if UNITY_6000_5_OR_NEWER
        private static void HierarchyItemOnGuiEntityID(EntityId entityId, Rect selectionRect)
        {
            var gameObject = EditorUtility.EntityIdToObject(entityId) as GameObject;
            HierarchyItemOnGUI(gameObject, selectionRect);
        }
#else
        private static void HierarchyItemOnGUIInstanceID(int instanceID, Rect selectionRect)
        {
#if UNITY_6000_3_OR_NEWER
            var gameObject = EditorUtility.EntityIdToObject(instanceID) as GameObject;
#else
            var gameObject = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#endif
            HierarchyItemOnGUI(gameObject, selectionRect);
        }
#endif

        private static void HierarchyItemOnGUI(GameObject gameObject, Rect selectionRect)
        {
            if (gameObject == null)
                return;

            if (gameObject.GetComponent<BuildingBlock>() == null)
                return;

            if (HierarchyIcon == null)
                return;

            Rect iconRect = new Rect(selectionRect.x + selectionRect.width - 20, selectionRect.y, 16, 16);
            GUI.DrawTexture(iconRect, HierarchyIcon);
        }
    }
}
