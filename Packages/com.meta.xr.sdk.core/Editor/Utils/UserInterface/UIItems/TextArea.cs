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

namespace Meta.XR.Editor.UserInterface
{
    internal class TextArea : TextField
    {
        private readonly int _lines;

        public TextArea(string text, int lines = 3, string placeholder = "", params GUILayoutOption[] options) : base(
            "", text, placeholder, options)
        {
            var layoutOptions = new List<GUILayoutOption>
            {
                GUILayout.Height(lines * EditorStyles.textField.lineHeight + 4),
            };
            layoutOptions.AddRange(options);
            _options = layoutOptions.ToArray();
        }

        /// <summary>
        /// Constructor to use in UIToolkit based environment
        /// </summary>
        /// <param name="label">Label text for the text area</param>
        /// <param name="text">Initial text value</param>
        /// <param name="lines">Number of visible lines for the text area</param>
        /// <param name="placeholder">Placeholder text when empty</param>
        /// <param name="onValueChanged">Callback when text value changes</param>
        public TextArea(string label, string text, int lines, string placeholder = "", Action<string> onValueChanged = null)
            : base(label, text, placeholder, onValueChanged)
        {
            _lines = lines;
        }

        public override void Draw()
        {
            Text = EditorGUILayout.TextArea(Text, _options);
            DrawPlaceholder();
        }

        /// <summary>
        /// Creates a UIToolkit TextField element with multiline support and RLDS styling applied.
        /// This method provides an alternative to the IMGUI Draw() method for UIToolkit-based workflows.
        /// </summary>
        /// <returns>A VisualElement containing the styled multiline text field</returns>
        public override VisualElement Get()
        {
            var visualElement = base.Get();

            // Find the TextField within the container and enable multiline
            var textField = visualElement.Q<UnityEngine.UIElements.TextField>();
            if (textField == null) return visualElement;

            textField.multiline = true;

            if (_lines > 1)
            {
                // Query the actual input element and set its minimum height
                var inputElement = textField.Q(className: "unity-text-field__input");
                if (inputElement != null)
                {
                    inputElement.style.minHeight = _lines * 20;
                }
            }

            return visualElement;
        }
    }
}
