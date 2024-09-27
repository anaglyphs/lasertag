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
using Meta.XR.Editor.UserInterface;
using UnityEditor;
using UnityEngine;
using static Meta.XR.Editor.UserInterface.Styles;
using static Meta.XR.Editor.UserInterface.Styles.Colors;
using static Meta.XR.Editor.UserInterface.Styles.Constants;

namespace Meta.XR.Editor.StatusMenu
{
    internal class Item
    {
        public enum Origins
        {
            Unknown = -1,
            Settings,
            Menu,
            StatusMenu,
            Console,
            Component,
            Toolbar
        }

        public struct HeaderIcon
        {
            public Color Color;
            public Action Action;
            public TextureContent TextureContent;

            internal void Show()
            {
                using (new UserInterface.Utils.ColorScope(UserInterface.Utils.ColorScope.Scope.Content, Color))
                {
                    using (new EditorGUI.DisabledScope(Action == null))
                    {
                        if (GUILayout.Button(TextureContent, GUIStyles.MiniButton))
                        {
                            Action?.Invoke();
                        }
                    }
                }
            }
        }

        public delegate (string, Color?) TextDelegate();

        public delegate (TextureContent, Color?, bool) PillIconDelegate();

        public string Name;
        public Color Color;
        public int Order;
        public TextureContent Icon;
        public List<HeaderIcon> HeaderIcons;
        public PillIconDelegate PillIcon;
        public TextDelegate InfoTextDelegate;
        public Action<Origins> OnClickDelegate;
        public bool CloseOnClick = true;

        private Vector2 _headerSize = Vector2.zero;

        public void Show(Action onClick, bool showHeaderIcons, Origins origin)
        {
            var buttonRect = EditorGUILayout.BeginVertical(Styles.GUIStyles.DescriptionAreaStyle);
            var hover = buttonRect.Contains(Event.current.mousePosition);
            {
                var rect = EditorGUILayout.BeginHorizontal();
                {
                    ShowIcon(rect);
                    ShowLabel(hover);

                    if (showHeaderIcons)
                    {
                        ShowHeaderIcons();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            var leftMarginRect = buttonRect;
            leftMarginRect.width = MiniMargin;
            EditorGUI.DrawRect(leftMarginRect, Color);
            EditorGUIUtility.AddCursorRect(buttonRect, MouseCursor.Link);
            if (hover && Event.current.type == EventType.MouseUp)
            {
                OnClickDelegate?.Invoke(origin);
                if (CloseOnClick)
                {
                    onClick?.Invoke();
                }
            }
        }

        internal void ShowHeaderIcons()
        {
            if (HeaderIcons == null) return;

            foreach (var icon in HeaderIcons)
            {
                icon.Show();
            }
        }

        private void ShowLabel(bool hover)
        {
            EditorGUILayout.BeginVertical();
            {
                EditorGUILayout.LabelField(Name, hover ? GUIStyles.BoldLabelHover : GUIStyles.BoldLabel);
                ShowInfoText();
            }
            EditorGUILayout.EndVertical();
        }

        private void ShowInfoText()
        {
            if (InfoTextDelegate == null) return;

            var (content, color) = InfoTextDelegate();
            var style = new GUIStyle(Styles.GUIStyles.SubtitleStyle);
            style.normal.textColor = color ?? LightGray;
            EditorGUILayout.LabelField(content, style);
        }

        private void ShowIcon(Rect rect)
        {
            EditorGUILayout.LabelField(Icon, Styles.GUIStyles.IconStyle, GUILayout.Width(ItemHeight));
            ShowPill(rect);
        }

        private void ShowPill(Rect rect)
        {
            if (PillIcon == null) return;

            var (content, color, _) = PillIcon();

            if (content == null) return;

            rect.x += 16;
            rect.y += 2;
            rect.width = Styles.GUIStyles.PillIconStyle.fixedWidth;
            rect.height = Styles.GUIStyles.PillIconStyle.fixedHeight;
            using (new Utils.ColorScope(Utils.ColorScope.Scope.Content, color ?? Color.white))
            {
                GUI.Label(rect, content, Styles.GUIStyles.PillIconStyle);
            }
        }

        private void UpdateCurrentWidth()
        {
            // Computing the correct width, without access to the current rect
            // Assumption : We're in the middle of rendering the HeaderGUI, in a Vertical block
            _headerSize.y = GUIStyles.Header.fixedHeight;
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginVertical();
            var rect = EditorGUILayout.BeginHorizontal(GUILayout.Height(1));
            EditorGUILayout.LabelField("");
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            _headerSize.x = rect.width != 0.0f ? rect.width : _headerSize.x;
        }

        internal void DrawHeader(float width = 0.0f)
        {
            if (width > 0.0f)
            {
                EditorGUILayout.BeginHorizontal(GUIStyles.Header, GUILayout.Width(width));
            }
            else
            {
                EditorGUILayout.BeginHorizontal(GUIStyles.Header);
            }
            {
                using (new Utils.ColorScope(Utils.ColorScope.Scope.Content, Color))
                {
                    EditorGUILayout.LabelField(Icon, GUIStyles.HeaderIconStyle, GUILayout.Width(32.0f),
                        GUILayout.ExpandWidth(false));
                }
                EditorGUILayout.LabelField(Name, GUIStyles.HeaderLabel);

                EditorGUILayout.Space(0, true);

                ShowHeaderIcons();
            }
            EditorGUILayout.EndHorizontal();
        }

        internal void DrawHeaderFromSettingProvider()
        {
            UpdateCurrentWidth();

            GUILayout.BeginArea(new Rect(0, 0, _headerSize.x, _headerSize.y));
            {
                DrawHeader(_headerSize.x);
            }
            GUILayout.EndArea();
        }
    }
}
