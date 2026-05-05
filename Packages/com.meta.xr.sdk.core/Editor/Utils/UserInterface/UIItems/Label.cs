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
using UnityEngine.UIElements;
using static Meta.XR.Editor.UserInterface.Styles.Constants;

namespace Meta.XR.Editor.UserInterface
{
    /// <summary>
    /// Make a read-only label.
    /// </summary>
    internal class Label : IUserInterfaceItem, IDynamicColorItem
    {
        private VisualElement _visualElement;
        private readonly string _typography;
        private UnityEngine.UIElements.Label _uiLabel;

        public bool Hide { get; set; }
        public GUIContent LabelContent { get; set; }
        public readonly GUIStyle GUIStyle;
        private readonly GUILayoutOption[] _options;
        public Func<IDynamicColorItem, Color> FetchDynamicColor { get; set; }

        public Label(string label, params GUILayoutOption[] options) : this(label, UIStyles.GUIStyles.Label, options)
        {
        }

        public Label(string label, GUIStyle style, params GUILayoutOption[] options)
        {
            LabelContent = new GUIContent(label);
            GUIStyle = new GUIStyle(style);
            _options = options;
        }

        /// <summary>
        /// Constructor to use in UIToolkit based environment
        /// </summary>
        /// <param name="label">Label text to show</param>
        /// <param name="typography"><see cref="RLDS.Props.Typography"/> for the typographic variants</param>
        public Label(string label, string typography)
        {
            LabelContent = new GUIContent(label);
            _typography = typography;
        }

        public void Draw()
        {
            if (FetchDynamicColor != null)
            {
                var expectedColor = FetchDynamicColor?.Invoke(this) ?? Color.white;
                // Splitting the behaviour to not play with color scope in case no color was set at all
                using (new Utils.ColorScope(Utils.ColorScope.Scope.Content, expectedColor))
                {
                    EditorGUILayout.LabelField(LabelContent.text, GUIStyle, _options);
                }
            }
            else
            {
                EditorGUILayout.LabelField(LabelContent.text, GUIStyle, _options);
            }

        }

        public float GetHeight(float contentWidth = UIStyles.Constants.DefaultWidth - LargeMargin) => GUIStyle.CalcHeight(LabelContent, contentWidth);
        public float GetWidth() => GUIStyle.CalcSize(LabelContent).x;

        /// <summary>
        /// Creates a UIToolkit Label element with RLDS styling applied.
        /// This method provides an alternative to the IMGUI Draw() method for UIToolkit-based workflows.
        /// </summary>
        /// <returns>A VisualElement containing the styled label</returns>
        public VisualElement Get()
        {
            if (_visualElement != null)
            {
                return _visualElement;
            }
            _visualElement = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    marginTop = RLDS.Styles.Spacing.Space4XS,
                    marginBottom = RLDS.Styles.Spacing.Space4XS
                }
            };

            _uiLabel = new UnityEngine.UIElements.Label(LabelContent.text);
            _uiLabel.AddToClassList(_typography);
            _uiLabel.style.whiteSpace = WhiteSpace.Normal;
            if (FetchDynamicColor != null)
            {
                _uiLabel.style.color = FetchDynamicColor.Invoke(this);
            }
            _visualElement.Add(_uiLabel);

            return _visualElement;
        }
    }
}
