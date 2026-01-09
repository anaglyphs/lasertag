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
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// A <see cref="GroupedItem"/> UI item with border and interactable properties.
    /// </summary>
    internal class Card : GroupedItem
    {
        public string Id { get; }
        public Color BorderColor { get; set; } = Styles.Colors.UnselectedWhite;
        public Color BorderHoverColor { get; set; } = Styles.Colors.OffWhite;
        public Color BorderSelectedColor { get; set; } = Styles.Colors.Meta;
        public Color ContentColor { get; set; } = Styles.Colors.LighterWhite;
        public Color ContentSelectedColor { get; set; } = Styles.Colors.OffWhite;
        public Color ContentHoverColor { get; set; } = Styles.Colors.SelectedWhite;
        public Color BackgroundColor { get; set; } = Styles.Colors.Grey40;
        public Color BackgroundHoverColor { get; set; } = Styles.Colors.Grey44;
        public float BorderWidth { get; set; } = 1.0f;
        public bool IsHovering { get; private set; }

        private bool Selected { get; set; }
        public Action<string> OnSelect { get; set; }
        public Func<string, bool> Disabled { get; set; }

        private readonly bool _interactable;

        public Card(string cardId, bool interactable = true,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Horizontal,
            params GUILayoutOption[] options)
            : base(Enumerable.Empty<IUserInterfaceItem>(), placementType, options)
        {
            _interactable = interactable;
            Id = cardId;
            Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardMainGroup;
        }

        public Card(IEnumerable<IUserInterfaceItem> items, bool interactable = true,
                Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Horizontal,
                params GUILayoutOption[] options)
                : base(items, placementType, options)
        {
            _interactable = interactable;
            Id = Guid.NewGuid().ToString();
            Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardMainGroup;
        }

        public Card(IEnumerable<IUserInterfaceItem> items, string cardId, bool interactable = true,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Horizontal,
            params GUILayoutOption[] options)
            : base(items, placementType, options)
        {
            _interactable = interactable;
            Id = cardId;
            Style = Meta.XR.Editor.UserInterface.Styles.GUIStyles.CardMainGroup;
        }

        private const float DisabledOpacity = 0.7f;
        private Color OpacityColor => new Color(DisabledOpacity, DisabledOpacity, DisabledOpacity);
        private Color DisabledColorMultiplier => (Disabled?.Invoke(Id) ?? false) ? OpacityColor : Color.white;

        public override void Draw()
        {
            var rect = EditorGUILayout.BeginHorizontal(Styles.GUIStyles.RoundedBox);
            if (_interactable)
            {
                IsHovering = HoverHelper.IsHover(Id + "_hover", Event.current, rect);
            }

            DrawCardStyle(rect, BorderWidth);
            base.Draw();

            if (_interactable)
            {
                var hit = HoverHelper.Button(Id + "_select", rect, new GUIContent(), GUIStyle.none, out _);
                if (hit)
                {
                    OnSelect?.Invoke(Id);
                }

                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }

            EditorGUILayout.EndHorizontal();
        }

        public Color FetchDynamicColor(IDynamicColorItem item)
        {
            var contentColor = Selected ? ContentSelectedColor :
                IsHovering ? ContentHoverColor :
                ContentColor;
            return contentColor * DisabledColorMultiplier;
        }

        public Color FetchDynamicIconColor(IDynamicColorItem item)
        {
            var contentColor = Selected ? BorderSelectedColor :
                IsHovering ? BorderHoverColor :
                BorderColor;
            return contentColor * DisabledColorMultiplier;
        }

        public void SetSelected(bool selected) => Selected = selected;

        private void DrawCardStyle(Rect contentRect, float borderWidth)
        {
            var bottomRect = new Rect(new Vector2(contentRect.position.x - borderWidth, contentRect.position.y - borderWidth),
                new Vector2(contentRect.width + (2 * borderWidth), contentRect.height + (2 * borderWidth)));

            var borderColor = Selected ? BorderSelectedColor :
                IsHovering ? BorderHoverColor :
                BorderColor;
            borderColor *= DisabledColorMultiplier;
            GUI.DrawTexture(bottomRect, borderColor.ToTexture(), ScaleMode.ScaleAndCrop,
                false, 1f, GUI.color, Vector4.zero, Styles.Constants.RoundedBorderVectors);

            var backgroundColor = IsHovering ? BackgroundHoverColor : BackgroundColor;
            GUI.DrawTexture(contentRect, backgroundColor.ToTexture(), ScaleMode.ScaleAndCrop,
                false, 1f, GUI.color, Vector4.zero, Styles.Constants.RoundedBorderVectors);
        }
    }
}
