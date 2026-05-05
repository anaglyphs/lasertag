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

namespace Meta.XR.Editor.UserInterface
{
    internal class Image : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly GUILayoutOption[] _options;
        private readonly Texture _texture;
        private readonly GUIStyle _style;
        private readonly int _width;
        private readonly int _height;
        private readonly string _ussClassName;
        private ScaleMode _scaleMode;

        public Image(TextureContent content, GUIStyle style, params GUILayoutOption[] options)
        {
            _texture = content.Image;
            _style = style;
            _options = options;
            _width = _texture.width;
            _height = (int)_style.fixedHeight > 0 ? (int)_style.fixedHeight : _texture.height;
            _ussClassName = null;
        }

        public Image(Texture texture, GUIStyle style, params GUILayoutOption[] options)
        {
            _texture = texture;
            _style = style;
            _options = options;
            _width = _texture.width;
            _height = (int)_style.fixedHeight > 0 ? (int)_style.fixedHeight : _texture.height;
            _ussClassName = null;
        }

        /// <summary>
        /// Constructor for UIToolkit usage with explicit width and height.
        /// </summary>
        /// <param name="texture">The texture to display</param>
        /// <param name="width">Width of the image</param>
        /// <param name="height">Height of the image</param>
        /// <param name="ussClassName">Optional USS class name to apply custom styling (e.g., "my-custom-image-style"). If provided, this class will be added to the image container element.</param>
        public Image(Texture texture, int width, int height, ScaleMode scaleMode = ScaleMode.ScaleAndCrop, string ussClassName = null)
        {
            _texture = texture;
            _width = width;
            _height = height;
            _scaleMode = scaleMode;
            _ussClassName = ussClassName;
        }

        public void Draw()
        {
            var ratio = (float)_texture.width / _texture.height;
            var rect = EditorGUILayout.GetControlRect(false, _style.fixedHeight, _style, _options);

            // Border/Background
            var borderRect = Expand(rect, UIStyles.Constants.ImageBorderWidth);
            var background = _style.normal.background;
            if (background != null)
            {
                GUI.DrawTexture(borderRect, background, ScaleMode.StretchToFill, false, 1, GUI.color,
                    Vector4.zero, UIStyles.Constants.RoundedBorderVectors);
            }

            // Actual Image
            GUI.DrawTexture(rect, _texture, ScaleMode.ScaleAndCrop, false, ratio, GUI.color,
                Vector4.zero, UIStyles.Constants.RoundedBorderVectors);
        }

        /// <summary>
        /// Creates a UIToolkit Image element.
        /// This method provides an alternative to the IMGUI Draw() method for UIToolkit-based workflows.
        /// </summary>
        /// <returns>A VisualElement containing the styled image</returns>
        public UnityEngine.UIElements.VisualElement Get()
        {
            var container = new UnityEngine.UIElements.VisualElement();
            container.style.width = _width;
            container.style.height = _height;

            // Apply USS class name if provided
            if (!string.IsNullOrEmpty(_ussClassName))
            {
                container.AddToClassList(_ussClassName);
            }

            // Create the image
            var image = new UnityEngine.UIElements.Image
            {
                image = _texture,
                scaleMode = _scaleMode,
                style =
                {
                    width = new UnityEngine.UIElements.StyleLength(UnityEngine.UIElements.StyleKeyword.Auto),
                    height = new UnityEngine.UIElements.StyleLength(UnityEngine.UIElements.StyleKeyword.Auto),
                    flexGrow = 1
                }
            };

            container.Add(image);

            return container;
        }

        private static Rect Expand(Rect rect, int amount)
        {
            rect.x -= amount;
            rect.width += amount * 2;
            rect.y -= amount;
            rect.height += amount * 2;
            return rect;
        }
    }
}
