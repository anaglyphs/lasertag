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
using Meta.XR.Editor.UserInterface.RLDS;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Meta.XR.Editor.UserInterface
{
    internal class TextField : IUserInterfaceItem
    {
        public enum Size
        {
            Large,
            Small
        }

        private VisualElement _visualElement;
        private UnityEngine.UIElements.TextField _uiTextField;
        private UnityEngine.UIElements.Label _errorMessageLabel;
        private UnityEngine.UIElements.Label _characterCountLabel;
        private VisualElement _helperContainer;
        private Action<string> _onValueChanged;

        public bool Hide { get; set; }
        public string Text { get; set; }
        public Size FieldSize { get; set; } = Size.Large;
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsReadOnly { get; set; }
        public bool ShowCharacterCount { get; set; }
        public int MaxCharacters { get; set; }

        protected GUILayoutOption[] _options;
        private readonly string _label;
        private readonly string _placeholder;

        public TextField(string label = "", string text = "", params GUILayoutOption[] options) : this(label, text, "",
            options)
        {
        }

        public TextField(string label = "", string text = "", string placeholder = "", params GUILayoutOption[] options)
        {
            Text = text;
            _label = label;
            _options = options;
            _placeholder = placeholder;
        }

        /// <summary>
        /// Constructor to use in UIToolkit based environment
        /// </summary>
        /// <param name="label">Label text for the text field</param>
        /// <param name="text">Initial text value</param>
        /// <param name="placeholder">Placeholder text when empty</param>
        /// <param name="onValueChanged">Callback when text value changes</param>
        public TextField(string label, string text, string placeholder, Action<string> onValueChanged = null)
        {
            _label = label;
            Text = text;
            _placeholder = placeholder;
            _onValueChanged = onValueChanged;
        }

        public virtual void Draw()
        {
            Text = EditorGUILayout.TextField(_label, Text, _options);
            DrawPlaceholder();
        }

        protected void DrawPlaceholder()
        {
            if (!string.IsNullOrEmpty(Text)) return;
            var pos = new Rect(GUILayoutUtility.GetLastRect());
            var style = new GUIStyle
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(8, 0, 4, 0),
                fontStyle = FontStyle.Italic,
                normal =
                {
                    textColor = Color.grey
                },
                wordWrap = true
            };
            EditorGUI.LabelField(pos, _placeholder, style);
        }

        /// <summary>
        /// Creates a UIToolkit TextField element with RLDS styling applied.
        /// This method provides an alternative to the IMGUI Draw() method for UIToolkit-based workflows.
        /// </summary>
        /// <returns>A VisualElement containing the styled text field</returns>
        public virtual VisualElement Get()
        {
            if (_visualElement != null)
            {
                return _visualElement;
            }

            _visualElement = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Column
                }
            };

            // Apply base RLDS text field styling
            _visualElement.AddToClassList(Props.TextField.Base);

            // Apply size variant
            if (FieldSize == Size.Small)
            {
                _visualElement.AddToClassList(Props.TextField.Small);
            }

            // Apply error state
            if (HasError)
            {
                _visualElement.AddToClassList(Props.TextField.Error);
            }

            // Apply read-only state
            if (IsReadOnly)
            {
                _visualElement.AddToClassList(Props.TextField.ReadOnly);
            }

            _uiTextField = new UnityEngine.UIElements.TextField
            {
                label = _label,
                value = Text
            };

            // Set read-only state on the actual input
            if (IsReadOnly)
            {
                _uiTextField.SetEnabled(false);
            }

            if (!string.IsNullOrEmpty(_placeholder))
            {
#if UNITY_2023_1_OR_NEWER
                _uiTextField.textEdition.placeholder = _placeholder;
#else
                // Unity 2022 and earlier doesn't support placeholder directly in UIToolkit
                // You would need to implement a custom placeholder solution
#endif
            }

            _uiTextField.RegisterValueChangedCallback(evt =>
            {
                Text = evt.newValue;
                _onValueChanged?.Invoke(evt.newValue);
                UpdateCharacterCount();
            });

            _visualElement.Add(_uiTextField);

            // Add helper container if needed (error message or character count)
            if (HasError || ShowCharacterCount)
            {
                _helperContainer = new VisualElement
                {
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        justifyContent = HasError ? Justify.SpaceBetween : Justify.FlexEnd,
                        marginTop = 4
                    }
                };
                _helperContainer.AddToClassList(Props.TextField.HelperContainer);

                if (HasError)
                {
                    _helperContainer.AddToClassList(Props.TextField.HasError);
                }

                // Add error message if present
                if (HasError && !string.IsNullOrEmpty(ErrorMessage))
                {
                    _errorMessageLabel = new UnityEngine.UIElements.Label(ErrorMessage);
                    _errorMessageLabel.AddToClassList(Props.TextField.ErrorMessage);
                    _helperContainer.Add(_errorMessageLabel);
                }

                // Add character counter if enabled
                if (ShowCharacterCount)
                {
                    _characterCountLabel = new UnityEngine.UIElements.Label();
                    _characterCountLabel.AddToClassList(Props.TextField.CharacterCount);
                    UpdateCharacterCount();
                    _helperContainer.Add(_characterCountLabel);
                }

                _visualElement.Add(_helperContainer);

                // Align helper container with input box by calculating label width
                // Schedule this for after layout to get accurate measurements
                _visualElement.RegisterCallback<GeometryChangedEvent>(evt =>
                {
                    AlignHelperContainerWithInput();
                });
            }

            return _visualElement;
        }

        /// <summary>
        /// Updates the character count label text
        /// </summary>
        private void UpdateCharacterCount()
        {
            if (_characterCountLabel != null && ShowCharacterCount)
            {
                var currentLength = string.IsNullOrEmpty(Text) ? 0 : Text.Length;
                if (MaxCharacters > 0)
                {
                    _characterCountLabel.text = $"{currentLength}/{MaxCharacters}";
                }
                else
                {
                    _characterCountLabel.text = $"{currentLength}";
                }
            }
        }

        /// <summary>
        /// Updates the error state and message dynamically
        /// </summary>
        /// <param name="hasError">Whether the field has an error</param>
        /// <param name="errorMessage">Optional error message to display</param>
        public void SetError(bool hasError, string errorMessage = "")
        {
            HasError = hasError;
            ErrorMessage = errorMessage;

            if (_visualElement != null)
            {
                if (hasError)
                {
                    _visualElement.AddToClassList(Props.TextField.Error);
                }
                else
                {
                    _visualElement.RemoveFromClassList(Props.TextField.Error);
                }

                if (_errorMessageLabel != null)
                {
                    _errorMessageLabel.text = errorMessage;
                    _errorMessageLabel.style.display = string.IsNullOrEmpty(errorMessage) ? DisplayStyle.None : DisplayStyle.Flex;
                }
            }
        }

        /// <summary>
        /// Aligns the helper container with the input box by calculating the label width.
        /// This ensures error messages and character counters align with the text input, not the container edge.
        /// </summary>
        private void AlignHelperContainerWithInput()
        {
            if (_helperContainer == null || _uiTextField == null)
                return;

            // Query the TextField's label element
            var labelElement = _uiTextField.Q<UnityEngine.UIElements.Label>();
            if (labelElement != null && !string.IsNullOrEmpty(_label))
            {
                // Get the actual rendered width of the label
                var labelWidth = labelElement.resolvedStyle.width;

                // Apply this as left padding to align with the input box, plus 4px offset
                if (!float.IsNaN(labelWidth) && labelWidth > 0)
                {
                    _helperContainer.style.paddingLeft = labelWidth + 4;
                }
            }
        }
    }
}
