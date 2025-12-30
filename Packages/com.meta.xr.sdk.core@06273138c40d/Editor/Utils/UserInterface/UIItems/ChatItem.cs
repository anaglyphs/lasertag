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

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    internal class ChatItem : GroupedItem
    {
        private readonly bool _showCopy;
        private Action<string> _onCopyButtonPressed;
        public string Id { get; }

        public ChatItem(string id, List<IUserInterfaceItem> items, bool showCopy = false, Action<string> onCopyButtonPressed = null,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Horizontal,
            params GUILayoutOption[] options) : base(items, placementType, options)
        {
            Id = id;
            _showCopy = showCopy;
            _onCopyButtonPressed = onCopyButtonPressed;
        }

        public override void Draw()
        {
            base.Draw();

            if (!_showCopy)
                return;

            var rect = GUILayoutUtility.GetLastRect();
            var hover = HoverHelper.IsHover(Id, Event.current, rect);
            if (!hover) return;

            using var iconColor = new Utils.ColorScope(Utils.ColorScope.Scope.Content, Styles.Colors.LightGray);
            var iconSize = UIStyles.GUIStyles.IconStyle.fixedWidth;
            var iconRect = new Rect(
                new Vector2(rect.width - iconSize - Styles.Constants.Padding, rect.y + Styles.Constants.Padding),
                new Vector2(iconSize, iconSize));
            var hit = HoverHelper.Button(Id, iconRect, UIStyles.Contents.CopyIcon, UIStyles.GUIStyles.IconStyle,
                out _);
            EditorGUIUtility.AddCursorRect(iconRect, MouseCursor.Link);
            if (hit)
            {
                _onCopyButtonPressed?.Invoke(Id);
            }
        }
    }
}
