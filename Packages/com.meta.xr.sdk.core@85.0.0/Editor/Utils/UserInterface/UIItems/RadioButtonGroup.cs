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
    internal class RadioButtonGroup : IUserInterfaceItem
    {
        public bool Hide { get; set; }

        private readonly List<RadioButton> _buttons;
        private readonly GUIStyle _style;
        private readonly Utils.UIItemPlacementType _placementType;
        private readonly GUILayoutOption[] _options;
        private readonly bool _hasDuplicateRadioButtonId;
        private readonly Action<string> _onRadioButtonSelect;
        private readonly Dictionary<string, RadioButton> _radioButtonsMap = new();
        private string _currentSelection;

        public RadioButtonGroup(IEnumerable<RadioButton> items, Action<string> onRadioButtonSelect,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Vertical,
            params GUILayoutOption[] options) :
            this(items, onRadioButtonSelect, new GUIStyle(), placementType, options)
        {
        }

        public RadioButtonGroup(IEnumerable<RadioButton> items, Action<string> onRadioButtonSelect,
            GUIStyle style,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Vertical,
            params GUILayoutOption[] options)
        {
            _buttons = new List<RadioButton>(items);
            _style = style;
            _placementType = placementType;
            _options = options;
            _onRadioButtonSelect = onRadioButtonSelect;

            // validate that pages have unique id
            _hasDuplicateRadioButtonId = _buttons.Select(p => p.Id).Distinct().Count() != _buttons.Count;

            foreach (var radioButton in _buttons)
            {
                _radioButtonsMap.Add(radioButton.Id, radioButton);
                radioButton.OnSelect = SetSelection;
            }
        }

        public void SetSelection(string id)
        {
            if (!_radioButtonsMap.ContainsKey(id) || _currentSelection == id)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_currentSelection))
            {
                _radioButtonsMap[_currentSelection].State = false;
            }

            _radioButtonsMap[id].State = true;
            _currentSelection = id;
            _onRadioButtonSelect?.Invoke(id);
        }

        public void ClearSelection()
        {
            if (!string.IsNullOrEmpty(_currentSelection))
            {
                _radioButtonsMap[_currentSelection].State = false;
                _currentSelection = "";
            }
        }

        public void Draw()
        {
            if (_hasDuplicateRadioButtonId) return;

            if (_placementType == Utils.UIItemPlacementType.Horizontal)
            {
                EditorGUILayout.BeginHorizontal(_style, _options);
            }
            else
            {
                EditorGUILayout.BeginVertical(_style, _options);
            }

            foreach (var item in _buttons.Where(item => !item.Hide))
            {
                item.Draw();
            }

            if (_placementType == Utils.UIItemPlacementType.Horizontal)
            {
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.EndVertical();
            }
        }
    }
}
