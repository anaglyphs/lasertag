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

namespace Meta.XR.Editor.UserInterface
{
    internal class ScrollView : GroupedItem
    {
        private Vector3 _scrollPosition;
        public GUIStyle BoxStyle { get; set; } = Styles.GUIStyles.ScrollViewBox;
        public GUIStyle ScrollViewStyle { get; set; } = Styles.GUIStyles.ScrollViewGroup;

        public ScrollView(IEnumerable<IUserInterfaceItem> items,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Horizontal,
            params GUILayoutOption[] options) :
            this(items, new GUIStyle(), placementType, options)
        {
        }

        public ScrollView(IEnumerable<IUserInterfaceItem> items, GUIStyle style,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Horizontal,
            params GUILayoutOption[] options) : base(items, style, placementType, options)
        {
        }

        public override void Draw()
        {
            var rect = EditorGUILayout.BeginHorizontal(BoxStyle);
            DrawCardStyle(rect, 1);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition,
                false, false,
                GUI.skin.horizontalScrollbar,
                GUI.skin.verticalScrollbar,
                ScrollViewStyle);
            base.Draw();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndHorizontal();
        }


        private void DrawCardStyle(Rect contentRect, int borderWidth)
        {
            var bottomRect = new Rect(new Vector2(contentRect.position.x - borderWidth, contentRect.position.y - borderWidth),
                new Vector2(contentRect.width + (2 * borderWidth), contentRect.height + (2 * borderWidth)));

            var borderColor = Meta.XR.Editor.UserInterface.Styles.Colors.DarkBorder;
            GUI.DrawTexture(bottomRect, borderColor.ToTexture(), ScaleMode.ScaleAndCrop,
                false, 1f, GUI.color, Vector4.zero, Styles.Constants.RoundedBorderVectors);

            var backgroundColor = Meta.XR.Editor.UserInterface.Styles.Colors.Grey40;
            GUI.DrawTexture(contentRect, backgroundColor.ToTexture(), ScaleMode.ScaleAndCrop,
                false, 1f, GUI.color, Vector4.zero, Styles.Constants.RoundedBorderVectors);
        }
    }
}
