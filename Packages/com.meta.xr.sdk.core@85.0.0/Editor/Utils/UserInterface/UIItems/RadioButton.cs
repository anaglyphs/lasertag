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
using UnityEditor;
using UnityEngine;

namespace Meta.XR.Editor.UserInterface
{
    internal class RadioButton : IUserInterfaceItem, IDynamicColorItem
    {
        public string Id { get; }
        public bool Hide { get; set; }
        public Action<string> OnSelect { get; set; }
        public bool State { get; set; }
        public TextureContent NormalIcon { get; set; } = Styles.Contents.RadioButtonIcon;
        public TextureContent SelectedIcon { get; set; } = Styles.Contents.RadioButtonSelectedIcon;
        public Func<IDynamicColorItem, Color> FetchDynamicColor { get; set; }
        public bool HandleOwnClicks { get; set; } = true;

        private readonly string _label;
        private readonly Color _normalColor;
        private readonly Color _selectedColor;

        public RadioButton(string id, string label = "", Action<string> onSelect = null) :
            this(id, Styles.Colors.LightGray, Styles.Colors.LightGray, label, onSelect)
        {
            Id = id;
            _label = label;
            OnSelect = onSelect;
        }

        public RadioButton(string id, Color normalColor, Color selectedColor, string label = "",
            Action<string> onSelect = null)
        {
            Id = id;
            _normalColor = normalColor;
            _selectedColor = selectedColor;
            _label = label;
            OnSelect = onSelect;
        }

        public void Draw()
        {
            var icon = State ? SelectedIcon : NormalIcon;
            var color = State ? _selectedColor : _normalColor;
            if (FetchDynamicColor != null)
            {
                color = FetchDynamicColor?.Invoke(this) ?? color;
            }

            var rect = EditorGUILayout.BeginVertical();
            new Icon(icon, color, _label).Draw();
            EditorGUILayout.EndVertical();

            // Only handle clicks if HandleOwnClicks is true
            if (HandleOwnClicks)
            {
                var hit = HoverHelper.Button(Id, rect, new GUIContent(), GUIStyle.none, out _);
                if (hit && !State)
                {
                    State = true;
                    OnSelect?.Invoke(Id);
                }

                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }
        }

        public bool ToggleState() => State = !State;
    }
}
