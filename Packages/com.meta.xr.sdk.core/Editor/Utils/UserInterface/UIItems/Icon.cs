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
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static Meta.XR.Editor.UserInterface.Styles.Constants;
using static Meta.XR.Editor.UserInterface.Utils;

namespace Meta.XR.Editor.UserInterface
{
    internal class Icon : IUserInterfaceItem
    {
        public bool Hide { get; set; }
        public string LabelText { get; set; }
        public GUIStyle Style { get; set; }
        public Color Color { get; set; }
        public GUIStyle LabelStyle { get; set; } = UIStyles.GUIStyles.Label;

        private readonly GUILayoutOption[] _options;
        private readonly TextureContent _icon;
        private readonly int _width;
        private readonly int _height;
        private VisualElement _container;

        public Icon(TextureContent icon, params GUILayoutOption[] options) : this(icon, Color.white, "",
            options)
        {
        }

        public Icon(TextureContent icon, Color iconColor, string labelText = "", params GUILayoutOption[] options)
        {
            _icon = icon;
            Color = iconColor;
            LabelText = labelText;

            var layoutOptions = new List<GUILayoutOption>
            {
                GUILayout.Width(SmallIconSize),
                GUILayout.Height(SmallIconSize)
            };

            layoutOptions.AddRange(options);
            _options = layoutOptions.ToArray();
            Style = new GUIStyle(Styles.GUIStyles.IconUIItemStyle);
        }

        public Icon(TextureContent icon, Color iconColor, GUIStyle style, params GUILayoutOption[] options) :
            this(icon, iconColor, string.Empty, options)
        {
            Style = new GUIStyle(style);
        }

        /// <summary>
        /// Constructor for UIToolkit usage with explicit width and height.
        /// </summary>
        /// <param name="icon">The icon texture to display</param>
        /// <param name="iconColor">Tint color for the icon</param>
        /// <param name="width">Width of the icon (defaults to SmallIconSize)</param>
        /// <param name="height">Height of the icon (defaults to SmallIconSize)</param>
        /// <param name="labelText">Optional label text to display next to the icon</param>
        public Icon(TextureContent icon, Color iconColor, int width = (int)SmallIconSize,
            int height = (int)SmallIconSize, string labelText = "") : this(icon, iconColor, labelText)
        {
            _icon = icon;
            Color = iconColor;
            LabelText = labelText;
            _width = width;
            _height = height;
        }

        public void Draw()
        {
            if (LabelTextAvailable)
            {
                EditorGUILayout.BeginHorizontal();
            }

            using (new ColorScope(ColorScope.Scope.Content, Color))
            {
                EditorGUILayout.LabelField(_icon, Style, _options);
            }

            if (LabelTextAvailable)
            {
                new Label(LabelText, LabelStyle).Draw();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// Creates a UIToolkit Image element for the icon with optional label.
        /// This method provides an alternative to the IMGUI Draw() method for UIToolkit-based workflows.
        /// </summary>
        /// <returns>A VisualElement containing the styled icon and optional label</returns>
        public VisualElement Get()
        {
            if (_container != null)
            {
                return _container;
            }

            _container = new VisualElement
            {
                style =
                {
                    flexDirection = LabelTextAvailable ? FlexDirection.Row : FlexDirection.Column,
                    alignItems = Align.Center
                }
            };

            // Create the icon image
            var image = new UnityEngine.UIElements.Image
            {
                image = _icon.Image,
                tintColor = Color,
                style =
                {
                    width = _width,
                    height = _height,
                    flexShrink = 0
                }
            };

            _container.Add(image);

            // Add label if available
            if (LabelTextAvailable)
            {
                var label = new UnityEngine.UIElements.Label(LabelText)
                {
                    style =
                    {
                        marginLeft = 4
                    }
                };
                label.AddToClassList(Props.Typography.Body2SupportingText);
                _container.Add(label);
            }

            return _container;
        }

        private bool LabelTextAvailable => !String.IsNullOrEmpty(LabelText);
    }
}
