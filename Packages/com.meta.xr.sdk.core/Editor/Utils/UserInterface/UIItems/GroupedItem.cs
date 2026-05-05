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
    internal class GroupedItem : IUserInterfaceItem
    {
        public List<IUserInterfaceItem> Items { get; set; }

        public bool Hide { get; set; }
        private readonly GUILayoutOption[] _options;
        private readonly Utils.UIItemPlacementType _placementType;
        private readonly string[] _styleClasses = Array.Empty<string>();
        public GUIStyle Style { get; set; }
        private VisualElement _containerElement;
        public VisualElement ContainerElement => _containerElement ??= Get();

        public GroupedItem(IEnumerable<IUserInterfaceItem> items,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Horizontal,
            params GUILayoutOption[] options) :
            this(items, new GUIStyle(), placementType, options)
        {
        }

        public GroupedItem(IEnumerable<IUserInterfaceItem> items, GUIStyle style,
            Utils.UIItemPlacementType placementType = Utils.UIItemPlacementType.Horizontal,
            params GUILayoutOption[] options)
        {
            Items = new List<IUserInterfaceItem>(items);
            Style = style;
            _placementType = placementType;
            _options = options;
        }

        /// <summary>
        /// Constructor to use in UIToolkit based environment
        /// </summary>
        /// <param name="items"></param>
        /// <param name="placementType"></param>
        /// <param name="styleClasses"></param>
        public GroupedItem(Utils.UIItemPlacementType placementType,
            IEnumerable<IUserInterfaceItem> items,
            params string[] styleClasses
            ) : this(items, placementType, Array.Empty<GUILayoutOption>())
        {
            _styleClasses = styleClasses;
        }

        public virtual void Draw()
        {
            if (_placementType == Utils.UIItemPlacementType.Horizontal)
            {
                EditorGUILayout.BeginHorizontal(Style, _options);
            }
            else
            {
                EditorGUILayout.BeginVertical(Style, _options);
            }

            foreach (var item in Items)
            {
                if (item.Hide) continue;
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

        public virtual VisualElement Get()
        {
            if (_containerElement != null)
                return _containerElement;

            _containerElement = new VisualElement();
            var placement = _placementType == Utils.UIItemPlacementType.Horizontal
                ? Props.Flexbox.Row
                : Props.Flexbox.Column;

            _containerElement.AddToClassList(placement);
            _containerElement.AddToClassList(Props.Flexbox.AlignStart);

            foreach (var styleClass in _styleClasses)
            {
                if (!string.IsNullOrEmpty(styleClass))
                {
                    _containerElement.AddToClassList(styleClass);
                }
            }

            foreach (var item in Items)
            {
                _containerElement.Add(item.Get());
            }

            return _containerElement;
        }
    }
}
