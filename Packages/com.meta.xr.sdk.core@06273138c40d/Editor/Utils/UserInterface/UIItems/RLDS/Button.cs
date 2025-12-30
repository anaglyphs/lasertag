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

using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface.RLDS
{
    internal class Button : IUserInterfaceItem
    {
        public bool Hide { get; set; }
        public bool Disable { get; set; }
        public bool Invisible { get; set; }

        private readonly GUIStyle _containerStyle;
        private readonly ButtonStyle _buttonStyle;
        private readonly GUIStyle _textStyle;
        private readonly ActionLinkDescription _action;

        public Button(ActionLinkDescription action, ButtonStyle buttonStyle, int fixedWidth = 0)
        {
            _action = action;
            _buttonStyle = buttonStyle;
            var style = _buttonStyle.ToGUIStyle();
            _containerStyle = new GUIStyle
            {
                fixedHeight = _buttonStyle.Height,
                stretchHeight = false,
                stretchWidth = true,
                padding = new RectOffset(0, 0, _buttonStyle.Height / 2, _buttonStyle.Height / 2),
                margin = style.margin
            };
            if (fixedWidth != 0)
            {
                _containerStyle.fixedWidth = fixedWidth;
                _containerStyle.stretchWidth = false;
                _containerStyle.padding.left = fixedWidth / 2;
                _containerStyle.padding.right = fixedWidth / 2;
            }

            _textStyle = _buttonStyle.TextStyle;
            _textStyle.alignment = style.alignment;
            var clearTexture = Color.clear.ToTexture();
            _textStyle.normal = new()
            {
                background = clearTexture,
                textColor = _buttonStyle.TextColorNormal
            };
            _textStyle.hover = new()
            {
                background = clearTexture,
                textColor = _buttonStyle.TextColorNormal
            };
            _textStyle.active.textColor = _buttonStyle.TextColorHover;
            _textStyle.padding = new RectOffset(RLDS.Styles.Spacing.Space3XS, RLDS.Styles.Spacing.Space3XS,
                RLDS.Styles.Spacing.Space3XS, RLDS.Styles.Spacing.Space3XS);
            ;
        }

        public void Draw()
        {
            EditorGUI.BeginDisabledGroup(Disable);
            var rect = EditorGUILayout.BeginVertical(_containerStyle);
            var hover = HoverHelper.IsHover(GetType() + "RLDS_BUTTON", Event.current, rect);

            if (_buttonStyle.BorderWidth > 0)
            {
                DrawBorder(rect, _buttonStyle.BorderWidth, _buttonStyle.CornerRadius, _buttonStyle.BorderColor);
            }

            var expectedColor = hover ? _buttonStyle.BackgroundColorHover : _buttonStyle.BackgroundColorNormal;
            GUI.DrawTexture(rect, expectedColor.ToTexture(), ScaleMode.ScaleAndCrop, false, 1, GUI.color,
                Vector4.zero, _buttonStyle.CornerRadius);
            if (GUI.Button(rect, _action.Content, _textStyle))
            {
                _action.Action?.Invoke();
            }

            EditorGUILayout.EndVertical();
            EditorGUI.EndDisabledGroup();
        }

        private void DrawBorder(Rect contentRect, int borderWidth, int cornerRadius, Color borderColor)
        {
            var bottomRect = new Rect(
                new Vector2(contentRect.position.x - borderWidth, contentRect.position.y - borderWidth),
                new Vector2(contentRect.width + (2 * borderWidth), contentRect.height + (2 * borderWidth)));
            GUI.DrawTexture(bottomRect, borderColor.ToTexture(), ScaleMode.ScaleAndCrop,
                false, 1f, GUI.color, borderWidth, cornerRadius);
        }
    }
}
