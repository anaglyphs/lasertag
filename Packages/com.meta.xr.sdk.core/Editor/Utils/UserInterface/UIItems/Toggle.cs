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
    internal class Toggle : IUserInterfaceItem
    {
        private VisualElement _visualElement;
        private readonly string _typography;
        private UnityEngine.UIElements.Toggle _uiToggle;

        public bool Hide { get; set; }
        public bool State { get; set; }
        private readonly bool _toggleOnLeft;
        private readonly string _label;
        private readonly Action<bool> _onToggleChanged;
        private readonly GUILayoutOption[] _options;

        public Toggle(string label = "", bool toggleOnLeft = true, Action<bool> onToggleChanged = null) : this(label,
            toggleOnLeft, false, onToggleChanged)
        {
        }

        public Toggle(string label = "", bool toggleOnLeft = true, bool selected = false,
            Action<bool> onToggleChanged = null, params GUILayoutOption[] options)
        {
            _label = label;
            _toggleOnLeft = toggleOnLeft;
            _onToggleChanged = onToggleChanged;
            State = selected;
            _options = options;
        }

        /// <summary>
        /// Constructor to use in UIToolkit based environment
        /// </summary>
        /// <param name="label">Label text for the toggle</param>
        /// <param name="selected">Initial toggle state</param>
        /// <param name="typography"><see cref="RLDS.Props.Typography"/> for the typographic variants</param>
        /// <param name="onToggleChanged">Callback when toggle state changes</param>
        public Toggle(string label, bool selected, string typography, Action<bool> onToggleChanged = null)
        {
            _label = label;
            State = selected;
            _typography = typography;
            _onToggleChanged = onToggleChanged;
        }

        public void Draw()
        {
            var newState = _toggleOnLeft
                ? EditorGUILayout.ToggleLeft(_label, State, EditorStyles.toggle, _options)
                : EditorGUILayout.Toggle(_label, State, EditorStyles.toggle, _options);
            if (newState != State)
            {
                _onToggleChanged?.Invoke(newState);
            }

            State = newState;
        }

        /// <summary>
        /// Creates a UIToolkit Toggle element with RLDS styling applied.
        /// This method provides an alternative to the IMGUI Draw() method for UIToolkit-based workflows.
        /// </summary>
        /// <returns>A VisualElement containing the styled toggle</returns>
        public VisualElement Get()
        {
            _visualElement = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center
                }
            };

            _visualElement.AddToClassList(Props.Toggle.Base);

            _uiToggle = new UnityEngine.UIElements.Toggle
            {
                text = _label,
                value = State
            };

            if (!string.IsNullOrEmpty(_typography))
            {
                _uiToggle.AddToClassList(_typography);
            }

            _uiToggle.RegisterValueChangedCallback(evt =>
            {
                State = evt.newValue;
                _onToggleChanged?.Invoke(evt.newValue);
            });

            _visualElement.Add(_uiToggle);

            return _visualElement;
        }
    }
}
